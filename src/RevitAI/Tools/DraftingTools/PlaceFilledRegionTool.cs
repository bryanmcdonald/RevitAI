// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Tools.DraftingTools.Helpers;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that creates a filled region with a hatched/solid pattern in a view.
/// Uses a 3-tier type resolution: explicit type name > pattern name match > first available.
/// </summary>
public sealed class PlaceFilledRegionTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceFilledRegionTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the region in. Optional - uses active view if not specified."
                    },
                    "boundary_points": {
                        "type": "array",
                        "items": {
                            "type": "array",
                            "items": { "type": "number" },
                            "minItems": 2,
                            "maxItems": 3
                        },
                        "minItems": 3,
                        "description": "Array of boundary points [[x,y], ...] in feet. Minimum 3 points. Auto-closes if first != last."
                    },
                    "region_type_name": {
                        "type": "string",
                        "description": "Name of the FilledRegionType to use. Optional - if not specified, tries fill_pattern_name match, then first available."
                    },
                    "fill_pattern_name": {
                        "type": "string",
                        "description": "Name of a fill pattern to match a region type by. Used if region_type_name is not specified. Optional."
                    }
                },
                "required": ["boundary_points"],
                "additionalProperties": false
            }
            """;
        _inputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string Name => "place_filled_region";

    public string Description => "Creates a filled region (hatched/solid area) with a boundary in a view. Coordinates are in feet. Use get_fill_patterns to discover available patterns.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var pointCount = input.TryGetProperty("boundary_points", out var pts) ? pts.GetArrayLength() : 0;
        var typeName = input.TryGetProperty("region_type_name", out var t) ? t.GetString() : null;
        var patternName = input.TryGetProperty("fill_pattern_name", out var p) ? p.GetString() : null;

        if (typeName != null)
            return $"Would create a filled region with {pointCount} boundary points using type '{typeName}'.";
        if (patternName != null)
            return $"Would create a filled region with {pointCount} boundary points matching pattern '{patternName}'.";
        return $"Would create a filled region with {pointCount} boundary points using the default type.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Resolve view
            var (view, viewError) = DraftingHelper.ResolveDetailView(doc, input);
            if (viewError != null) return Task.FromResult(viewError);

            // Parse boundary points (min 3)
            var (points, pointsError) = DraftingHelper.ParsePointArray(input, "boundary_points", minPoints: 3);
            if (pointsError != null) return Task.FromResult(pointsError);

            // Build closed CurveLoop
            var (curveLoop, loopError) = DraftingHelper.BuildClosedCurveLoop(points!);
            if (loopError != null) return Task.FromResult(loopError);

            // 3-tier FilledRegionType resolution
            var (regionType, resolveMethod) = ResolveFilledRegionType(doc, input);
            if (regionType == null)
            {
                var available = ElementLookupHelper.GetAvailableFilledRegionTypeNames(doc);
                return Task.FromResult(ToolResult.Error(
                    $"Could not find a suitable filled region type. Available types: {available}"));
            }

            // Create the filled region
            var filledRegion = FilledRegion.Create(doc, regionType.Id, view!.Id, new List<CurveLoop> { curveLoop! });

            var result = new PlaceFilledRegionResult
            {
                ElementIds = new[] { filledRegion.Id.Value },
                ViewId = view.Id.Value,
                ViewName = view.Name,
                RegionTypeName = regionType.Name,
                ResolvedBy = resolveMethod,
                BoundaryPointCount = points!.Count,
                Message = $"Created filled region using type '{regionType.Name}' with {points.Count} boundary points in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { filledRegion.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create filled region: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Resolves a FilledRegionType using a 3-tier strategy:
    /// 1. Explicit region_type_name
    /// 2. Match by fill_pattern_name on foreground pattern
    /// 3. First non-masking type in document
    /// </summary>
    private static (FilledRegionType? Type, string Method) ResolveFilledRegionType(Document doc, JsonElement input)
    {
        var allTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegionType))
            .Cast<FilledRegionType>()
            .ToList();

        // Tier 1: Explicit type name
        if (input.TryGetProperty("region_type_name", out var typeNameElement) && !string.IsNullOrWhiteSpace(typeNameElement.GetString()))
        {
            var typeName = typeNameElement.GetString()!.Trim();
            var match = allTypes.FirstOrDefault(frt =>
                string.Equals(frt.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return (match, "region_type_name");
            return (null, "region_type_name_not_found");
        }

        // Tier 2: Match by fill pattern name
        if (input.TryGetProperty("fill_pattern_name", out var patternNameElement) && !string.IsNullOrWhiteSpace(patternNameElement.GetString()))
        {
            var patternName = patternNameElement.GetString()!.Trim();
            foreach (var frt in allTypes.Where(t => !t.IsMasking))
            {
                var patternId = frt.ForegroundPatternId;
                if (patternId == ElementId.InvalidElementId) continue;

                var pattern = doc.GetElement(patternId) as FillPatternElement;
                if (pattern != null && string.Equals(pattern.Name, patternName, StringComparison.OrdinalIgnoreCase))
                    return (frt, "fill_pattern_name");
            }
            // Pattern name specified but no match found — fail explicitly
            return (null, "fill_pattern_name_not_found");
        }

        // Tier 3: First non-masking type
        var defaultType = allTypes.FirstOrDefault(frt => !frt.IsMasking);
        return defaultType != null ? (defaultType, "default") : (null, "none_available");
    }

    private sealed class PlaceFilledRegionResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string RegionTypeName { get; set; } = string.Empty;
        public string ResolvedBy { get; set; } = string.Empty;
        public int BoundaryPointCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
