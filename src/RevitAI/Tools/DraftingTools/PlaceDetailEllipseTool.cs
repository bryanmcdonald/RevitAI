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
/// Tool that places a detail ellipse (two half-ellipses) in a view.
/// </summary>
public sealed class PlaceDetailEllipseTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailEllipseTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the ellipse in. Optional - uses active view if not specified."
                    },
                    "center": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Center point [x, y] or [x, y, z] in feet."
                    },
                    "radius_x": {
                        "type": "number",
                        "description": "Semi-axis radius along the X direction (before rotation) in feet. Must be >= 0.001."
                    },
                    "radius_y": {
                        "type": "number",
                        "description": "Semi-axis radius along the Y direction (before rotation) in feet. Must be >= 0.001."
                    },
                    "rotation": {
                        "type": "number",
                        "description": "Rotation angle in degrees (counter-clockwise). Default: 0."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to use. Optional - uses default if not specified."
                    }
                },
                "required": ["center", "radius_x", "radius_y"],
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

    public string Name => "place_detail_ellipse";

    public string Description => "Places a detail ellipse in a view as a single element. Supports optional rotation. Coordinates are in feet. Cannot be placed in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var rx = input.TryGetProperty("radius_x", out var rxE) ? rxE.GetDouble() : 0;
        var ry = input.TryGetProperty("radius_y", out var ryE) ? ryE.GetDouble() : 0;
        var rotation = input.TryGetProperty("rotation", out var rot) ? rot.GetDouble() : 0;
        var lineStyle = input.TryGetProperty("line_style", out var s) ? s.GetString() : null;

        var rotPart = Math.Abs(rotation) > 0.001 ? $", rotated {rotation:F0} degrees" : "";
        if (lineStyle != null)
            return $"Would place a detail ellipse ({rx:F2}' x {ry:F2}'{rotPart}) using '{lineStyle}' style.";
        return $"Would place a detail ellipse ({rx:F2}' x {ry:F2}'{rotPart}).";
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

            var (center, centerError) = DraftingHelper.ParsePoint(input, "center");
            if (centerError != null) return Task.FromResult(centerError);

            if (!input.TryGetProperty("radius_x", out var rxElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: radius_x"));
            if (!input.TryGetProperty("radius_y", out var ryElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: radius_y"));

            var radiusX = rxElement.GetDouble();
            var radiusY = ryElement.GetDouble();

            if (radiusX < 0.001 || radiusY < 0.001)
                return Task.FromResult(ToolResult.Error("Both radius_x and radius_y must be at least 0.001 feet (must be positive)."));

            var rotationDeg = input.TryGetProperty("rotation", out var rotElement) ? rotElement.GetDouble() : 0;
            var rotationRad = DraftingHelper.DegreesToRadians(rotationDeg);

            // Compute rotated basis vectors
            var xAxis = new XYZ(Math.Cos(rotationRad), Math.Sin(rotationRad), 0);
            var yAxis = new XYZ(-Math.Sin(rotationRad), Math.Cos(rotationRad), 0);

            // Try single-element ellipse (full 0 to 2PI)
            // Falls back to two half-ellipses if Revit rejects the full curve
            DetailCurve? singleCurve = null;
            try
            {
                var ellipse = Ellipse.CreateCurve(center!, radiusX, radiusY, xAxis, yAxis, 0, 2 * Math.PI);
                singleCurve = doc.Create.NewDetailCurve(view!, ellipse);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Full ellipse rejected — fall back to two halves below
            }

            if (singleCurve != null)
            {
                var (appliedStyle, styleError) = DraftingHelper.ApplyLineStyle(doc, singleCurve, input);
                if (styleError != null) return Task.FromResult(styleError);

                var result = new PlaceDetailEllipseResult
                {
                    ElementIds = new[] { singleCurve.Id.Value },
                    ViewId = view!.Id.Value,
                    ViewName = view.Name,
                    Center = new[] { center!.X, center.Y },
                    RadiusX = Math.Round(radiusX, 4),
                    RadiusY = Math.Round(radiusY, 4),
                    Rotation = Math.Round(rotationDeg, 2),
                    LineStyle = appliedStyle,
                    Message = appliedStyle != null
                        ? $"Created detail ellipse ({radiusX:F2}' x {radiusY:F2}') with '{appliedStyle}' style in '{view.Name}'."
                        : $"Created detail ellipse ({radiusX:F2}' x {radiusY:F2}') in '{view.Name}'."
                };

                return Task.FromResult(ToolResult.OkWithElements(
                    JsonSerializer.Serialize(result, _jsonOptions), new[] { singleCurve.Id.Value }));
            }

            // Fallback: two half-ellipses
            var ellipse1 = Ellipse.CreateCurve(center!, radiusX, radiusY, xAxis, yAxis, 0, Math.PI);
            var ellipse2 = Ellipse.CreateCurve(center!, radiusX, radiusY, xAxis, yAxis, Math.PI, 2 * Math.PI);

            var (detailCurves, createError) = DraftingHelper.CreateDetailCurves(doc, view!, new Curve[] { ellipse1, ellipse2 });
            if (createError != null) return Task.FromResult(createError);

            var (fallbackStyle, fallbackStyleError) = DraftingHelper.ApplyLineStyleToAll(doc, detailCurves!, input);
            if (fallbackStyleError != null) return Task.FromResult(fallbackStyleError);

            var elementIds = detailCurves!.Select(c => c.Id.Value).ToArray();

            var fallbackResult = new PlaceDetailEllipseResult
            {
                ElementIds = elementIds,
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                Center = new[] { center!.X, center.Y },
                RadiusX = Math.Round(radiusX, 4),
                RadiusY = Math.Round(radiusY, 4),
                Rotation = Math.Round(rotationDeg, 2),
                LineStyle = fallbackStyle,
                Message = fallbackStyle != null
                    ? $"Created detail ellipse ({radiusX:F2}' x {radiusY:F2}') with '{fallbackStyle}' style in '{view.Name}'."
                    : $"Created detail ellipse ({radiusX:F2}' x {radiusY:F2}') in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(fallbackResult, _jsonOptions), elementIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create detail ellipse: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceDetailEllipseResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public double[] Center { get; set; } = Array.Empty<double>();
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        public double Rotation { get; set; }
        public string? LineStyle { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
