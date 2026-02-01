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
/// Tool that places a wall between two points.
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
                        "description": "Start point [x, y] in feet."
                    },
                    "end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "End point [x, y] in feet."
                    },
                    "base_level": {
                        "type": "string",
                        "description": "Name of the base level (e.g., 'Level 1')."
                    },
                    "wall_type": {
                        "type": "string",
                        "description": "Wall type name (e.g., 'Basic Wall: Generic - 8\"'). Optional - uses default if not specified."
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
                "required": ["start", "end", "base_level"],
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

    public string Description => "Places a wall between two points. Coordinates are in feet. Use get_levels to see available levels and get_available_types with 'Walls' to see wall types.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var baseLevel = input.TryGetProperty("base_level", out var levelElem) ? levelElem.GetString() ?? "unknown" : "unknown";
        var wallType = input.TryGetProperty("wall_type", out var typeElem) ? typeElem.GetString() : null;

        // Calculate length if possible
        double? length = null;
        if (input.TryGetProperty("start", out var startElem) && input.TryGetProperty("end", out var endElem))
        {
            var start = startElem.EnumerateArray().ToList();
            var end = endElem.EnumerateArray().ToList();
            if (start.Count == 2 && end.Count == 2)
            {
                var dx = end[0].GetDouble() - start[0].GetDouble();
                var dy = end[1].GetDouble() - start[1].GetDouble();
                length = Math.Sqrt(dx * dx + dy * dy);
            }
        }

        if (wallType != null && length.HasValue)
        {
            return $"Would place a {length.Value:F2}' '{wallType}' wall on {baseLevel}.";
        }
        else if (length.HasValue)
        {
            return $"Would place a {length.Value:F2}' wall on {baseLevel}.";
        }
        return $"Would place a wall on {baseLevel}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get required parameters
        if (!input.TryGetProperty("start", out var startElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: start"));

        if (!input.TryGetProperty("end", out var endElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: end"));

        if (!input.TryGetProperty("base_level", out var baseLevelElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: base_level"));

        try
        {
            // Parse start point
            var startArray = startElement.EnumerateArray().ToList();
            if (startArray.Count != 2)
                return Task.FromResult(ToolResult.Error("start must be an array of exactly 2 numbers [x, y]."));
            var startX = startArray[0].GetDouble();
            var startY = startArray[1].GetDouble();

            // Parse end point
            var endArray = endElement.EnumerateArray().ToList();
            if (endArray.Count != 2)
                return Task.FromResult(ToolResult.Error("end must be an array of exactly 2 numbers [x, y]."));
            var endX = endArray[0].GetDouble();
            var endY = endArray[1].GetDouble();

            // Validate points are different
            if (Math.Abs(startX - endX) < 0.001 && Math.Abs(startY - endY) < 0.001)
                return Task.FromResult(ToolResult.Error("start and end points must be different."));

            // Find level
            var baseLevelName = baseLevelElement.GetString();
            if (string.IsNullOrWhiteSpace(baseLevelName))
                return Task.FromResult(ToolResult.Error("base_level cannot be empty."));

            var level = ElementLookupHelper.FindLevelByName(doc, baseLevelName);
            if (level == null)
            {
                var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                return Task.FromResult(ToolResult.Error(
                    $"Level '{baseLevelName}' not found. Available levels: {availableLevels}"));
            }

            // Get optional wall type
            WallType? wallType = null;
            if (input.TryGetProperty("wall_type", out var wallTypeElement))
            {
                var wallTypeName = wallTypeElement.GetString();
                if (!string.IsNullOrWhiteSpace(wallTypeName))
                {
                    wallType = ElementLookupHelper.FindWallTypeByName(doc, wallTypeName);
                    if (wallType == null)
                    {
                        var availableTypes = ElementLookupHelper.GetAvailableWallTypeNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Wall type '{wallTypeName}' not found. Available types: {availableTypes}"));
                    }
                }
            }

            // If no wall type specified, get default
            wallType ??= GetDefaultWallType(doc);
            if (wallType == null)
                return Task.FromResult(ToolResult.Error("No wall types available in the document."));

            // Get optional height
            double height = 10.0; // Default 10 feet
            if (input.TryGetProperty("height", out var heightElement))
            {
                height = heightElement.GetDouble();
                if (height <= 0)
                    return Task.FromResult(ToolResult.Error("height must be greater than 0."));
            }

            // Get optional base offset
            double baseOffset = 0.0;
            if (input.TryGetProperty("base_offset", out var baseOffsetElement))
            {
                baseOffset = baseOffsetElement.GetDouble();
            }

            // Get optional structural flag
            bool structural = false;
            if (input.TryGetProperty("structural", out var structuralElement))
            {
                structural = structuralElement.GetBoolean();
            }

            // Create the wall curve at the level's elevation (offset handled by Wall.Create)
            var startPoint = new XYZ(startX, startY, level.Elevation);
            var endPoint = new XYZ(endX, endY, level.Elevation);
            var line = Line.CreateBound(startPoint, endPoint);

            // Create the wall
            var wall = Wall.Create(doc, line, wallType.Id, level.Id, height, baseOffset, false, structural);

            var wallLength = line.Length;
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
                Message = baseOffset != 0
                    ? $"Created {wallLength:F2}' wall on {level.Name} with {baseOffset:F2}' base offset."
                    : $"Created {wallLength:F2}' wall on {level.Name}."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
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
        public string Message { get; set; } = string.Empty;
    }
}
