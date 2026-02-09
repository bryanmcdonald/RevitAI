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
/// Tool that places a detail circle (two semicircular arcs) in a view.
/// </summary>
public sealed class PlaceDetailCircleTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailCircleTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the circle in. Optional - uses active view if not specified."
                    },
                    "center": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Center point [x, y] or [x, y, z] in feet."
                    },
                    "radius": {
                        "type": "number",
                        "description": "Radius in feet. Must be >= 0.001."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to use (e.g., 'Thin Lines', 'Medium Lines'). Optional - uses default if not specified."
                    }
                },
                "required": ["center", "radius"],
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

    public string Name => "place_detail_circle";

    public string Description => "Places a detail circle in a view as a single element. Coordinates are in feet. Cannot be placed in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var radius = input.TryGetProperty("radius", out var r) ? r.GetDouble() : 0;
        var lineStyle = input.TryGetProperty("line_style", out var s) ? s.GetString() : null;

        if (lineStyle != null)
            return $"Would place a detail circle with radius {radius:F2}' using '{lineStyle}' style.";
        return $"Would place a detail circle with radius {radius:F2}'.";
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

            if (!input.TryGetProperty("radius", out var radiusElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: radius"));

            var radius = radiusElement.GetDouble();
            if (radius < 0.001)
                return Task.FromResult(ToolResult.Error("Radius must be at least 0.001 feet (must be positive)."));

            // Try single-element circle using Ellipse with equal radii (full 0 to 2PI)
            // Falls back to two semicircular arcs if Revit rejects the full curve
            DetailCurve? singleCurve = null;
            try
            {
                var circle = Ellipse.CreateCurve(center!, radius, radius, XYZ.BasisX, XYZ.BasisY, 0, 2 * Math.PI);
                singleCurve = doc.Create.NewDetailCurve(view!, circle);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Full circle rejected — fall back to two semicircles below
            }

            var circumference = 2 * Math.PI * radius;

            if (singleCurve != null)
            {
                // Single-element circle succeeded
                var (appliedStyle, styleError) = DraftingHelper.ApplyLineStyle(doc, singleCurve, input);
                if (styleError != null) return Task.FromResult(styleError);

                var result = new PlaceDetailCircleResult
                {
                    ElementIds = new[] { singleCurve.Id.Value },
                    ViewId = view!.Id.Value,
                    ViewName = view.Name,
                    Center = new[] { center!.X, center.Y },
                    Radius = Math.Round(radius, 4),
                    Circumference = Math.Round(circumference, 4),
                    LineStyle = appliedStyle,
                    Message = appliedStyle != null
                        ? $"Created detail circle (radius {radius:F2}') with '{appliedStyle}' style in '{view.Name}'."
                        : $"Created detail circle (radius {radius:F2}') in '{view.Name}'."
                };

                return Task.FromResult(ToolResult.OkWithElements(
                    JsonSerializer.Serialize(result, _jsonOptions), new[] { singleCurve.Id.Value }));
            }

            // Fallback: two semicircular arcs
            var arc1 = Arc.Create(center!, radius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
            var arc2 = Arc.Create(center!, radius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);

            var (detailCurves, createError) = DraftingHelper.CreateDetailCurves(doc, view!, new Curve[] { arc1, arc2 });
            if (createError != null) return Task.FromResult(createError);

            var (fallbackStyle, fallbackStyleError) = DraftingHelper.ApplyLineStyleToAll(doc, detailCurves!, input);
            if (fallbackStyleError != null) return Task.FromResult(fallbackStyleError);

            var elementIds = detailCurves!.Select(c => c.Id.Value).ToArray();

            var fallbackResult = new PlaceDetailCircleResult
            {
                ElementIds = elementIds,
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                Center = new[] { center!.X, center.Y },
                Radius = Math.Round(radius, 4),
                Circumference = Math.Round(circumference, 4),
                LineStyle = fallbackStyle,
                Message = fallbackStyle != null
                    ? $"Created detail circle (radius {radius:F2}') with '{fallbackStyle}' style in '{view.Name}'."
                    : $"Created detail circle (radius {radius:F2}') in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(fallbackResult, _jsonOptions), elementIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create detail circle: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceDetailCircleResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public double[] Center { get; set; } = Array.Empty<double>();
        public double Radius { get; set; }
        public double Circumference { get; set; }
        public string? LineStyle { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
