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
/// Tool that creates a new drafting view (2D detail view not associated with model geometry).
/// </summary>
public sealed class CreateDraftingViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateDraftingViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the drafting view"
                    },
                    "scale": {
                        "type": "integer",
                        "description": "View scale (e.g., 48 for 1/4\" = 1'-0\", 96 for 1/8\" = 1'-0\")",
                        "default": 48
                    },
                    "view_family_type_id": {
                        "type": "integer",
                        "description": "Optional: specific ViewFamilyType ID to use"
                    }
                },
                "required": ["name"],
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

    public string Name => "create_drafting_view";

    public string Description =>
        "Creates a new drafting view (2D detail view not associated with model geometry). " +
        "Useful for creating 2D details, diagrams, and standard details. " +
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
        if (!input.TryGetProperty("name", out var nameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: name"));

        var viewName = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(viewName))
            return Task.FromResult(ToolResult.Error("Parameter 'name' cannot be empty."));

        // Validate name doesn't contain invalid characters
        if (viewName.Contains(':'))
            return Task.FromResult(ToolResult.Error(
                "View names cannot contain colons (:). Please remove the colon from the name."));

        // Get optional scale (default 48)
        var scale = 48;
        if (input.TryGetProperty("scale", out var scaleProp))
        {
            scale = scaleProp.GetInt32();
            if (scale <= 0)
                return Task.FromResult(ToolResult.Error("Scale must be a positive integer."));
        }

        try
        {
            // Get drafting ViewFamilyType
            ViewFamilyType? vft = null;
            if (input.TryGetProperty("view_family_type_id", out var vftIdProp))
            {
                vft = doc.GetElement(new ElementId(vftIdProp.GetInt64())) as ViewFamilyType;
                if (vft == null)
                    return Task.FromResult(ToolResult.Error("Invalid view_family_type_id."));
                if (vft.ViewFamily != ViewFamily.Drafting)
                    return Task.FromResult(ToolResult.Error($"ViewFamilyType {vft.Name} is not a Drafting type."));
            }
            else
            {
                vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
            }

            if (vft == null)
                return Task.FromResult(ToolResult.Error("No drafting view family type found in the document."));

            // Create the drafting view
            var view = ViewDrafting.Create(doc, vft.Id);

            // Try to set the name
            try
            {
                view.Name = viewName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A view named '{viewName}' already exists. Please choose a different name."));
            }

            // Set the scale
            view.Scale = scale;

            var result = new CreateDraftingResult
            {
                CreatedViewId = view.Id.Value,
                Name = view.Name,
                Scale = scale,
                ViewType = "Drafting"
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create drafting view: {ex.Message}"));
        }
    }

    private sealed class CreateDraftingResult
    {
        public long CreatedViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Scale { get; set; }
        public string ViewType { get; set; } = string.Empty;
    }
}
