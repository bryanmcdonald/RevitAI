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
/// Tool that duplicates an existing view with a new name.
/// </summary>
public sealed class DuplicateViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static DuplicateViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The element ID of the view to duplicate"
                    },
                    "new_name": {
                        "type": "string",
                        "description": "Name for the duplicated view"
                    },
                    "duplicate_option": {
                        "type": "string",
                        "description": "How to duplicate: Duplicate (view only), AsDependent (linked to original), WithDetailing (includes annotations)",
                        "enum": ["Duplicate", "AsDependent", "WithDetailing"],
                        "default": "Duplicate"
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

    public string Name => "duplicate_view";

    public string Description =>
        "Duplicates an existing view with a new name. Options: " +
        "Duplicate (view only), AsDependent (linked to original), WithDetailing (includes annotations). " +
        "After creation, use switch_view with the returned view ID to open the new view.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;

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

        // Get duplicate option (default Duplicate)
        var duplicateOption = ViewDuplicateOption.Duplicate;
        if (input.TryGetProperty("duplicate_option", out var optionProp))
        {
            var optionStr = optionProp.GetString();
            duplicateOption = optionStr switch
            {
                "Duplicate" => ViewDuplicateOption.Duplicate,
                "AsDependent" => ViewDuplicateOption.AsDependent,
                "WithDetailing" => ViewDuplicateOption.WithDetailing,
                _ => ViewDuplicateOption.Duplicate
            };
        }

        try
        {
            var view = doc.GetElement(viewId) as View;
            if (view == null)
                return Task.FromResult(ToolResult.Error($"View with ID {viewId.Value} not found."));

            if (view.IsTemplate)
                return Task.FromResult(ToolResult.Error("Cannot duplicate a view template with this tool."));

            // Check if the view can be duplicated with the selected option
            if (!view.CanViewBeDuplicated(duplicateOption))
            {
                var alternatives = new List<string>();
                if (view.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                    alternatives.Add("Duplicate");
                if (view.CanViewBeDuplicated(ViewDuplicateOption.AsDependent))
                    alternatives.Add("AsDependent");
                if (view.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
                    alternatives.Add("WithDetailing");

                var altText = alternatives.Count > 0
                    ? $" Try: {string.Join(", ", alternatives)}"
                    : " This view type may not support duplication.";

                return Task.FromResult(ToolResult.Error(
                    $"Cannot duplicate view '{view.Name}' with option '{duplicateOption}'.{altText}"));
            }

            // Duplicate the view
            var newViewId = view.Duplicate(duplicateOption);
            var newView = doc.GetElement(newViewId) as View;

            if (newView == null)
                return Task.FromResult(ToolResult.Error("Failed to duplicate view."));

            // Try to set the name
            try
            {
                newView.Name = newName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A view named '{newName}' already exists. Please choose a different name."));
            }

            var result = new DuplicateViewResult
            {
                CreatedViewId = newView.Id.Value,
                Name = newView.Name,
                ViewType = newView.ViewType.ToString(),
                SourceViewId = viewId.Value,
                SourceViewName = view.Name,
                DuplicateOption = duplicateOption.ToString()
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to duplicate view: {ex.Message}"));
        }
    }

    private sealed class DuplicateViewResult
    {
        public long CreatedViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public long SourceViewId { get; set; }
        public string SourceViewName { get; set; } = string.Empty;
        public string DuplicateOption { get; set; } = string.Empty;
    }
}
