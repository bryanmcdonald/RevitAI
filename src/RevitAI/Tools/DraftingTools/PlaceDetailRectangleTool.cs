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
/// Tool that places a detail rectangle (four detail lines) in a view.
/// </summary>
public sealed class PlaceDetailRectangleTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailRectangleTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the rectangle in. Optional - uses active view if not specified."
                    },
                    "corner1": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "First corner point [x, y] or [x, y, z] in feet."
                    },
                    "corner2": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Opposite corner point [x, y] or [x, y, z] in feet."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to use (e.g., 'Thin Lines', 'Medium Lines'). Optional - uses default if not specified."
                    }
                },
                "required": ["corner1", "corner2"],
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

    public string Name => "place_detail_rectangle";

    public string Description => "Places a detail rectangle (four lines) in a view from two opposite corners. The rectangle is always axis-aligned. Coordinates are in feet. Cannot be placed in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var lineStyle = input.TryGetProperty("line_style", out var s) ? s.GetString() : null;

        if (lineStyle != null)
            return $"Would place a detail rectangle using '{lineStyle}' style.";
        return "Would place a detail rectangle.";
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

            var (corner1, c1Error) = DraftingHelper.ParsePoint(input, "corner1");
            if (c1Error != null) return Task.FromResult(c1Error);

            var (corner2, c2Error) = DraftingHelper.ParsePoint(input, "corner2");
            if (c2Error != null) return Task.FromResult(c2Error);

            // Validate Z coordinates match (rectangle must be planar)
            if (Math.Abs(corner1!.Z - corner2!.Z) > 0.001)
                return Task.FromResult(ToolResult.Error("corner1 and corner2 must have the same Z coordinate for an axis-aligned rectangle."));

            // Validate corners differ in both X and Y
            var minX = Math.Min(corner1.X, corner2.X);
            var maxX = Math.Max(corner1.X, corner2.X);
            var minY = Math.Min(corner1.Y, corner2.Y);
            var maxY = Math.Max(corner1.Y, corner2.Y);

            if (maxX - minX < 0.001)
                return Task.FromResult(ToolResult.Error("Corners must differ in X coordinate (non-zero width)."));
            if (maxY - minY < 0.001)
                return Task.FromResult(ToolResult.Error("Corners must differ in Y coordinate (non-zero height)."));

            var z = corner1.Z;
            var p1 = new XYZ(minX, minY, z);
            var p2 = new XYZ(maxX, minY, z);
            var p3 = new XYZ(maxX, maxY, z);
            var p4 = new XYZ(minX, maxY, z);

            var lines = new Curve[]
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            };

            var (detailCurves, createError) = DraftingHelper.CreateDetailCurves(doc, view!, lines);
            if (createError != null) return Task.FromResult(createError);

            var (appliedStyle, styleError) = DraftingHelper.ApplyLineStyleToAll(doc, detailCurves!, input);
            if (styleError != null) return Task.FromResult(styleError);

            var width = maxX - minX;
            var height = maxY - minY;
            var elementIds = detailCurves!.Select(c => c.Id.Value).ToArray();

            var result = new PlaceDetailRectangleResult
            {
                ElementIds = elementIds,
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                Corner1 = new[] { minX, minY },
                Corner2 = new[] { maxX, maxY },
                Width = Math.Round(width, 4),
                Height = Math.Round(height, 4),
                LineStyle = appliedStyle,
                Message = appliedStyle != null
                    ? $"Created {width:F2}' x {height:F2}' detail rectangle with '{appliedStyle}' style in '{view.Name}'."
                    : $"Created {width:F2}' x {height:F2}' detail rectangle in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), elementIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create detail rectangle: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceDetailRectangleResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public double[] Corner1 { get; set; } = Array.Empty<double>();
        public double[] Corner2 { get; set; } = Array.Empty<double>();
        public double Width { get; set; }
        public double Height { get; set; }
        public string? LineStyle { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
