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
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a floor from a boundary of points.
/// </summary>
public sealed class PlaceFloorTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceFloorTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "boundary": {
                        "type": "array",
                        "items": {
                            "type": "array",
                            "items": { "type": "number" },
                            "minItems": 2,
                            "maxItems": 2
                        },
                        "minItems": 3,
                        "description": "Array of [x, y] points defining the floor boundary in feet. Minimum 3 points required. The boundary will be auto-closed."
                    },
                    "level": {
                        "type": "string",
                        "description": "Name of the level for the floor. Optional - defaults to the active view's level."
                    },
                    "floor_type": {
                        "type": "string",
                        "description": "Floor type name (e.g., 'Generic 12\"'). Supports fuzzy matching. Optional - uses default if not specified."
                    },
                    "elevation_offset": {
                        "type": "number",
                        "description": "Offset from the level elevation in feet. Positive is up. Default is 0."
                    },
                    "structural": {
                        "type": "boolean",
                        "description": "Whether the floor is structural. Default is false."
                    }
                },
                "required": ["boundary"],
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

    public string Name => "place_floor";

    public string Description => "Places a floor from a boundary of points. The boundary is automatically closed. Coordinates are in feet. Level defaults to active view. Type name supports fuzzy matching. Use resolve_grid_intersection to get coordinates for grid-aligned boundaries.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var level = input.TryGetProperty("level", out var levelElem) ? levelElem.GetString() ?? "active view level" : "active view level";
        var pointCount = input.TryGetProperty("boundary", out var boundaryElem) ? boundaryElem.GetArrayLength() : 0;
        var floorType = input.TryGetProperty("floor_type", out var typeElem) ? typeElem.GetString() : null;

        if (floorType != null)
        {
            return $"Would place a '{floorType}' floor with {pointCount} boundary points on {level}.";
        }
        return $"Would place a floor with {pointCount} boundary points on {level}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get required parameters
        if (!input.TryGetProperty("boundary", out var boundaryElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: boundary"));

        try
        {
            // Parse boundary points
            var points = new List<XYZ>();
            foreach (var pointElement in boundaryElement.EnumerateArray())
            {
                var coords = pointElement.EnumerateArray().ToList();
                if (coords.Count != 2)
                    return Task.FromResult(ToolResult.Error("Each boundary point must be an array of 2 numbers [x, y]."));

                var x = coords[0].GetDouble();
                var y = coords[1].GetDouble();
                points.Add(new XYZ(x, y, 0)); // Z will be set by level
            }

            if (points.Count < 3)
                return Task.FromResult(ToolResult.Error("boundary must have at least 3 points."));

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

            // ── Get floor type (fuzzy) ───────────────────────────────
            FloorType? floorType = null;
            bool isFuzzy = false;
            string? matchedName = null;

            if (input.TryGetProperty("floor_type", out var floorTypeElement))
            {
                var floorTypeName = floorTypeElement.GetString();
                if (!string.IsNullOrWhiteSpace(floorTypeName))
                {
                    var (ft, fuzzy, name) = ElementLookupHelper.FindFloorTypeByNameFuzzy(doc, floorTypeName);
                    if (ft == null)
                    {
                        var availableTypes = ElementLookupHelper.GetAvailableFloorTypeNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Floor type '{floorTypeName}' not found. Available types: {availableTypes}"));
                    }
                    floorType = ft;
                    isFuzzy = fuzzy;
                    matchedName = name;
                }
            }

            // If no floor type specified, get default
            floorType ??= GetDefaultFloorType(doc);
            if (floorType == null)
                return Task.FromResult(ToolResult.Error("No floor types available in the document."));

            // Get optional elevation offset
            double elevationOffset = 0.0;
            if (input.TryGetProperty("elevation_offset", out var elevationOffsetElement))
            {
                elevationOffset = elevationOffsetElement.GetDouble();
            }

            // Get optional structural flag
            bool structural = false;
            if (input.TryGetProperty("structural", out var structuralElement))
            {
                structural = structuralElement.GetBoolean();
            }

            // Calculate actual elevation
            var actualElevation = level.Elevation + elevationOffset;

            // Adjust points to level elevation and create CurveLoop
            var curveLoop = new CurveLoop();
            for (int i = 0; i < points.Count; i++)
            {
                var startPt = new XYZ(points[i].X, points[i].Y, actualElevation);
                var endPt = new XYZ(points[(i + 1) % points.Count].X, points[(i + 1) % points.Count].Y, actualElevation);

                // Skip if points are the same
                if (startPt.DistanceTo(endPt) < 0.001)
                    continue;

                var line = Line.CreateBound(startPt, endPt);
                curveLoop.Append(line);
            }

            // Validate curve loop
            if (curveLoop.Count() < 3)
                return Task.FromResult(ToolResult.Error("boundary must form a valid closed loop with at least 3 distinct edges."));

            // Create the floor using Revit 2026 API
            var curveLoops = new List<CurveLoop> { curveLoop };
            var floor = Floor.Create(doc, curveLoops, floorType.Id, level.Id, structural, null, elevationOffset);

            // Calculate approximate area (simple polygon area formula)
            var area = CalculatePolygonArea(points);

            var msg = elevationOffset != 0
                ? $"Created floor on {level.Name} with {elevationOffset:F2}' offset, approximately {area:F0} sq ft area."
                : $"Created floor on {level.Name} with approximately {area:F0} sq ft area.";

            if (isFuzzy)
                msg += $" Type fuzzy-matched to '{matchedName}'.";

            var result = new PlaceFloorResult
            {
                FloorId = floor.Id.Value,
                FloorType = GetFullTypeName(floorType),
                Level = level.Name,
                ElevationOffset = elevationOffset,
                PointCount = points.Count,
                ApproximateArea = Math.Round(area, 2),
                Structural = structural,
                FuzzyMatched = isFuzzy ? matchedName : null,
                Message = msg
            };

            return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), new[] { floor.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Invalid floor boundary: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static FloorType? GetDefaultFloorType(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault(ft => ft.IsFoundationSlab == false);
    }

    private static string GetFullTypeName(FloorType floorType)
    {
        var familyParam = floorType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
        var familyName = familyParam?.AsString();

        return string.IsNullOrEmpty(familyName)
            ? floorType.Name
            : $"{familyName}: {floorType.Name}";
    }

    private static double CalculatePolygonArea(List<XYZ> points)
    {
        // Shoelace formula for polygon area
        double area = 0;
        int n = points.Count;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += points[i].X * points[j].Y;
            area -= points[j].X * points[i].Y;
        }

        return Math.Abs(area) / 2.0;
    }

    private sealed class PlaceFloorResult
    {
        public long FloorId { get; set; }
        public string FloorType { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public double ElevationOffset { get; set; }
        public int PointCount { get; set; }
        public double ApproximateArea { get; set; }
        public bool Structural { get; set; }
        public string? FuzzyMatched { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
