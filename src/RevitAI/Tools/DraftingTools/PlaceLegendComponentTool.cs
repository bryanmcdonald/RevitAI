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
using RevitAI.Tools.DraftingTools.Helpers;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that places a legend component (family symbol representation) in a legend view.
/// </summary>
public sealed class PlaceLegendComponentTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceLegendComponentTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "legend_view_id": {
                        "type": "integer",
                        "description": "The element ID of the legend view to place the component in."
                    },
                    "family_name": {
                        "type": "string",
                        "description": "Family name of the component. Can be just the family name, or 'Family: Type' format."
                    },
                    "type_name": {
                        "type": "string",
                        "description": "Type name within the family. Optional - if provided, combined with family_name as 'family_name: type_name'."
                    },
                    "location": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Placement location [x, y] in feet within the legend view."
                    },
                    "view_direction": {
                        "type": "string",
                        "enum": ["Plan", "Front", "Back", "Left", "Right"],
                        "description": "The component representation direction. Optional - defaults to Front."
                    }
                },
                "required": ["legend_view_id", "family_name", "location"],
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

    public string Name => "place_legend_component";

    public string Description =>
        "Places a legend component (a family symbol representation) in a legend view. " +
        "Supports fuzzy name matching across all family categories. " +
        "Use create_legend first to create the legend view, then use this tool to add components.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var familyName = input.TryGetProperty("family_name", out var f) ? f.GetString() : "unknown";
        var typeName = input.TryGetProperty("type_name", out var t) ? t.GetString() : null;
        var direction = input.TryGetProperty("view_direction", out var d) ? d.GetString() : "Front";
        var displayName = typeName != null ? $"{familyName}: {typeName}" : familyName;
        return $"Would place legend component '{displayName}' ({direction} view).";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Resolve legend view
            if (!input.TryGetProperty("legend_view_id", out var viewIdElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: legend_view_id"));

            var viewId = new ElementId(viewIdElement.GetInt64());
            var view = doc.GetElement(viewId) as View;
            if (view == null)
                return Task.FromResult(ToolResult.Error($"View with ID {viewIdElement.GetInt64()} not found."));

            if (view.ViewType != ViewType.Legend)
                return Task.FromResult(ToolResult.Error(
                    $"View '{view.Name}' is not a legend view (type: {view.ViewType}). " +
                    "Legend components can only be placed in legend views."));

            // Parse family name
            if (!input.TryGetProperty("family_name", out var familyNameElement) || string.IsNullOrWhiteSpace(familyNameElement.GetString()))
                return Task.FromResult(ToolResult.Error("Missing required parameter: family_name"));
            var familyName = familyNameElement.GetString()!.Trim();

            // Build full name: "Family: Type" if type_name is provided
            var fullName = familyName;
            if (input.TryGetProperty("type_name", out var typeNameElement) && !string.IsNullOrWhiteSpace(typeNameElement.GetString()))
            {
                fullName = $"{familyName}: {typeNameElement.GetString()!.Trim()}";
            }

            // Parse location
            var (location, locationError) = DraftingHelper.ParsePoint(input, "location");
            if (locationError != null) return Task.FromResult(locationError);

            // Find family symbol using cross-category fuzzy matching
            var (symbol, isFuzzy, matchedName) = ElementLookupHelper.FindFamilySymbolFuzzy(doc, fullName);

            if (symbol == null)
                return Task.FromResult(ToolResult.Error(
                    $"Family symbol '{fullName}' not found. Ensure the family is loaded in the project."));

            // Activate if needed
            if (!symbol.IsActive)
                symbol.Activate();

            // Place the instance in the legend view
            var instance = doc.Create.NewFamilyInstance(location!, symbol, view);

            // Try to set view direction (best-effort)
            string? directionNote = null;
            if (input.TryGetProperty("view_direction", out var dirElement))
            {
                var dirString = dirElement.GetString();
                // LEGEND_COMPONENT parameter integer values (verified via RevitLookup):
                // 0=Plan, 1=Front, 2=Back, 3=Left, 4=Right
                var dirValue = dirString switch
                {
                    "Plan" => 0,
                    "Front" => 1,
                    "Back" => 2,
                    "Left" => 3,
                    "Right" => 4,
                    _ => 1
                };

                var legendParam = instance.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
                if (legendParam != null && !legendParam.IsReadOnly)
                {
                    legendParam.Set(dirValue);
                    directionNote = $"View direction set to {dirString}.";
                }
                else
                {
                    directionNote = $"View direction parameter not available for this family; placed with default orientation.";
                }
            }

            var result = new PlaceLegendComponentResult
            {
                ElementIds = new[] { instance.Id.Value },
                ViewId = view.Id.Value,
                ViewName = view.Name,
                FamilyName = symbol.Family?.Name ?? "",
                TypeName = symbol.Name,
                MatchedName = matchedName,
                IsFuzzyMatch = isFuzzy,
                Location = new[] { location!.X, location.Y },
                DirectionNote = directionNote,
                Message = isFuzzy
                    ? $"Placed legend component '{matchedName}' (fuzzy match for '{fullName}') in '{view.Name}'."
                    : $"Placed legend component '{matchedName}' in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { instance.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to place legend component: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceLegendComponentResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string? MatchedName { get; set; }
        public bool IsFuzzyMatch { get; set; }
        public double[] Location { get; set; } = Array.Empty<double>();
        public string? DirectionNote { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
