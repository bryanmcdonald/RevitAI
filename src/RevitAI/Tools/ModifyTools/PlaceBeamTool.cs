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
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitAI.Services;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a structural beam between two 3D points.
/// Supports grid intersections, raw coordinates, or a mix of both.
/// </summary>
public sealed class PlaceBeamTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceBeamTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "start": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 3,
                        "maxItems": 3,
                        "description": "Start point [x, y, z] in feet. Optional if start_grid_intersection is provided."
                    },
                    "end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 3,
                        "maxItems": 3,
                        "description": "End point [x, y, z] in feet. Optional if end_grid_intersection is provided."
                    },
                    "start_grid_intersection": {
                        "type": "object",
                        "properties": {
                            "grid1": { "type": "string", "description": "Name of the first grid." },
                            "grid2": { "type": "string", "description": "Name of the second grid." }
                        },
                        "required": ["grid1", "grid2"],
                        "additionalProperties": false,
                        "description": "Start point at the intersection of two grids. Z comes from the level elevation."
                    },
                    "end_grid_intersection": {
                        "type": "object",
                        "properties": {
                            "grid1": { "type": "string", "description": "Name of the first grid." },
                            "grid2": { "type": "string", "description": "Name of the second grid." }
                        },
                        "required": ["grid1", "grid2"],
                        "additionalProperties": false,
                        "description": "End point at the intersection of two grids. Z comes from the level elevation."
                    },
                    "beam_type": {
                        "type": "string",
                        "description": "Beam type name (e.g., 'W12x26' or 'W-Wide Flange: W12x26'). Supports fuzzy matching."
                    },
                    "level": {
                        "type": "string",
                        "description": "Reference level name for the beam. Optional - defaults to the active view's level."
                    }
                },
                "required": ["beam_type"],
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

    public string Name => "place_beam";

    public string Description => "Places a structural beam between two points. Accepts grid intersections or raw [x,y,z] coordinates for start/end. Level defaults to active view. Type name supports fuzzy matching.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var beamType = input.TryGetProperty("beam_type", out var typeElem) ? typeElem.GetString() ?? "unknown" : "unknown";
        var level = input.TryGetProperty("level", out var levelElem) ? levelElem.GetString() ?? "active view level" : "active view level";

        var startDesc = DescribeEndpoint(input, "start", "start_grid_intersection");
        var endDesc = DescribeEndpoint(input, "end", "end_grid_intersection");

        return $"Would place a '{beamType}' beam from {startDesc} to {endDesc} on {level}.";
    }

    private static string DescribeEndpoint(JsonElement input, string coordProp, string gridProp)
    {
        if (input.TryGetProperty(gridProp, out var gridElem))
        {
            var g1 = gridElem.TryGetProperty("grid1", out var g1E) ? g1E.GetString() : "?";
            var g2 = gridElem.TryGetProperty("grid2", out var g2E) ? g2E.GetString() : "?";
            return $"grid {g1}/{g2}";
        }

        if (input.TryGetProperty(coordProp, out var coordElem))
        {
            var coords = coordElem.EnumerateArray().ToList();
            if (coords.Count == 3)
                return $"({coords[0].GetDouble():F1}, {coords[1].GetDouble():F1}, {coords[2].GetDouble():F1})";
        }

        return "unknown";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("beam_type", out var beamTypeElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: beam_type"));

        try
        {
            // ── Resolve level ────────────────────────────────────────
            Level? level = null;
            if (input.TryGetProperty("level", out var levelElement))
            {
                var levelName = levelElement.GetString();
                if (!string.IsNullOrWhiteSpace(levelName))
                {
                    level = ElementLookupHelper.FindLevelByName(doc, levelName);
                    if (level == null)
                    {
                        var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Level '{levelName}' not found. Available levels: {availableLevels}"));
                    }
                }
            }

            if (level == null)
            {
                level = ElementLookupHelper.InferLevelFromActiveView(app);
                if (level == null)
                {
                    var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                    return Task.FromResult(ToolResult.Error(
                        $"No level specified and cannot infer from active view. Available levels: {availableLevels}"));
                }
            }

            // ── Resolve start point ──────────────────────────────────
            var (startPoint, startResolvedFrom, startError) = ResolveEndpoint(
                doc, input, "start", "start_grid_intersection", level.Elevation);
            if (startPoint == null)
                return Task.FromResult(ToolResult.Error(startError!));

            // ── Resolve end point ────────────────────────────────────
            var (endPoint, endResolvedFrom, endError) = ResolveEndpoint(
                doc, input, "end", "end_grid_intersection", level.Elevation);
            if (endPoint == null)
                return Task.FromResult(ToolResult.Error(endError!));

            // Validate points are different
            var distance = startPoint.DistanceTo(endPoint);
            if (distance < 0.01)
                return Task.FromResult(ToolResult.Error("start and end points must be at least 0.01 feet apart."));

            // ── Find beam type (fuzzy) ───────────────────────────────
            var beamTypeName = beamTypeElement.GetString();
            if (string.IsNullOrWhiteSpace(beamTypeName))
                return Task.FromResult(ToolResult.Error("beam_type cannot be empty."));

            var (beamSymbol, isFuzzy, matchedName) = ElementLookupHelper.FindFamilySymbolInCategoryFuzzy(
                doc, BuiltInCategory.OST_StructuralFraming, beamTypeName);

            if (beamSymbol == null)
            {
                var availableTypes = ElementLookupHelper.GetAvailableTypeNames(doc, BuiltInCategory.OST_StructuralFraming);
                return Task.FromResult(ToolResult.Error(
                    $"Beam type '{beamTypeName}' not found. Available types: {availableTypes}"));
            }

            // Activate symbol if not already active
            if (!beamSymbol.IsActive)
            {
                beamSymbol.Activate();
                doc.Regenerate();
            }

            // Create beam
            var beamLine = Line.CreateBound(startPoint, endPoint);
            var beam = doc.Create.NewFamilyInstance(
                beamLine, beamSymbol, level, StructuralType.Beam);

            // Build result
            var resolvedFrom = BuildResolvedFromNote(startResolvedFrom, endResolvedFrom);
            var msg = $"Created {distance:F2}' beam on {level.Name}.";
            if (resolvedFrom != null)
                msg += $" {resolvedFrom}";
            if (isFuzzy)
                msg += $" Type fuzzy-matched to '{matchedName}'.";

            var result = new PlaceBeamResult
            {
                BeamId = beam.Id.Value,
                BeamType = $"{beamSymbol.Family?.Name}: {beamSymbol.Name}",
                Level = level.Name,
                Length = Math.Round(distance, 4),
                Start = new[] { startPoint.X, startPoint.Y, startPoint.Z },
                End = new[] { endPoint.X, endPoint.Y, endPoint.Z },
                ResolvedFrom = resolvedFrom,
                FuzzyMatched = isFuzzy ? matchedName : null,
                Message = msg
            };

            return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), new[] { beam.Id.Value }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static (XYZ? Point, string? ResolvedFrom, string? Error) ResolveEndpoint(
        Document doc, JsonElement input, string coordProp, string gridProp, double levelElevation)
    {
        if (input.TryGetProperty(gridProp, out var gridElem))
        {
            var g1 = gridElem.GetProperty("grid1").GetString()!;
            var g2 = gridElem.GetProperty("grid2").GetString()!;
            var (point, error) = GeometryResolver.ResolveGridIntersection(doc, g1, g2);
            if (point == null)
                return (null, null, error);
            // Use level elevation for Z
            var point3d = new XYZ(point.X, point.Y, levelElevation);
            return (point3d, $"grid {g1}/{g2}", null);
        }

        if (input.TryGetProperty(coordProp, out var coordElem))
        {
            var coords = coordElem.EnumerateArray().ToList();
            if (coords.Count != 3)
                return (null, null, $"{coordProp} must be an array of exactly 3 numbers [x, y, z].");
            var point = new XYZ(coords[0].GetDouble(), coords[1].GetDouble(), coords[2].GetDouble());
            return (point, null, null);
        }

        return (null, null, $"Must provide either {coordProp} or {gridProp}.");
    }

    private static string? BuildResolvedFromNote(string? startResolved, string? endResolved)
    {
        if (startResolved != null && endResolved != null)
            return $"Start resolved from {startResolved}, end from {endResolved}.";
        if (startResolved != null)
            return $"Start resolved from {startResolved}.";
        if (endResolved != null)
            return $"End resolved from {endResolved}.";
        return null;
    }

    private sealed class PlaceBeamResult
    {
        public long BeamId { get; set; }
        public string BeamType { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public double Length { get; set; }
        public double[] Start { get; set; } = Array.Empty<double>();
        public double[] End { get; set; } = Array.Empty<double>();
        public string? ResolvedFrom { get; set; }
        public string? FuzzyMatched { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
