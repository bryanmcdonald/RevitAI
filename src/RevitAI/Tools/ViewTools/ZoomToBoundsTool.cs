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
/// Tool that zooms the view to specific coordinate bounds.
/// </summary>
public sealed class ZoomToBoundsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ZoomToBoundsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "min_x": {
                        "type": "number",
                        "description": "Minimum X coordinate in feet."
                    },
                    "min_y": {
                        "type": "number",
                        "description": "Minimum Y coordinate in feet."
                    },
                    "min_z": {
                        "type": "number",
                        "description": "Minimum Z coordinate in feet (optional, for 3D views)."
                    },
                    "max_x": {
                        "type": "number",
                        "description": "Maximum X coordinate in feet."
                    },
                    "max_y": {
                        "type": "number",
                        "description": "Maximum Y coordinate in feet."
                    },
                    "max_z": {
                        "type": "number",
                        "description": "Maximum Z coordinate in feet (optional, for 3D views)."
                    }
                },
                "required": ["min_x", "min_y", "max_x", "max_y"],
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

    public string Name => "zoom_to_bounds";

    public string Description =>
        "Zooms the current view to show the specified coordinate bounds. " +
        "Coordinates are in feet. Works in both 2D and 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get required parameters
        if (!input.TryGetProperty("min_x", out var minXElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: min_x"));
        if (!input.TryGetProperty("min_y", out var minYElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: min_y"));
        if (!input.TryGetProperty("max_x", out var maxXElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: max_x"));
        if (!input.TryGetProperty("max_y", out var maxYElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: max_y"));

        var minX = minXElement.GetDouble();
        var minY = minYElement.GetDouble();
        var maxX = maxXElement.GetDouble();
        var maxY = maxYElement.GetDouble();

        // Get optional Z coordinates
        var minZ = input.TryGetProperty("min_z", out var minZElement) ? minZElement.GetDouble() : 0;
        var maxZ = input.TryGetProperty("max_z", out var maxZElement) ? maxZElement.GetDouble() : 0;

        // Validate bounds
        if (minX >= maxX)
            return Task.FromResult(ToolResult.Error("min_x must be less than max_x."));
        if (minY >= maxY)
            return Task.FromResult(ToolResult.Error("min_y must be less than max_y."));

        try
        {
            var uiView = GetActiveUIView(uiDoc);
            if (uiView == null)
                return Task.FromResult(ToolResult.Error("Cannot access the active view for zoom operations."));

            var minPoint = new XYZ(minX, minY, minZ);
            var maxPoint = new XYZ(maxX, maxY, maxZ);

            uiView.ZoomAndCenterRectangle(minPoint, maxPoint);

            var result = new ZoomToBoundsResult
            {
                ViewName = uiDoc.ActiveView.Name,
                MinPoint = $"({minX:F2}, {minY:F2}, {minZ:F2})",
                MaxPoint = $"({maxX:F2}, {maxY:F2}, {maxZ:F2})",
                Message = "View zoomed to specified bounds."
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

    private sealed class ZoomToBoundsResult
    {
        public string ViewName { get; set; } = string.Empty;
        public string MinPoint { get; set; } = string.Empty;
        public string MaxPoint { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
