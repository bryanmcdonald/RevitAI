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
/// Tool that places a detail polyline (connected line segments) in a view.
/// </summary>
public sealed class PlaceDetailPolylineTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailPolylineTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the polyline in. Optional - uses active view if not specified."
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
                    "closed": {
                        "type": "boolean",
                        "description": "If true, adds a closing segment from the last point back to the first. Default: false."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to use (e.g., 'Thin Lines', 'Medium Lines'). Optional - uses default if not specified."
                    }
                },
                "required": ["points"],
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

    public string Name => "place_detail_polyline";

    public string Description => "Places a detail polyline (connected line segments) in a view. Optionally closes the shape by connecting the last point to the first. Coordinates are in feet. Cannot be placed in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var pointCount = input.TryGetProperty("points", out var pts) ? pts.GetArrayLength() : 0;
        var closed = input.TryGetProperty("closed", out var c) && c.GetBoolean();
        var lineStyle = input.TryGetProperty("line_style", out var s) ? s.GetString() : null;

        var segments = closed ? pointCount : Math.Max(0, pointCount - 1);
        var shape = closed ? "closed polyline" : "polyline";

        if (lineStyle != null)
            return $"Would place a {shape} with {segments} segments using '{lineStyle}' style.";
        return $"Would place a {shape} with {segments} segments.";
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

            var (points, pointsError) = DraftingHelper.ParsePointArray(input, "points", minPoints: 2);
            if (pointsError != null) return Task.FromResult(pointsError);

            var closed = input.TryGetProperty("closed", out var closedElement) && closedElement.GetBoolean();

            if (closed && points!.Count < 3)
                return Task.FromResult(ToolResult.Error("A closed polyline requires at least 3 distinct points."));

            // Build line segments
            var lines = new List<Curve>();
            for (int i = 0; i < points!.Count - 1; i++)
            {
                if (points[i].DistanceTo(points[i + 1]) < 0.001)
                    return Task.FromResult(ToolResult.Error($"Points at index {i} and {i + 1} are too close together (less than 0.001 feet)."));

                lines.Add(Line.CreateBound(points[i], points[i + 1]));
            }

            // Add closing segment if requested
            if (closed)
            {
                if (points[^1].DistanceTo(points[0]) < 0.001)
                    return Task.FromResult(ToolResult.Error("Last point is too close to the first point for a closing segment."));

                lines.Add(Line.CreateBound(points[^1], points[0]));
            }

            var (detailCurves, createError) = DraftingHelper.CreateDetailCurves(doc, view!, lines);
            if (createError != null) return Task.FromResult(createError);

            var (appliedStyle, styleError) = DraftingHelper.ApplyLineStyleToAll(doc, detailCurves!, input);
            if (styleError != null) return Task.FromResult(styleError);

            var totalLength = lines.Sum(l => l.Length);
            var elementIds = detailCurves!.Select(c => c.Id.Value).ToArray();

            var result = new PlaceDetailPolylineResult
            {
                ElementIds = elementIds,
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                SegmentCount = lines.Count,
                TotalLength = Math.Round(totalLength, 4),
                Closed = closed,
                LineStyle = appliedStyle,
                Message = closed
                    ? $"Created closed detail polyline with {lines.Count} segments (total length {totalLength:F2}') in '{view.Name}'."
                    : $"Created detail polyline with {lines.Count} segments (total length {totalLength:F2}') in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), elementIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create detail polyline: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceDetailPolylineResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public int SegmentCount { get; set; }
        public double TotalLength { get; set; }
        public bool Closed { get; set; }
        public string? LineStyle { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
