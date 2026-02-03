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
/// Tool that zooms in or out by a percentage.
/// </summary>
public sealed class ZoomByPercentTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ZoomByPercentTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "percent": {
                        "type": "number",
                        "description": "Zoom level as percentage. Values >100 zoom in (e.g., 200 = 2x zoom in, showing half the area). Values <100 zoom out (e.g., 50 = 2x zoom out, showing double the area). Must be greater than 0."
                    }
                },
                "required": ["percent"],
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

    public string Name => "zoom_by_percent";

    public string Description =>
        "Zooms the current view by a percentage. Values greater than 100 zoom in (200 = 2x zoom in), " +
        "values less than 100 zoom out (50 = 2x zoom out). Works in both 2D and 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get percent parameter
        if (!input.TryGetProperty("percent", out var percentElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: percent"));

        var percent = percentElement.GetDouble();
        if (percent <= 0)
            return Task.FromResult(ToolResult.Error("Percent must be greater than 0."));

        try
        {
            var uiView = GetActiveUIView(uiDoc);
            if (uiView == null)
                return Task.FromResult(ToolResult.Error("Cannot access the active view for zoom operations."));

            // Get current view corners
            var corners = uiView.GetZoomCorners();
            var corner1 = corners[0];
            var corner2 = corners[1];

            // Calculate center point
            var center = new XYZ(
                (corner1.X + corner2.X) / 2,
                (corner1.Y + corner2.Y) / 2,
                (corner1.Z + corner2.Z) / 2
            );

            // Calculate scale factor (100/percent means higher percent = smaller view area = zoom in)
            var scaleFactor = 100.0 / percent;

            // Scale corners from center
            var newCorner1 = ScaleFromCenter(corner1, center, scaleFactor);
            var newCorner2 = ScaleFromCenter(corner2, center, scaleFactor);

            // Apply zoom
            uiView.ZoomAndCenterRectangle(newCorner1, newCorner2);

            var zoomDirection = percent > 100 ? "in" : "out";
            var result = new ZoomByPercentResult
            {
                ViewName = uiDoc.ActiveView.Name,
                Percent = percent,
                Message = $"Zoomed {zoomDirection} to {percent}%."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static UIView? GetActiveUIView(UIDocument uiDoc)
    {
        var uiViews = uiDoc.GetOpenUIViews();
        return uiViews.FirstOrDefault(v => v.ViewId == uiDoc.ActiveView.Id);
    }

    private static XYZ ScaleFromCenter(XYZ point, XYZ center, double scale)
    {
        var offset = point - center;
        return center + offset * scale;
    }

    private sealed class ZoomByPercentResult
    {
        public string ViewName { get; set; } = string.Empty;
        public double Percent { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
