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

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that isolates specified elements in the current view, hiding all other elements.
/// Supports both temporary and permanent isolation modes.
/// </summary>
public sealed class IsolateElementsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static IsolateElementsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to isolate in the view."
                    },
                    "temporary": {
                        "type": "boolean",
                        "description": "If true (default), uses temporary isolation that can be reset easily. If false, applies permanent isolation requiring undo to revert."
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

    public string Name => "isolate_elements";

    public string Description =>
        "Isolates specified elements in the current view, hiding all other elements. " +
        "By default uses temporary isolation that can be easily reset with reset_visibility. " +
        "Set temporary=false for permanent isolation.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true; // All visibility operations need transactions in Revit 2026

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;
        var activeView = uiDoc.ActiveView;

        // Get element_ids parameter
        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        // Get optional temporary parameter (default true)
        var temporary = true;
        if (input.TryGetProperty("temporary", out var temporaryElement))
        {
            temporary = temporaryElement.GetBoolean();
        }

        try
        {
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
            {
                requestedIds.Add(idElement.GetInt64());
            }

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            // Validate elements exist in document and can be hidden
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();
            var cannotHideIds = new List<long>();

            foreach (var id in requestedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);

                if (element == null)
                {
                    invalidIds.Add(id);
                }
                else if (!element.CanBeHidden(activeView))
                {
                    cannotHideIds.Add(id);
                }
                else
                {
                    validIds.Add(elementId);
                }
            }

            if (validIds.Count == 0)
            {
                var errorMsg = "No valid elements to isolate.";
                if (invalidIds.Count > 0)
                    errorMsg += $" Invalid IDs: {string.Join(", ", invalidIds)}.";
                if (cannotHideIds.Count > 0)
                    errorMsg += $" Cannot isolate in this view: {string.Join(", ", cannotHideIds)}.";
                return Task.FromResult(ToolResult.Error(errorMsg));
            }

            // Perform isolation (transaction is handled by framework since RequiresTransaction = true)
            if (temporary)
            {
                activeView.IsolateElementsTemporary(validIds);
            }
            else
            {
                // Permanent isolation - hide all other visible elements
                var collector = new FilteredElementCollector(doc, activeView.Id);
                var allVisibleIds = collector
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .ToList();

                var idsToHide = new List<ElementId>();
                foreach (var id in allVisibleIds)
                {
                    if (!validIds.Contains(id))
                    {
                        var element = doc.GetElement(id);
                        if (element != null && element.CanBeHidden(activeView))
                        {
                            idsToHide.Add(id);
                        }
                    }
                }

                if (idsToHide.Count > 0)
                {
                    activeView.HideElements(idsToHide);
                }
            }

            var result = new IsolateElementsResult
            {
                IsolatedCount = validIds.Count,
                Temporary = temporary,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                CannotHideIds = cannotHideIds.Count > 0 ? cannotHideIds : null,
                Message = $"Isolated {validIds.Count} element(s) in {(temporary ? "temporary" : "permanent")} mode."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class IsolateElementsResult
    {
        public int IsolatedCount { get; set; }
        public bool Temporary { get; set; }
        public List<long>? InvalidIds { get; set; }
        public List<long>? CannotHideIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
