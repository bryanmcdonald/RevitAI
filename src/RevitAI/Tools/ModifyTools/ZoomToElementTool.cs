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
/// Tool that zooms the view to frame specified elements.
/// </summary>
public sealed class ZoomToElementTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ZoomToElementTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to zoom to."
                    }
                },
                "required": ["element_ids"],
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

    public string Name => "zoom_to_element";

    public string Description => "Zooms the current view to frame the specified elements, making them visible and centered in the view.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        var doc = uiDoc?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get element_ids parameter
        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        try
        {
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
            {
                requestedIds.Add(idElement.GetInt64());
            }

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            // Filter to valid element IDs
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();

            foreach (var id in requestedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);

                if (element != null)
                {
                    validIds.Add(elementId);
                }
                else
                {
                    invalidIds.Add(id);
                }
            }

            if (validIds.Count == 0)
                return Task.FromResult(ToolResult.Error($"None of the specified element IDs are valid: {string.Join(", ", invalidIds)}"));

            // Zoom to the elements
            uiDoc.ShowElements(validIds);

            var result = new ZoomToElementResult
            {
                ZoomedToCount = validIds.Count,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                Message = invalidIds.Count == 0
                    ? $"Zoomed to {validIds.Count} element(s)."
                    : $"Zoomed to {validIds.Count} element(s). {invalidIds.Count} ID(s) were invalid."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class ZoomToElementResult
    {
        public int ZoomedToCount { get; set; }
        public List<long>? InvalidIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
