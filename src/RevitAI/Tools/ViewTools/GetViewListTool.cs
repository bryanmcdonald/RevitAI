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
/// Tool that lists all views in the project with optional type filtering.
/// </summary>
public sealed class GetViewListTool : IRevitTool
{
    private const int MaxViews = 200;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetViewListTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_type": {
                        "type": "string",
                        "description": "Filter by view type. Options: FloorPlan, CeilingPlan, Elevation, Section, 3D, Schedule, Sheet, Drafting, Legend",
                        "enum": ["FloorPlan", "CeilingPlan", "Elevation", "Section", "3D", "Schedule", "Sheet", "Drafting", "Legend"]
                    }
                },
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

    public string Name => "get_view_list";

    public string Description =>
        "Lists all views in the project. Use to find view IDs for switching, " +
        "or to understand what views are available. Filter by type: " +
        "FloorPlan, CeilingPlan, Elevation, Section, 3D, Schedule, Sheet, Drafting, Legend.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var activeView = app.ActiveUIDocument?.ActiveView;

        try
        {
            IEnumerable<View> query = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate); // Exclude view templates

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

            var views = query
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .Take(MaxViews)
                .Select(v => new ViewData
                {
                    Id = v.Id.Value,
                    Name = v.Name,
                    ViewType = v.ViewType.ToString(),
                    Level = GetLevelName(v),
                    Scale = GetScale(v),
                    IsActive = activeView != null && v.Id == activeView.Id
                })
                .ToList();

            var result = new GetViewListResult
            {
                Views = views,
                Count = views.Count,
                Truncated = views.Count >= MaxViews,
                TruncatedMessage = views.Count >= MaxViews ? $"Showing first {MaxViews} views. Use view_type filter to narrow results." : null
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get view list: {ex.Message}"));
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

    private static string? GetLevelName(View view)
    {
        // GenLevel is available on floor plans, ceiling plans, etc.
        if (view is ViewPlan viewPlan)
        {
            return viewPlan.GenLevel?.Name;
        }
        return null;
    }

    private static int? GetScale(View view)
    {
        try
        {
            // Some view types (like schedules) don't have a meaningful scale
            if (view.ViewType == ViewType.Schedule || view.ViewType == ViewType.Legend)
                return null;
            return view.Scale;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ViewData
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string? Level { get; set; }
        public int? Scale { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class GetViewListResult
    {
        public List<ViewData> Views { get; set; } = new();
        public int Count { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
    }
}
