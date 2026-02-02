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
/// Tool that opens a view by name with partial matching support.
/// </summary>
public sealed class OpenViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static OpenViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_name": {
                        "type": "string",
                        "description": "Name of the view to open (partial match supported)"
                    },
                    "view_type": {
                        "type": "string",
                        "description": "View type filter to narrow search",
                        "enum": ["FloorPlan", "CeilingPlan", "Elevation", "Section", "3D", "Schedule", "Sheet", "Drafting", "Legend"]
                    }
                },
                "required": ["view_name"],
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

    public string Name => "open_view";

    public string Description =>
        "Opens a view by name. Searches all views and opens the matching one. " +
        "Optionally filter by view type to avoid ambiguity.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;

        // Get view_name parameter
        if (!input.TryGetProperty("view_name", out var viewNameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: view_name"));

        var viewName = viewNameElement.GetString();
        if (string.IsNullOrWhiteSpace(viewName))
            return Task.FromResult(ToolResult.Error("Parameter 'view_name' cannot be empty."));

        try
        {
            IEnumerable<View> query = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate);

            // Filter by type if specified
            if (input.TryGetProperty("view_type", out var viewTypeProp))
            {
                var viewTypeStr = viewTypeProp.GetString();
                if (!string.IsNullOrEmpty(viewTypeStr))
                {
                    if (!TryParseViewType(viewTypeStr, out var viewType))
                    {
                        return Task.FromResult(ToolResult.Error(
                            $"Unknown view type: '{viewTypeStr}'. Valid types: FloorPlan, CeilingPlan, Elevation, Section, 3D, Schedule, Sheet, Drafting, Legend."));
                    }
                    query = query.Where(v => v.ViewType == viewType);
                }
            }

            // Find matching views (case-insensitive, partial match)
            var matches = query
                .Where(v => v.Name.Contains(viewName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return Task.FromResult(ToolResult.Error($"No view found matching '{viewName}'."));

            View viewToOpen;
            if (matches.Count > 1)
            {
                // Try exact match first
                var exactMatch = matches.FirstOrDefault(v =>
                    v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    viewToOpen = exactMatch;
                }
                else
                {
                    var names = string.Join(", ", matches.Take(10).Select(v => $"'{v.Name}'"));
                    var moreText = matches.Count > 10 ? $" and {matches.Count - 10} more" : "";
                    return Task.FromResult(ToolResult.Error(
                        $"Multiple views match '{viewName}': {names}{moreText}. Please be more specific or use view_type filter."));
                }
            }
            else
            {
                viewToOpen = matches[0];
            }

            uiDoc.ActiveView = viewToOpen;

            var result = new OpenViewResult
            {
                Opened = viewToOpen.Name,
                ViewType = viewToOpen.ViewType.ToString(),
                ViewId = viewToOpen.Id.Value
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to open view: {ex.Message}"));
        }
    }

    private static bool TryParseViewType(string typeStr, out ViewType viewType)
    {
        viewType = typeStr switch
        {
            "FloorPlan" => ViewType.FloorPlan,
            "CeilingPlan" => ViewType.CeilingPlan,
            "Elevation" => ViewType.Elevation,
            "Section" => ViewType.Section,
            "3D" or "ThreeD" => ViewType.ThreeD,
            "Schedule" => ViewType.Schedule,
            "Sheet" => ViewType.DrawingSheet,
            "Drafting" => ViewType.DraftingView,
            "Legend" => ViewType.Legend,
            _ => ViewType.Undefined
        };
        return viewType != ViewType.Undefined || typeStr == "Undefined";
    }

    private sealed class OpenViewResult
    {
        public string Opened { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public long ViewId { get; set; }
    }
}
