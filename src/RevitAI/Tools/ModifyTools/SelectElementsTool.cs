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
/// Tool that selects elements in Revit by their element IDs.
/// </summary>
public sealed class SelectElementsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static SelectElementsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to select. Empty array clears selection."
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

    public string Name => "select_elements";

    public string Description => "Selects elements in Revit by their element IDs. Pass an empty array to clear the selection.";

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

            // If empty array, clear selection
            if (requestedIds.Count == 0)
            {
                uiDoc.Selection.SetElementIds(new List<ElementId>());
                var clearResult = new SelectElementsResult
                {
                    SelectedCount = 0,
                    Message = "Selection cleared."
                };
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(clearResult, _jsonOptions)));
            }

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

            // Set the selection
            uiDoc.Selection.SetElementIds(validIds);

            var result = new SelectElementsResult
            {
                SelectedCount = validIds.Count,
                RequestedCount = requestedIds.Count,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                Message = validIds.Count == requestedIds.Count
                    ? $"Selected {validIds.Count} element(s)."
                    : $"Selected {validIds.Count} of {requestedIds.Count} requested elements. {invalidIds.Count} ID(s) were invalid."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class SelectElementsResult
    {
        public int SelectedCount { get; set; }
        public int? RequestedCount { get; set; }
        public List<long>? InvalidIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
