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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that creates a new legend view.
/// </summary>
public sealed class CreateLegendTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateLegendTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "The name for the new legend view. Cannot contain colons (:)."
                    },
                    "scale": {
                        "type": "integer",
                        "description": "The scale denominator for the legend (e.g. 96 for 1:96). Optional - defaults to 96."
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

    public string Name => "create_legend";

    public string Description =>
        "Creates a new legend view. Legends are used to display symbol keys and annotations " +
        "that explain the symbols used in the project. Use place_legend_component to add family " +
        "symbols to the legend after creation.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var name = input.TryGetProperty("name", out var n) ? n.GetString() : "unknown";
        var scale = input.TryGetProperty("scale", out var s) ? s.GetInt32() : 96;
        return $"Would create legend view '{name}' at scale 1:{scale}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get required name parameter
        if (!input.TryGetProperty("name", out var nameElement) || string.IsNullOrWhiteSpace(nameElement.GetString()))
            return Task.FromResult(ToolResult.Error("Missing required parameter: name"));

        var name = nameElement.GetString()!.Trim();

        if (name.Contains(':'))
            return Task.FromResult(ToolResult.Error(
                "Legend view names cannot contain colons (:). Please remove the colon from the name."));

        // Get optional scale (default 96)
        var scale = 96;
        if (input.TryGetProperty("scale", out var scaleElement))
        {
            scale = scaleElement.GetInt32();
            if (scale <= 0)
                return Task.FromResult(ToolResult.Error("Parameter 'scale' must be a positive integer."));
        }

        try
        {
            // The Revit API does not have a direct "create legend" method.
            // We must duplicate an existing legend view to create a new one.
            var existingLegend = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.ViewType == ViewType.Legend && !v.IsTemplate);

            if (existingLegend == null)
                return Task.FromResult(ToolResult.Error(
                    "No existing legend view found to duplicate. " +
                    "Create a legend view manually in Revit first (View > Legends > Legend), " +
                    "then this tool can create additional legends by duplicating it."));

            // Duplicate the existing legend
            var newViewId = existingLegend.Duplicate(ViewDuplicateOption.Duplicate);
            var legendView = doc.GetElement(newViewId) as View;
            if (legendView == null)
                return Task.FromResult(ToolResult.Error("Failed to duplicate legend view."));

            // Set the name
            try
            {
                legendView.Name = name;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A view named '{name}' already exists. Please choose a different name."));
            }

            // Set the scale
            legendView.Scale = scale;

            // Delete any elements copied from the source legend
            var copiedElements = new FilteredElementCollector(doc, newViewId)
                .WhereElementIsNotElementType()
                .ToElementIds();

            if (copiedElements.Count > 0)
            {
                try { doc.Delete(copiedElements); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { /* view-owned elements can't be deleted */ }
            }

            var result = new CreateLegendResult
            {
                ViewId = legendView.Id.Value,
                ViewName = legendView.Name,
                Scale = scale,
                Message = $"Created legend view '{legendView.Name}' at scale 1:{scale}."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create legend view: {ex.Message}"));
        }
    }

    private sealed class CreateLegendResult
    {
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public int Scale { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
