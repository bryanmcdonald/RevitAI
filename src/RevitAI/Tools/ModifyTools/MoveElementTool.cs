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

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that moves an element by a specified translation vector.
/// </summary>
public sealed class MoveElementTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static MoveElementTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_id": {
                        "type": "integer",
                        "description": "The element ID of the element to move."
                    },
                    "translation": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 3,
                        "maxItems": 3,
                        "description": "Translation vector [x, y, z] in feet."
                    }
                },
                "required": ["element_id", "translation"],
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

    public string Name => "move_element";

    public string Description => "Moves a SINGLE element by a specified translation vector [x, y, z] in feet. Positive X is typically East, positive Y is typically North, positive Z is up. For multiple elements, call this tool once per element.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get element_id parameter
        if (!input.TryGetProperty("element_id", out var elementIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_id"));

        // Get translation parameter
        if (!input.TryGetProperty("translation", out var translationElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: translation"));

        try
        {
            var elementId = new ElementId(elementIdElement.GetInt64());
            var element = doc.GetElement(elementId);

            if (element == null)
                return Task.FromResult(ToolResult.Error($"Element with ID {elementId.Value} not found."));

            // Check if element is pinned
            if (element.Pinned)
                return Task.FromResult(ToolResult.Error($"Element {elementId.Value} is pinned and cannot be moved. Unpin it first."));

            // Parse translation vector
            var translationArray = translationElement.EnumerateArray().ToList();
            if (translationArray.Count != 3)
                return Task.FromResult(ToolResult.Error("Translation must be an array of exactly 3 numbers [x, y, z]."));

            var x = translationArray[0].GetDouble();
            var y = translationArray[1].GetDouble();
            var z = translationArray[2].GetDouble();
            var translationVector = new XYZ(x, y, z);

            // Get original location for result
            var originalLocation = GetElementLocation(element);

            // Move the element
            ElementTransformUtils.MoveElement(doc, elementId, translationVector);

            // Get new location for result
            var newLocation = GetElementLocation(element);

            var result = new MoveElementResult
            {
                ElementId = elementId.Value,
                Category = element.Category?.Name ?? "Unknown",
                Translation = new[] { x, y, z },
                OriginalLocation = originalLocation,
                NewLocation = newLocation,
                Message = $"Moved element {elementId.Value} by ({x:F2}, {y:F2}, {z:F2}) feet."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static double[]? GetElementLocation(Element element)
    {
        var location = element.Location;

        if (location is LocationPoint locationPoint)
        {
            var pt = locationPoint.Point;
            return new[] { Math.Round(pt.X, 4), Math.Round(pt.Y, 4), Math.Round(pt.Z, 4) };
        }
        else if (location is LocationCurve locationCurve)
        {
            var curve = locationCurve.Curve;
            var midpoint = curve.Evaluate(0.5, true);
            return new[] { Math.Round(midpoint.X, 4), Math.Round(midpoint.Y, 4), Math.Round(midpoint.Z, 4) };
        }

        return null;
    }

    private sealed class MoveElementResult
    {
        public long ElementId { get; set; }
        public string Category { get; set; } = string.Empty;
        public double[] Translation { get; set; } = Array.Empty<double>();
        public double[]? OriginalLocation { get; set; }
        public double[]? NewLocation { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
