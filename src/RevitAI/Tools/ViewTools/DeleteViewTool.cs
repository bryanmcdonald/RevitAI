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
/// Tool that deletes a view from the project. Requires user confirmation.
/// </summary>
public sealed class DeleteViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static DeleteViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The element ID of the view to delete"
                    }
                },
                "required": ["view_id"],
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

    public string Name => "delete_view";

    public string Description =>
        "Deletes a view from the project. This action requires confirmation and can be undone with Ctrl+Z.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        if (!input.TryGetProperty("view_id", out var viewIdElement))
            return "Would delete a view (view_id not provided).";

        var viewId = viewIdElement.GetInt64();
        return $"Would delete view with ID {viewId}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;

        // Get required parameter
        if (!input.TryGetProperty("view_id", out var viewIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: view_id"));

        var viewId = new ElementId(viewIdElement.GetInt64());

        try
        {
            var view = doc.GetElement(viewId) as View;
            if (view == null)
                return Task.FromResult(ToolResult.Error($"View with ID {viewId.Value} not found."));

            // Store view info before deletion
            var viewName = view.Name;
            var viewType = view.ViewType.ToString();

            // Check if this is the active view
            if (uiDoc.ActiveView.Id == viewId)
            {
                return Task.FromResult(ToolResult.Error(
                    "Cannot delete the currently active view. Please switch to a different view first."));
            }

            // Delete the view
            doc.Delete(viewId);

            var result = new DeleteViewResult
            {
                DeletedViewId = viewId.Value,
                DeletedViewName = viewName,
                ViewType = viewType,
                Message = $"View '{viewName}' has been deleted. Use Ctrl+Z to undo."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to delete view: {ex.Message}"));
        }
    }

    private sealed class DeleteViewResult
    {
        public long DeletedViewId { get; set; }
        public string DeletedViewName { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
