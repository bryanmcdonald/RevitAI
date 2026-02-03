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
/// Tool that pans the view by direction, to an element, or to a point.
/// </summary>
public sealed class PanViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    private static readonly Dictionary<string, XYZ> DirectionVectors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "north", new XYZ(0, 1, 0) },
        { "south", new XYZ(0, -1, 0) },
        { "east", new XYZ(1, 0, 0) },
        { "west", new XYZ(-1, 0, 0) },
        { "up", new XYZ(0, 0, 1) },
        { "down", new XYZ(0, 0, -1) }
    };

    static PanViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "direction": {
                        "type": "string",
                        "enum": ["north", "south", "east", "west", "up", "down"],
                        "description": "Direction to pan the view. Use with 'distance' parameter."
                    },
                    "distance": {
                        "type": "number",
                        "description": "Distance to pan in feet. Required when using 'direction'."
                    },
                    "center_on_element": {
                        "type": "integer",
                        "description": "Element ID to center the view on."
                    },
                    "center_on_point": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "number", "description": "X coordinate in feet." },
                            "y": { "type": "number", "description": "Y coordinate in feet." },
                            "z": { "type": "number", "description": "Z coordinate in feet (optional)." }
                        },
                        "required": ["x", "y"],
                        "description": "Point coordinates to center the view on."
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

    public string Name => "pan_view";

    public string Description =>
        "Pans the current view. Use ONE of: (1) direction + distance to pan by offset, " +
        "(2) center_on_element to center on an element, or (3) center_on_point to center on coordinates.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;

        // Determine which mode we're in
        var hasDirection = input.TryGetProperty("direction", out var directionElement);
        var hasDistance = input.TryGetProperty("distance", out var distanceElement);
        var hasElement = input.TryGetProperty("center_on_element", out var elementIdElement);
        var hasPoint = input.TryGetProperty("center_on_point", out var pointElement);

        // Count modes
        var modeCount = (hasDirection || hasDistance ? 1 : 0) + (hasElement ? 1 : 0) + (hasPoint ? 1 : 0);

        if (modeCount == 0)
            return Task.FromResult(ToolResult.Error(
                "Must specify one of: (direction + distance), center_on_element, or center_on_point."));

        if (modeCount > 1)
            return Task.FromResult(ToolResult.Error(
                "Specify only ONE of: (direction + distance), center_on_element, or center_on_point."));

        try
        {
            var uiView = GetActiveUIView(uiDoc);
            if (uiView == null)
                return Task.FromResult(ToolResult.Error("Cannot access the active view for pan operations."));

            // Handle direction mode
            if (hasDirection)
            {
                if (!hasDistance)
                    return Task.FromResult(ToolResult.Error("'distance' is required when using 'direction'."));

                var direction = directionElement.GetString()!;
                if (!DirectionVectors.TryGetValue(direction, out var dirVector))
                    return Task.FromResult(ToolResult.Error($"Invalid direction: {direction}"));

                var distance = distanceElement.GetDouble();
                if (distance <= 0)
                    return Task.FromResult(ToolResult.Error("Distance must be greater than 0."));

                return PanByOffset(uiView, uiDoc, dirVector * distance);
            }

            // Handle center on element mode
            if (hasElement)
            {
                var elementId = new ElementId(elementIdElement.GetInt64());
                var element = doc.GetElement(elementId);

                if (element == null)
                    return Task.FromResult(ToolResult.Error($"Element with ID {elementId.Value} not found."));

                return CenterOnElement(uiView, uiDoc, element);
            }

            // Handle center on point mode
            if (hasPoint)
            {
                if (!pointElement.TryGetProperty("x", out var xElement) ||
                    !pointElement.TryGetProperty("y", out var yElement))
                {
                    return Task.FromResult(ToolResult.Error("center_on_point requires 'x' and 'y' coordinates."));
                }

                var x = xElement.GetDouble();
                var y = yElement.GetDouble();
                var z = pointElement.TryGetProperty("z", out var zElement) ? zElement.GetDouble() : 0;

                return CenterOnPoint(uiView, uiDoc, new XYZ(x, y, z));
            }

            return Task.FromResult(ToolResult.Error("Invalid parameters."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static Task<ToolResult> PanByOffset(UIView uiView, UIDocument uiDoc, XYZ offset)
    {
        // Get current view corners
        var corners = uiView.GetZoomCorners();
        var corner1 = corners[0];
        var corner2 = corners[1];

        // Offset both corners
        var newCorner1 = corner1 + offset;
        var newCorner2 = corner2 + offset;

        // Apply the new view
        uiView.ZoomAndCenterRectangle(newCorner1, newCorner2);

        var result = new PanViewResult
        {
            ViewName = uiDoc.ActiveView.Name,
            Mode = "direction",
            Message = $"Panned view by offset ({offset.X:F2}, {offset.Y:F2}, {offset.Z:F2}) feet."
        };

        return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
    }

    private static Task<ToolResult> CenterOnElement(UIView uiView, UIDocument uiDoc, Element element)
    {
        // Get element bounding box
        var bbox = element.get_BoundingBox(uiDoc.ActiveView) ?? element.get_BoundingBox(null);

        if (bbox == null)
            return Task.FromResult(ToolResult.Error($"Cannot get bounding box for element {element.Id.Value}."));

        // Calculate element center
        var elementCenter = new XYZ(
            (bbox.Min.X + bbox.Max.X) / 2,
            (bbox.Min.Y + bbox.Max.Y) / 2,
            (bbox.Min.Z + bbox.Max.Z) / 2
        );

        return CenterOnPoint(uiView, uiDoc, elementCenter, $"Centered view on element {element.Id.Value}.");
    }

    private static Task<ToolResult> CenterOnPoint(UIView uiView, UIDocument uiDoc, XYZ targetCenter, string? message = null)
    {
        // Get current view corners
        var corners = uiView.GetZoomCorners();
        var corner1 = corners[0];
        var corner2 = corners[1];

        // Calculate current center
        var currentCenter = new XYZ(
            (corner1.X + corner2.X) / 2,
            (corner1.Y + corner2.Y) / 2,
            (corner1.Z + corner2.Z) / 2
        );

        // Calculate offset to move center to target
        var offset = targetCenter - currentCenter;

        // Apply offset to corners
        var newCorner1 = corner1 + offset;
        var newCorner2 = corner2 + offset;

        uiView.ZoomAndCenterRectangle(newCorner1, newCorner2);

        var result = new PanViewResult
        {
            ViewName = uiDoc.ActiveView.Name,
            Mode = "center",
            TargetPoint = $"({targetCenter.X:F2}, {targetCenter.Y:F2}, {targetCenter.Z:F2})",
            Message = message ?? $"Centered view on point ({targetCenter.X:F2}, {targetCenter.Y:F2}, {targetCenter.Z:F2})."
        };

        return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
    }

    private static UIView? GetActiveUIView(UIDocument uiDoc)
    {
        var uiViews = uiDoc.GetOpenUIViews();
        return uiViews.FirstOrDefault(v => v.ViewId == uiDoc.ActiveView.Id);
    }

    private sealed class PanViewResult
    {
        public string ViewName { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string? TargetPoint { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
