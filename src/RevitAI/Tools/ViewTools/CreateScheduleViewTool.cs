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
using RevitAI.Tools.ReadTools.Helpers;

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that creates a schedule view for a specified category with configurable fields.
/// </summary>
public sealed class CreateScheduleViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateScheduleViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the schedule"
                    },
                    "category": {
                        "type": "string",
                        "description": "Category to schedule (e.g., Walls, Doors, Structural Columns)"
                    },
                    "fields": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "Parameter names to include as columns (case-insensitive)"
                    }
                },
                "required": ["name", "category", "fields"],
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

    public string Name => "create_schedule_view";

    public string Description =>
        "Creates a schedule view for the specified category with the specified fields. " +
        "Use get_available_types to see schedulable categories. Common fields: Type, Family, Level, Count, Area, Length. " +
        "After creation, use switch_view with the returned view ID to open the new schedule.";

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

        if (!input.TryGetProperty("category", out var categoryElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: category"));

        if (!input.TryGetProperty("fields", out var fieldsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: fields"));

        var viewName = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(viewName))
            return Task.FromResult(ToolResult.Error("Parameter 'name' cannot be empty."));

        // Validate name doesn't contain invalid characters
        if (viewName.Contains(':'))
            return Task.FromResult(ToolResult.Error(
                "View names cannot contain colons (:). Please remove the colon from the name."));

        var categoryStr = categoryElement.GetString();
        if (string.IsNullOrWhiteSpace(categoryStr))
            return Task.FromResult(ToolResult.Error("Parameter 'category' cannot be empty."));

        var fields = fieldsElement.EnumerateArray()
            .Select(f => f.GetString())
            .Where(f => !string.IsNullOrEmpty(f))
            .Cast<string>()
            .ToList();

        if (fields.Count == 0)
            return Task.FromResult(ToolResult.Error("Parameter 'fields' must contain at least one field name."));

        // Resolve category
        if (!CategoryHelper.TryGetCategory(categoryStr, out var builtInCategory))
            return Task.FromResult(ToolResult.Error(CategoryHelper.GetInvalidCategoryError(categoryStr)));

        try
        {
            var category = Category.GetCategory(doc, builtInCategory);
            if (category == null)
                return Task.FromResult(ToolResult.Error($"Category '{categoryStr}' not found in document."));

            // Create the schedule
            var schedule = ViewSchedule.CreateSchedule(doc, category.Id);

            // Try to set the name
            try
            {
                schedule.Name = viewName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A schedule named '{viewName}' already exists. Please choose a different name."));
            }

            // Add fields
            var definition = schedule.Definition;
            var addedFields = new List<string>();
            var failedFields = new List<string>();

            // Get available schedulable fields
            var schedulableFields = definition.GetSchedulableFields();

            foreach (var fieldName in fields)
            {
                var schedulableField = schedulableFields
                    .FirstOrDefault(f => f.GetName(doc).Equals(fieldName, StringComparison.OrdinalIgnoreCase));

                if (schedulableField != null)
                {
                    try
                    {
                        definition.AddField(schedulableField);
                        addedFields.Add(schedulableField.GetName(doc));
                    }
                    catch
                    {
                        failedFields.Add(fieldName);
                    }
                }
                else
                {
                    failedFields.Add(fieldName);
                }
            }

            var result = new CreateScheduleResult
            {
                CreatedViewId = schedule.Id.Value,
                Name = schedule.Name,
                Category = CategoryHelper.GetDisplayName(builtInCategory),
                AddedFields = addedFields,
                FailedFields = failedFields.Count > 0 ? failedFields : null,
                ViewType = "Schedule"
            };

            // Add a warning if some fields failed
            if (failedFields.Count > 0)
            {
                result.Warning = $"Could not add {failedFields.Count} field(s). Use schedule field editor to see available fields.";
            }

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create schedule view: {ex.Message}"));
        }
    }

    private sealed class CreateScheduleResult
    {
        public long CreatedViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public List<string> AddedFields { get; set; } = new();
        public List<string>? FailedFields { get; set; }
        public string ViewType { get; set; } = string.Empty;
        public string? Warning { get; set; }
    }
}
