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
/// Tool that places a detail spline or hermite curve in a view.
/// Hermite curves are implemented as Catmull-Rom cubic Bezier segments (NurbSpline)
/// because Revit's NewDetailCurve rejects HermiteSpline objects.
/// </summary>
public sealed class PlaceDetailCurveTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailCurveTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the curve in. Optional - uses active view if not specified."
                    },
                    "curve_type": {
                        "type": "string",
                        "enum": ["spline", "hermite"],
                        "description": "'hermite' creates a smooth curve that passes through all points (Catmull-Rom interpolation, one segment per point pair). 'spline' creates a single NURBS curve using points as control points (curve passes near them, not through them)."
                    },
                    "points": {
                        "type": "array",
                        "items": {
                            "type": "array",
                            "items": { "type": "number" },
                            "minItems": 2,
                            "maxItems": 3
                        },
                        "minItems": 2,
                        "description": "Array of points [[x,y], ...] in feet. Minimum 2 points."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to use. Optional - uses default if not specified."
                    }
                },
                "required": ["curve_type", "points"],
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

    public string Name => "place_detail_curve";

    public string Description => "Places a detail spline or hermite curve in a view. 'hermite' creates a smooth curve that passes through all specified points (Catmull-Rom interpolation, creates one cubic segment per point pair). 'spline' (NURBS) uses points as control points — the curve passes near them, not through them. Coordinates are in feet. Cannot be placed in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var curveType = input.TryGetProperty("curve_type", out var ct) ? ct.GetString() : "curve";
        var pointCount = input.TryGetProperty("points", out var pts) ? pts.GetArrayLength() : 0;
        var lineStyle = input.TryGetProperty("line_style", out var s) ? s.GetString() : null;

        var pointDesc = curveType == "spline" ? $"with {pointCount} control points" : $"through {pointCount} points";
        if (lineStyle != null)
            return $"Would place a {curveType} curve {pointDesc} using '{lineStyle}' style.";
        return $"Would place a {curveType} curve {pointDesc}.";
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

            if (!input.TryGetProperty("curve_type", out var curveTypeElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: curve_type"));

            var curveType = curveTypeElement.GetString();
            if (curveType != "spline" && curveType != "hermite")
                return Task.FromResult(ToolResult.Error("curve_type must be 'spline' or 'hermite'."));

            var (points, pointsError) = DraftingHelper.ParsePointArray(input, "points", minPoints: 2);
            if (pointsError != null) return Task.FromResult(pointsError);

            // Validate no duplicate consecutive points
            for (int i = 0; i < points!.Count - 1; i++)
            {
                if (points[i].DistanceTo(points[i + 1]) < 0.001)
                    return Task.FromResult(ToolResult.Error($"Points at index {i} and {i + 1} are too close together (less than 0.001 feet)."));
            }

            if (curveType == "hermite")
            {
                return CreateHermiteCurve(doc, view!, points, input);
            }
            else
            {
                return CreateSplineCurve(doc, view!, points, input);
            }
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create detail curve: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Creates a single NURBS spline using points as control points.
    /// </summary>
    private static Task<ToolResult> CreateSplineCurve(Document doc, View view, List<XYZ> points, JsonElement input)
    {
        var weights = Enumerable.Repeat(1.0, points.Count).ToList();
        var curve = NurbSpline.CreateCurve(points, weights);

        var detailCurve = doc.Create.NewDetailCurve(view, curve);

        var (appliedStyle, styleError) = DraftingHelper.ApplyLineStyle(doc, detailCurve, input);
        if (styleError != null) return Task.FromResult(styleError);

        var result = new PlaceDetailCurveResult
        {
            ElementIds = new[] { detailCurve.Id.Value },
            ViewId = view.Id.Value,
            ViewName = view.Name,
            CurveType = "spline",
            PointCount = points.Count,
            SegmentCount = 1,
            Length = Math.Round(curve.Length, 4),
            LineStyle = appliedStyle,
            Message = appliedStyle != null
                ? $"Created spline curve with {points.Count} control points (length {curve.Length:F2}') with '{appliedStyle}' style in '{view.Name}'."
                : $"Created spline curve with {points.Count} control points (length {curve.Length:F2}') in '{view.Name}'."
        };

        return Task.FromResult(ToolResult.OkWithElements(
            JsonSerializer.Serialize(result, _jsonOptions), new[] { detailCurve.Id.Value }));
    }

    /// <summary>
    /// Creates a smooth curve through all points using Catmull-Rom cubic Bezier segments.
    /// Each segment is a cubic NurbSpline that passes exactly through the data points.
    /// Revit's NewDetailCurve rejects HermiteSpline objects, so this is the workaround.
    /// </summary>
    private static Task<ToolResult> CreateHermiteCurve(Document doc, View view, List<XYZ> points, JsonElement input)
    {
        // Compute Catmull-Rom tangent vectors at each data point
        var tangents = new List<XYZ>();
        for (int i = 0; i < points.Count; i++)
        {
            if (i == 0)
                tangents.Add(points[1] - points[0]);
            else if (i == points.Count - 1)
                tangents.Add(points[i] - points[i - 1]);
            else
                tangents.Add((points[i + 1] - points[i - 1]) * 0.5);
        }

        // Build one cubic Bezier NurbSpline per segment
        var curves = new List<Curve>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[i];
            var p1 = points[i] + tangents[i] * (1.0 / 3.0);
            var p2 = points[i + 1] - tangents[i + 1] * (1.0 / 3.0);
            var p3 = points[i + 1];

            var controlPts = new List<XYZ> { p0, p1, p2, p3 };
            var weights = new List<double> { 1.0, 1.0, 1.0, 1.0 };
            curves.Add(NurbSpline.CreateCurve(controlPts, weights));
        }

        var (detailCurves, createError) = DraftingHelper.CreateDetailCurves(doc, view, curves);
        if (createError != null) return Task.FromResult(createError);

        var (appliedStyle, styleError) = DraftingHelper.ApplyLineStyleToAll(doc, detailCurves!, input);
        if (styleError != null) return Task.FromResult(styleError);

        var totalLength = curves.Sum(c => c.Length);
        var elementIds = detailCurves!.Select(c => c.Id.Value).ToArray();

        var result = new PlaceDetailCurveResult
        {
            ElementIds = elementIds,
            ViewId = view.Id.Value,
            ViewName = view.Name,
            CurveType = "hermite",
            PointCount = points.Count,
            SegmentCount = curves.Count,
            Length = Math.Round(totalLength, 4),
            LineStyle = appliedStyle,
            Message = appliedStyle != null
                ? $"Created hermite curve through {points.Count} points ({curves.Count} segments, length {totalLength:F2}') with '{appliedStyle}' style in '{view.Name}'."
                : $"Created hermite curve through {points.Count} points ({curves.Count} segments, length {totalLength:F2}') in '{view.Name}'."
        };

        return Task.FromResult(ToolResult.OkWithElements(
            JsonSerializer.Serialize(result, _jsonOptions), elementIds));
    }

    private sealed class PlaceDetailCurveResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string CurveType { get; set; } = string.Empty;
        public int PointCount { get; set; }
        public int SegmentCount { get; set; }
        public double Length { get; set; }
        public string? LineStyle { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
