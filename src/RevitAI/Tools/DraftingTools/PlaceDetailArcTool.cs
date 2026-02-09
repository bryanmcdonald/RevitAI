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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that places a detail arc in a view.
/// Supports center+radius+angles mode and three-point mode.
/// </summary>
public sealed class PlaceDetailArcTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailArcTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the arc in. Optional - uses active view if not specified."
                    },
                    "mode": {
                        "type": "string",
                        "enum": ["center_radius", "three_point"],
                        "description": "Arc creation mode. 'center_radius' uses center, radius, start_angle, end_angle. 'three_point' uses start, mid, end points. Auto-detected from parameters if not specified."
                    },
                    "center": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Center point [x, y] in feet. Required for center_radius mode."
                    },
                    "radius": {
                        "type": "number",
                        "description": "Radius in feet. Required for center_radius mode. Must be >= 0.001."
                    },
                    "start_angle": {
                        "type": "number",
                        "description": "Start angle in degrees (0 = right/east, counter-clockwise). Required for center_radius mode."
                    },
                    "end_angle": {
                        "type": "number",
                        "description": "End angle in degrees (counter-clockwise from start). Required for center_radius mode."
                    },
                    "start": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Start point [x, y] in feet. Required for three_point mode."
                    },
                    "mid": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Point on the arc [x, y] in feet. Required for three_point mode."
                    },
                    "end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "End point [x, y] in feet. Required for three_point mode."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to use. Optional - uses default if not specified."
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

    public string Name => "place_detail_arc";

    public string Description => "Places a detail arc in a view. Supports two modes: 'center_radius' (center, radius, start_angle, end_angle in degrees) or 'three_point' (start, mid, end points). Mode is auto-detected from parameters. For full circles, use place_detail_circle instead. Coordinates are in feet.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var mode = DetectMode(input);
        var lineStyle = input.TryGetProperty("line_style", out var s) ? s.GetString() : null;

        if (mode == "three_point")
        {
            if (lineStyle != null)
                return $"Would place a three-point detail arc using '{lineStyle}' style.";
            return "Would place a three-point detail arc.";
        }

        var radius = input.TryGetProperty("radius", out var r) ? r.GetDouble() : 0;
        if (lineStyle != null)
            return $"Would place a detail arc (radius {radius:F2}') using '{lineStyle}' style.";
        return $"Would place a detail arc (radius {radius:F2}').";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            var (view, viewError) = DraftingHelper.ResolveDetailView(doc, input);
            if (viewError != null) return Task.FromResult(viewError);

            var mode = DetectMode(input);

            Arc arc;
            if (mode == "three_point")
            {
                var (startPt, startErr) = DraftingHelper.ParsePoint(input, "start");
                if (startErr != null) return Task.FromResult(startErr);

                var (midPt, midErr) = DraftingHelper.ParsePoint(input, "mid");
                if (midErr != null) return Task.FromResult(midErr);

                var (endPt, endErr) = DraftingHelper.ParsePoint(input, "end");
                if (endErr != null) return Task.FromResult(endErr);

                // Validate 3 distinct points
                if (startPt!.DistanceTo(midPt!) < 0.001)
                    return Task.FromResult(ToolResult.Error("Start and mid points are too close together."));
                if (midPt!.DistanceTo(endPt!) < 0.001)
                    return Task.FromResult(ToolResult.Error("Mid and end points are too close together."));
                if (startPt.DistanceTo(endPt!) < 0.001)
                    return Task.FromResult(ToolResult.Error("Start and end points are too close together."));

                // Validate not collinear (cross product magnitude)
                var v1 = midPt - startPt;
                var v2 = endPt! - startPt;
                var cross = v1.CrossProduct(v2);
                if (cross.GetLength() < 1e-6)
                    return Task.FromResult(ToolResult.Error("The three points are collinear (on the same line). An arc requires non-collinear points."));

                // Revit Arc.Create signature: start, end, pointOnArc
                arc = Arc.Create(startPt, endPt, midPt);
            }
            else
            {
                // center_radius mode
                var (center, centerErr) = DraftingHelper.ParsePoint(input, "center");
                if (centerErr != null) return Task.FromResult(centerErr);

                if (!input.TryGetProperty("radius", out var radiusElement))
                    return Task.FromResult(ToolResult.Error("Missing required parameter: radius (for center_radius mode)."));
                var radius = radiusElement.GetDouble();
                if (radius < 0.001)
                    return Task.FromResult(ToolResult.Error("Radius must be at least 0.001 feet (must be positive)."));

                if (!input.TryGetProperty("start_angle", out var startAngleElement))
                    return Task.FromResult(ToolResult.Error("Missing required parameter: start_angle (for center_radius mode)."));
                if (!input.TryGetProperty("end_angle", out var endAngleElement))
                    return Task.FromResult(ToolResult.Error("Missing required parameter: end_angle (for center_radius mode)."));

                var startAngleDeg = startAngleElement.GetDouble();
                var endAngleDeg = endAngleElement.GetDouble();

                // Normalize angles to [0, 360)
                startAngleDeg = ((startAngleDeg % 360) + 360) % 360;
                endAngleDeg = ((endAngleDeg % 360) + 360) % 360;

                if (Math.Abs(startAngleDeg - endAngleDeg) < 0.001)
                    return Task.FromResult(ToolResult.Error("Start and end angles must be different. For a full circle, use place_detail_circle."));

                var startAngleRad = DraftingHelper.DegreesToRadians(startAngleDeg);
                var endAngleRad = DraftingHelper.DegreesToRadians(endAngleDeg);

                // Ensure endAngleRad > startAngleRad for Revit
                if (endAngleRad <= startAngleRad)
                    endAngleRad += 2 * Math.PI;

                // Check for near-full circle (sweep >= 360)
                if (endAngleRad - startAngleRad >= 2 * Math.PI - 0.001)
                    return Task.FromResult(ToolResult.Error("Arc sweep is 360 degrees (full circle). Use place_detail_circle instead."));

                arc = Arc.Create(center!, radius, startAngleRad, endAngleRad, XYZ.BasisX, XYZ.BasisY);
            }

            var detailCurve = doc.Create.NewDetailCurve(view!, arc);

            // Apply line style
            var (appliedStyle, styleError) = DraftingHelper.ApplyLineStyle(doc, detailCurve, input);
            if (styleError != null) return Task.FromResult(styleError);

            var result = new PlaceDetailArcResult
            {
                ElementId = detailCurve.Id.Value,
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                ArcLength = Math.Round(arc.Length, 4),
                Radius = Math.Round(arc.Radius, 4),
                Mode = mode,
                LineStyle = appliedStyle,
                Message = appliedStyle != null
                    ? $"Created detail arc (radius {arc.Radius:F2}', length {arc.Length:F2}') with '{appliedStyle}' style in '{view.Name}'."
                    : $"Created detail arc (radius {arc.Radius:F2}', length {arc.Length:F2}') in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { detailCurve.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create detail arc: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static string DetectMode(JsonElement input)
    {
        if (input.TryGetProperty("mode", out var modeElement))
        {
            var mode = modeElement.GetString();
            if (mode == "three_point") return "three_point";
            if (mode == "center_radius") return "center_radius";
            // Invalid mode value — fall through to auto-detection from parameters
        }

        // Auto-detect from parameters
        if (input.TryGetProperty("start", out _) && input.TryGetProperty("mid", out _) && input.TryGetProperty("end", out _))
            return "three_point";

        return "center_radius";
    }

    private sealed class PlaceDetailArcResult
    {
        public long ElementId { get; set; }
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public double ArcLength { get; set; }
        public double Radius { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string? LineStyle { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
