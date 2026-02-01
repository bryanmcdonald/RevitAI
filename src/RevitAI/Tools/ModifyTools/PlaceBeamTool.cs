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
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a structural beam between two 3D points.
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
                        "description": "Start point [x, y, z] in feet."
                    },
                    "end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 3,
                        "maxItems": 3,
                        "description": "End point [x, y, z] in feet."
                    },
                    "beam_type": {
                        "type": "string",
                        "description": "Beam type name in 'Family: Type' format (e.g., 'W-Wide Flange: W12x26')."
                    },
                    "level": {
                        "type": "string",
                        "description": "Reference level name for the beam."
                    }
                },
                "required": ["start", "end", "beam_type", "level"],
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

    public string Description => "Places a structural beam between two 3D points. Coordinates are in feet. Use get_levels to see available levels and get_available_types with 'Beams' or 'Structural Framing' to see beam types.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var beamType = input.TryGetProperty("beam_type", out var typeElem) ? typeElem.GetString() ?? "unknown" : "unknown";
        var level = input.TryGetProperty("level", out var levelElem) ? levelElem.GetString() ?? "unknown" : "unknown";
        return $"Would place a '{beamType}' beam on {level}.";
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

        if (!input.TryGetProperty("beam_type", out var beamTypeElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: beam_type"));

        if (!input.TryGetProperty("level", out var levelElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: level"));

        try
        {
            // Parse start point
            var startArray = startElement.EnumerateArray().ToList();
            if (startArray.Count != 3)
                return Task.FromResult(ToolResult.Error("start must be an array of exactly 3 numbers [x, y, z]."));
            var startX = startArray[0].GetDouble();
            var startY = startArray[1].GetDouble();
            var startZ = startArray[2].GetDouble();

            // Parse end point
            var endArray = endElement.EnumerateArray().ToList();
            if (endArray.Count != 3)
                return Task.FromResult(ToolResult.Error("end must be an array of exactly 3 numbers [x, y, z]."));
            var endX = endArray[0].GetDouble();
            var endY = endArray[1].GetDouble();
            var endZ = endArray[2].GetDouble();

            // Validate points are different
            var distance = Math.Sqrt(
                Math.Pow(endX - startX, 2) +
                Math.Pow(endY - startY, 2) +
                Math.Pow(endZ - startZ, 2));

            if (distance < 0.01)
                return Task.FromResult(ToolResult.Error("start and end points must be at least 0.01 feet apart."));

            // Find level
            var levelName = levelElement.GetString();
            if (string.IsNullOrWhiteSpace(levelName))
                return Task.FromResult(ToolResult.Error("level cannot be empty."));

            var level = ElementLookupHelper.FindLevelByName(doc, levelName);
            if (level == null)
            {
                var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                return Task.FromResult(ToolResult.Error(
                    $"Level '{levelName}' not found. Available levels: {availableLevels}"));
            }

            // Find beam type
            var beamTypeName = beamTypeElement.GetString();
            if (string.IsNullOrWhiteSpace(beamTypeName))
                return Task.FromResult(ToolResult.Error("beam_type cannot be empty."));

            var beamSymbol = ElementLookupHelper.FindFamilySymbolInCategory(
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

            // Create beam curve
            var startPoint = new XYZ(startX, startY, startZ);
            var endPoint = new XYZ(endX, endY, endZ);
            var beamLine = Line.CreateBound(startPoint, endPoint);

            // Create the beam
            var beam = doc.Create.NewFamilyInstance(
                beamLine,
                beamSymbol,
                level,
                StructuralType.Beam);

            var result = new PlaceBeamResult
            {
                BeamId = beam.Id.Value,
                BeamType = $"{beamSymbol.Family?.Name}: {beamSymbol.Name}",
                Level = level.Name,
                Length = Math.Round(distance, 4),
                Start = new[] { startX, startY, startZ },
                End = new[] { endX, endY, endZ },
                Message = $"Created {distance:F2}' beam on {level.Name}."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceBeamResult
    {
        public long BeamId { get; set; }
        public string BeamType { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public double Length { get; set; }
        public double[] Start { get; set; } = Array.Empty<double>();
        public double[] End { get; set; } = Array.Empty<double>();
        public string Message { get; set; } = string.Empty;
    }
}
