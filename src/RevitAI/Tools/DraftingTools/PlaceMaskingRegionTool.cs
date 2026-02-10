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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that creates a masking (white-out) region in a view.
/// Uses a FilledRegionType where IsMasking is true.
/// </summary>
public sealed class PlaceMaskingRegionTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceMaskingRegionTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the masking region in. Optional - uses active view if not specified."
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

    public string Name => "place_masking_region";

    public string Description => "Creates a masking (white-out) region that hides elements behind it. Coordinates are in feet. Cannot be placed in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var pointCount = input.TryGetProperty("boundary_points", out var pts) ? pts.GetArrayLength() : 0;
        return $"Would create a masking region with {pointCount} boundary points.";
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

            // Find or create a proper masking type (IsMasking + no visible foreground pattern)
            var (maskingType, typeSource) = ResolveMaskingType(doc);
            if (maskingType == null)
                return Task.FromResult(ToolResult.Error(
                    "No masking region type found and could not create one. " +
                    "Create a masking type in Revit's Filled Region Type settings with no foreground pattern."));

            // Create the masking region
            var filledRegion = FilledRegion.Create(doc, maskingType.Id, view!.Id, new List<CurveLoop> { curveLoop! });

            var result = new PlaceMaskingRegionResult
            {
                ElementIds = new[] { filledRegion.Id.Value },
                ViewId = view.Id.Value,
                ViewName = view.Name,
                MaskingTypeName = maskingType.Name,
                BoundaryPointCount = points!.Count,
                Message = $"Created masking region with {points.Count} boundary points in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { filledRegion.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create masking region: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Resolves a masking FilledRegionType with no visible foreground pattern.
    /// If the document only has masking types with patterns, duplicates one and clears it.
    /// </summary>
    private static (FilledRegionType? Type, string Source) ResolveMaskingType(Document doc)
    {
        var allTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegionType))
            .Cast<FilledRegionType>()
            .ToList();

        // Tier 1: Masking type with no foreground pattern (ideal)
        var pure = allTypes.FirstOrDefault(frt =>
            frt.IsMasking && frt.ForegroundPatternId == ElementId.InvalidElementId);
        if (pure != null)
            return (pure, "existing");

        // Tier 2: Masking type exists but has a foreground pattern — duplicate and clear it
        var withPattern = allTypes.FirstOrDefault(frt => frt.IsMasking);
        if (withPattern != null)
        {
            try
            {
                var newType = withPattern.Duplicate("Masking - No Pattern") as FilledRegionType;
                if (newType != null)
                {
                    newType.ForegroundPatternId = ElementId.InvalidElementId;
                    return (newType, "created");
                }
            }
            catch
            {
                // Duplicate or pattern clear failed — fall through to Tier 3
            }
        }

        // Tier 3: No masking types at all — create from any available type
        var baseType = allTypes.FirstOrDefault();
        if (baseType != null)
        {
            try
            {
                var newType = baseType.Duplicate("Masking") as FilledRegionType;
                if (newType != null)
                {
                    newType.IsMasking = true;
                    newType.ForegroundPatternId = ElementId.InvalidElementId;
                    return (newType, "created");
                }
            }
            catch
            {
                // Creation failed
            }
        }

        return (null, "none");
    }

    private sealed class PlaceMaskingRegionResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string MaskingTypeName { get; set; } = string.Empty;
        public int BoundaryPointCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
