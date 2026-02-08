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
using RevitAI.Services;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a wall between two points.
/// Supports grid intersections, raw coordinates, or a mix of both.
/// </summary>
public sealed class PlaceWallTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceWallTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "start": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Start point [x, y] in feet. Optional if start_grid_intersection is provided."
                    },
                    "end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "End point [x, y] in feet. Optional if end_grid_intersection is provided."
                    },
                    "start_grid_intersection": {
                        "type": "object",
                        "properties": {
                            "grid1": { "type": "string", "description": "Name of the first grid." },
                            "grid2": { "type": "string", "description": "Name of the second grid." }
                        },
                        "required": ["grid1", "grid2"],
                        "additionalProperties": false,
                        "description": "Start point at the intersection of two grids."
                    },
                    "end_grid_intersection": {
                        "type": "object",
                        "properties": {
                            "grid1": { "type": "string", "description": "Name of the first grid." },
                            "grid2": { "type": "string", "description": "Name of the second grid." }
                        },
                        "required": ["grid1", "grid2"],
                        "additionalProperties": false,
                        "description": "End point at the intersection of two grids."
                    },
                    "base_level": {
                        "type": "string",
                        "description": "Name of the base level. Optional - defaults to the active view's level."
                    },
                    "wall_type": {
                        "type": "string",
                        "description": "Wall type name (e.g., 'Generic - 8\"'). Supports fuzzy matching. Optional - uses default if not specified."
                    },
                    "height": {
                        "type": "number",
                        "description": "Wall height in feet. Optional - uses default 10 feet if not specified."
                    },
                    "base_offset": {
                        "type": "number",
                        "description": "Offset from the base level in feet. Positive is up. Default is 0."
                    },
                    "structural": {
                        "type": "boolean",
                        "description": "Whether the wall is structural. Default is false."
                    }
                },
                "required": [],
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

    public string Name => "place_wall";

    public string Description => "Places a wall between two points. Accepts grid intersections or raw [x,y] coordinates for start/end. Level defaults to active view. Type name supports fuzzy matching.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var baseLevel = input.TryGetProperty("base_level", out var levelElem) ? levelElem.GetString() ?? "active view level" : "active view level";
        var wallType = input.TryGetProperty("wall_type", out var typeElem) ? typeElem.GetString() : null;

        var startDesc = DescribeEndpoint(input, "start", "start_grid_intersection");
        var endDesc = DescribeEndpoint(input, "end", "end_grid_intersection");

        if (wallType != null)
            return $"Would place a '{wallType}' wall from {startDesc} to {endDesc} on {baseLevel}.";
        return $"Would place a wall from {startDesc} to {endDesc} on {baseLevel}.";
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
            if (coords.Count == 2)
                return $"({coords[0].GetDouble():F1}, {coords[1].GetDouble():F1})";
        }

        return "unknown";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // ── Resolve base level ───────────────────────────────────
            Level? level = null;
            if (input.TryGetProperty("base_level", out var baseLevelElement))
            {
                var baseLevelName = baseLevelElement.GetString();
                if (!string.IsNullOrWhiteSpace(baseLevelName))
                {
                    level = ElementLookupHelper.FindLevelByName(doc, baseLevelName);
                    if (level == null)
                    {
                        var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Level '{baseLevelName}' not found. Available levels: {availableLevels}"));
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
                        $"No base_level specified and cannot infer from active view. Available levels: {availableLevels}"));
                }
            }

            // ── Resolve start point ──────────────────────────────────
            var (startXY, startResolvedFrom, startError) = ResolveEndpoint2D(
                doc, input, "start", "start_grid_intersection");
            if (startXY == null)
                return Task.FromResult(ToolResult.Error(startError!));

            // ── Resolve end point ────────────────────────────────────
            var (endXY, endResolvedFrom, endError) = ResolveEndpoint2D(
                doc, input, "end", "end_grid_intersection");
            if (endXY == null)
                return Task.FromResult(ToolResult.Error(endError!));

            var startX = startXY.Value.x;
            var startY = startXY.Value.y;
            var endX = endXY.Value.x;
            var endY = endXY.Value.y;

            // Validate points are different
            if (Math.Abs(startX - endX) < 0.001 && Math.Abs(startY - endY) < 0.001)
                return Task.FromResult(ToolResult.Error("start and end points must be different."));

            // ── Get wall type (fuzzy) ────────────────────────────────
            WallType? wallType = null;
            bool isFuzzy = false;
            string? matchedName = null;

            if (input.TryGetProperty("wall_type", out var wallTypeElement))
            {
                var wallTypeName = wallTypeElement.GetString();
                if (!string.IsNullOrWhiteSpace(wallTypeName))
                {
                    var (wt, fuzzy, name) = ElementLookupHelper.FindWallTypeByNameFuzzy(doc, wallTypeName);
                    if (wt == null)
                    {
                        var availableTypes = ElementLookupHelper.GetAvailableWallTypeNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Wall type '{wallTypeName}' not found. Available types: {availableTypes}"));
                    }
                    wallType = wt;
                    isFuzzy = fuzzy;
                    matchedName = name;
                }
            }

            wallType ??= GetDefaultWallType(doc);
            if (wallType == null)
                return Task.FromResult(ToolResult.Error("No wall types available in the document."));

            // Get optional parameters
            double height = 10.0;
            if (input.TryGetProperty("height", out var heightElement))
            {
                height = heightElement.GetDouble();
                if (height <= 0)
                    return Task.FromResult(ToolResult.Error("height must be greater than 0."));
            }

            double baseOffset = 0.0;
            if (input.TryGetProperty("base_offset", out var baseOffsetElement))
                baseOffset = baseOffsetElement.GetDouble();

            bool structural = false;
            if (input.TryGetProperty("structural", out var structuralElement))
                structural = structuralElement.GetBoolean();

            // Create the wall
            var startPoint = new XYZ(startX, startY, level.Elevation);
            var endPoint = new XYZ(endX, endY, level.Elevation);
            var line = Line.CreateBound(startPoint, endPoint);

            var wall = Wall.Create(doc, line, wallType.Id, level.Id, height, baseOffset, false, structural);

            var wallLength = line.Length;
            var resolvedFrom = BuildResolvedFromNote(startResolvedFrom, endResolvedFrom);
            var msg = baseOffset != 0
                ? $"Created {wallLength:F2}' wall on {level.Name} with {baseOffset:F2}' base offset."
                : $"Created {wallLength:F2}' wall on {level.Name}.";

            if (resolvedFrom != null)
                msg += $" {resolvedFrom}";
            if (isFuzzy)
                msg += $" Type fuzzy-matched to '{matchedName}'.";

            var result = new PlaceWallResult
            {
                WallId = wall.Id.Value,
                WallType = GetFullTypeName(wallType),
                Level = level.Name,
                BaseOffset = baseOffset,
                Length = Math.Round(wallLength, 4),
                Height = height,
                Structural = structural,
                Start = new[] { startX, startY },
                End = new[] { endX, endY },
                ResolvedFrom = resolvedFrom,
                FuzzyMatched = isFuzzy ? matchedName : null,
                Message = msg
            };

            return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), new[] { wall.Id.Value }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static ((double x, double y)? Point, string? ResolvedFrom, string? Error) ResolveEndpoint2D(
        Document doc, JsonElement input, string coordProp, string gridProp)
    {
        if (input.TryGetProperty(gridProp, out var gridElem))
        {
            var g1 = gridElem.GetProperty("grid1").GetString()!;
            var g2 = gridElem.GetProperty("grid2").GetString()!;
            var (point, error) = GeometryResolver.ResolveGridIntersection(doc, g1, g2);
            if (point == null)
                return (null, null, error);
            return ((point.X, point.Y), $"grid {g1}/{g2}", null);
        }

        if (input.TryGetProperty(coordProp, out var coordElem))
        {
            var coords = coordElem.EnumerateArray().ToList();
            if (coords.Count != 2)
                return (null, null, $"{coordProp} must be an array of exactly 2 numbers [x, y].");
            return ((coords[0].GetDouble(), coords[1].GetDouble()), null, null);
        }

        return (null, null, $"Wall endpoint not specified. Provide either '{coordProp}' (an [x,y] array) or '{gridProp}' (a grid intersection object).");
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

    private static WallType? GetDefaultWallType(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .FirstOrDefault();
    }

    private static string GetFullTypeName(WallType wallType)
    {
        var familyParam = wallType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
        var familyName = familyParam?.AsString();

        return string.IsNullOrEmpty(familyName)
            ? wallType.Name
            : $"{familyName}: {wallType.Name}";
    }

    private sealed class PlaceWallResult
    {
        public long WallId { get; set; }
        public string WallType { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public double BaseOffset { get; set; }
        public double Length { get; set; }
        public double Height { get; set; }
        public bool Structural { get; set; }
        public double[] Start { get; set; } = Array.Empty<double>();
        public double[] End { get; set; } = Array.Empty<double>();
        public string? ResolvedFrom { get; set; }
        public string? FuzzyMatched { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
