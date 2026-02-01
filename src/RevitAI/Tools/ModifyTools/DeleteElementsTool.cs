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
/// Tool that deletes elements by their element IDs.
/// </summary>
public sealed class DeleteElementsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static DeleteElementsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to delete."
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

    public string Name => "delete_elements";

    public string Description => "Deletes elements by their element IDs. Returns information about deleted elements and affected categories. This action can be undone with Ctrl+Z.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

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

            // Gather information about elements before deletion
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();
            var categoriesCounts = new Dictionary<string, int>();

            foreach (var id in requestedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);

                if (element != null)
                {
                    validIds.Add(elementId);
                    var categoryName = element.Category?.Name ?? "Unknown";
                    if (!categoriesCounts.TryGetValue(categoryName, out var count))
                        count = 0;
                    categoriesCounts[categoryName] = count + 1;
                }
                else
                {
                    invalidIds.Add(id);
                }
            }

            if (validIds.Count == 0)
                return Task.FromResult(ToolResult.Error($"None of the specified element IDs are valid: {string.Join(", ", invalidIds)}"));

            // Delete the elements
            var deletedIds = doc.Delete(validIds);

            var result = new DeleteElementsResult
            {
                DeletedCount = validIds.Count,
                TotalAffectedCount = deletedIds.Count,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                CategoriesAffected = categoriesCounts,
                Message = invalidIds.Count == 0
                    ? $"Deleted {validIds.Count} element(s), {deletedIds.Count} total elements affected (including dependent elements)."
                    : $"Deleted {validIds.Count} of {requestedIds.Count} requested elements. {invalidIds.Count} ID(s) were invalid."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class DeleteElementsResult
    {
        public int DeletedCount { get; set; }
        public int TotalAffectedCount { get; set; }
        public List<long>? InvalidIds { get; set; }
        public Dictionary<string, int> CategoriesAffected { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }
}
