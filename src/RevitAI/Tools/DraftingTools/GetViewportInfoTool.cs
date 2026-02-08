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
/// Tool that returns detailed viewport information for a specific sheet.
/// Shows each viewport's position, size, view name, and detail number.
/// </summary>
public sealed class GetViewportInfoTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetViewportInfoTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "sheet_id": {
                        "type": "integer",
                        "description": "The ID of the sheet to get viewport information for."
                    }
                },
                "required": ["sheet_id"],
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

    public string Name => "get_viewport_info";

    public string Description => "Returns detailed viewport information for a specific sheet, including position, size, " +
        "view name, and detail number for each viewport. Use get_sheet_list first to find the sheet ID.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("sheet_id", out var sheetIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: sheet_id"));

        try
        {
            var sheetId = new ElementId(sheetIdElement.GetInt64());
            var sheet = doc.GetElement(sheetId) as ViewSheet;
            if (sheet == null)
                return Task.FromResult(ToolResult.Error($"Sheet with ID {sheetIdElement.GetInt64()} not found."));

            var viewportIds = sheet.GetAllViewports();

            var viewports = new List<ViewportData>();
            foreach (var vpId in viewportIds)
            {
                var viewport = doc.GetElement(vpId) as Viewport;
                if (viewport == null)
                    continue;

                var view = doc.GetElement(viewport.ViewId) as View;
                var center = viewport.GetBoxCenter();
                var outline = viewport.GetBoxOutline();

                var vpData = new ViewportData
                {
                    ViewportId = viewport.Id.Value,
                    ViewId = viewport.ViewId.Value,
                    ViewName = view?.Name ?? "(unknown)",
                    ViewType = view?.ViewType.ToString() ?? "(unknown)",
                    DetailNumber = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString(),
                    Center = new[] { Math.Round(center.X, 4), Math.Round(center.Y, 4) },
                    OutlineMin = new[] { Math.Round(outline.MinimumPoint.X, 4), Math.Round(outline.MinimumPoint.Y, 4) },
                    OutlineMax = new[] { Math.Round(outline.MaximumPoint.X, 4), Math.Round(outline.MaximumPoint.Y, 4) }
                };

                viewports.Add(vpData);
            }

            var result = new GetViewportInfoResult
            {
                SheetId = sheet.Id.Value,
                SheetNumber = sheet.SheetNumber,
                SheetName = sheet.Name,
                Viewports = viewports,
                ViewportCount = viewports.Count
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get viewport info: {ex.Message}"));
        }
    }

    private sealed class ViewportData
    {
        public long ViewportId { get; set; }
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string? DetailNumber { get; set; }
        public double[] Center { get; set; } = Array.Empty<double>();
        public double[] OutlineMin { get; set; } = Array.Empty<double>();
        public double[] OutlineMax { get; set; } = Array.Empty<double>();
    }

    private sealed class GetViewportInfoResult
    {
        public long SheetId { get; set; }
        public string SheetNumber { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public List<ViewportData> Viewports { get; set; } = new();
        public int ViewportCount { get; set; }
    }
}
