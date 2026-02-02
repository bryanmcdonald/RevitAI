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
/// Tool that creates a new floor plan view for a specified level.
/// </summary>
public sealed class CreateFloorPlanViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateFloorPlanViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "level_id": {
                        "type": "integer",
                        "description": "The element ID of the level to create the plan for"
                    },
                    "name": {
                        "type": "string",
                        "description": "Name for the new view"
                    },
                    "view_family_type_id": {
                        "type": "integer",
                        "description": "Optional: specific ViewFamilyType ID to use (get from get_available_types with category='ViewFamilyType')"
                    }
                },
                "required": ["level_id", "name"],
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

    public string Name => "create_floor_plan_view";

    public string Description =>
        "Creates a new floor plan view for the specified level. " +
        "Optionally specify a view family type (use get_available_types with category='ViewFamilyType' to list options). " +
        "IMPORTANT: Always call switch_view immediately after to open the new view.";

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
        if (!input.TryGetProperty("level_id", out var levelIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: level_id"));

        if (!input.TryGetProperty("name", out var nameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: name"));

        var levelId = new ElementId(levelIdElement.GetInt64());
        var viewName = nameElement.GetString();

        if (string.IsNullOrWhiteSpace(viewName))
            return Task.FromResult(ToolResult.Error("Parameter 'name' cannot be empty."));

        // Validate name doesn't contain invalid characters
        if (viewName.Contains(':'))
            return Task.FromResult(ToolResult.Error(
                "View names cannot contain colons (:). Please remove the colon from the name."));

        try
        {
            var level = doc.GetElement(levelId) as Level;
            if (level == null)
                return Task.FromResult(ToolResult.Error($"Level with ID {levelId.Value} not found."));

            // Get ViewFamilyType for floor plans
            ViewFamilyType? vft = null;
            if (input.TryGetProperty("view_family_type_id", out var vftIdProp))
            {
                vft = doc.GetElement(new ElementId(vftIdProp.GetInt64())) as ViewFamilyType;
                if (vft == null)
                    return Task.FromResult(ToolResult.Error("Invalid view_family_type_id."));
                if (vft.ViewFamily != ViewFamily.FloorPlan)
                    return Task.FromResult(ToolResult.Error($"ViewFamilyType {vft.Name} is not a FloorPlan type."));
            }
            else
            {
                // Find default floor plan ViewFamilyType
                vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
            }

            if (vft == null)
                return Task.FromResult(ToolResult.Error("No floor plan view family type found in the document."));

            // Create the view
            var view = ViewPlan.Create(doc, vft.Id, levelId);

            // Try to set the name (may fail if duplicate)
            try
            {
                view.Name = viewName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A view named '{viewName}' already exists. Please choose a different name."));
            }

            var result = new CreateViewResult
            {
                CreatedViewId = view.Id.Value,
                Name = view.Name,
                Level = level.Name,
                ViewType = "FloorPlan"
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create floor plan view: {ex.Message}"));
        }
    }

    private sealed class CreateViewResult
    {
        public long CreatedViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Level { get; set; }
        public string ViewType { get; set; } = string.Empty;
    }
}
