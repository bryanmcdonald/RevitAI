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
/// Tool that renames an existing view.
/// </summary>
public sealed class RenameViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static RenameViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The element ID of the view to rename"
                    },
                    "new_name": {
                        "type": "string",
                        "description": "The new name for the view"
                    }
                },
                "required": ["view_id", "new_name"],
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

    public string Name => "rename_view";

    public string Description =>
        "Renames an existing view. View names must be unique within the project. " +
        "IMPORTANT: View names cannot contain colons (:), curly braces ({}), or other special characters.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get required parameters
        if (!input.TryGetProperty("view_id", out var viewIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: view_id"));

        if (!input.TryGetProperty("new_name", out var newNameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: new_name"));

        var viewId = new ElementId(viewIdElement.GetInt64());
        var newName = newNameElement.GetString();

        if (string.IsNullOrWhiteSpace(newName))
            return Task.FromResult(ToolResult.Error("Parameter 'new_name' cannot be empty."));

        // Validate name doesn't contain invalid characters
        if (newName.Contains(':'))
            return Task.FromResult(ToolResult.Error(
                "View names cannot contain colons (:). Please remove the colon from the name."));

        if (newName.Contains('{') || newName.Contains('}'))
            return Task.FromResult(ToolResult.Error(
                "View names cannot contain curly braces ({}). Please remove them from the name."));

        try
        {
            var view = doc.GetElement(viewId) as View;
            if (view == null)
                return Task.FromResult(ToolResult.Error($"View with ID {viewId.Value} not found."));

            var oldName = view.Name;

            // Check if the name is actually changing
            if (oldName == newName)
            {
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(new RenameViewResult
                {
                    ViewId = view.Id.Value,
                    OldName = oldName,
                    NewName = newName,
                    ViewType = view.ViewType.ToString(),
                    Message = "View already has this name."
                }, _jsonOptions)));
            }

            // Try to set the name
            try
            {
                view.Name = newName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A view named '{newName}' already exists. Please choose a different name."));
            }

            var result = new RenameViewResult
            {
                ViewId = view.Id.Value,
                OldName = oldName,
                NewName = view.Name,
                ViewType = view.ViewType.ToString()
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to rename view: {ex.Message}"));
        }
    }

    private sealed class RenameViewResult
    {
        public long ViewId { get; set; }
        public string OldName { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string? Message { get; set; }
    }
}
