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
/// Tool that copies elements with a translation offset.
/// </summary>
public sealed class CopyElementTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CopyElementTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to copy."
                    },
                    "translation": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 3,
                        "maxItems": 3,
                        "description": "Translation vector [x, y, z] in feet for the copies."
                    }
                },
                "required": ["element_ids", "translation"],
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

    public string Name => "copy_element";

    public string Description => "Copies one or more elements with a translation offset [x, y, z] in feet. Returns the new element IDs. Positive X is East, positive Y is North, positive Z is Up.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        if (input.TryGetProperty("translation", out var transElem))
        {
            var coords = transElem.EnumerateArray().ToList();
            if (coords.Count == 3)
            {
                return $"Would copy {count} element(s) offset by ({coords[0].GetDouble():F2}, {coords[1].GetDouble():F2}, {coords[2].GetDouble():F2}) feet.";
            }
        }
        return $"Would copy {count} element(s).";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        if (!input.TryGetProperty("translation", out var translationElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: translation"));

        try
        {
            // Parse element IDs
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
                requestedIds.Add(idElement.GetInt64());

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            // Parse translation vector
            var translationArray = translationElement.EnumerateArray().ToList();
            if (translationArray.Count != 3)
                return Task.FromResult(ToolResult.Error("Translation must be an array of exactly 3 numbers [x, y, z]."));

            var x = translationArray[0].GetDouble();
            var y = translationArray[1].GetDouble();
            var z = translationArray[2].GetDouble();
            var translationVector = new XYZ(x, y, z);

            // Validate element IDs
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();

            foreach (var id in requestedIds)
            {
                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);
                if (element != null)
                    validIds.Add(elementId);
                else
                    invalidIds.Add(id);
            }

            if (validIds.Count == 0)
                return Task.FromResult(ToolResult.Error($"None of the specified element IDs are valid: {string.Join(", ", invalidIds)}"));

            // Copy elements
            var copiedIds = ElementTransformUtils.CopyElements(doc, validIds, translationVector);

            var result = new CopyElementResult
            {
                CopiedCount = copiedIds.Count,
                NewElementIds = copiedIds.Select(id => id.Value).ToList(),
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                Translation = new[] { x, y, z },
                Message = invalidIds.Count == 0
                    ? $"Copied {copiedIds.Count} element(s) offset by ({x:F2}, {y:F2}, {z:F2}) feet."
                    : $"Copied {copiedIds.Count} of {requestedIds.Count} element(s). {invalidIds.Count} ID(s) were invalid."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class CopyElementResult
    {
        public int CopiedCount { get; set; }
        public List<long> NewElementIds { get; set; } = new();
        public List<long>? InvalidIds { get; set; }
        public double[] Translation { get; set; } = Array.Empty<double>();
        public string Message { get; set; } = string.Empty;
    }
}
