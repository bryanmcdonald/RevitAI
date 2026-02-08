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
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a detail line in a view.
/// </summary>
public sealed class PlaceDetailLineTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailLineTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the detail line in. Optional - uses active view if not specified."
                    },
                    "start": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Start point [x, y] or [x, y, z] in feet."
                    },
                    "end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "End point [x, y] or [x, y, z] in feet."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to use (e.g., 'Thin Lines', 'Medium Lines'). Optional - uses default if not specified."
                    }
                },
                "required": ["start", "end"],
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

    public string Name => "place_detail_line";

    public string Description => "Places a detail line in a view between two points. Coordinates are in feet. Detail lines are view-specific 2D elements. Cannot be placed in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        double? length = null;
        if (input.TryGetProperty("start", out var startElem) && input.TryGetProperty("end", out var endElem))
        {
            var start = startElem.EnumerateArray().ToList();
            var end = endElem.EnumerateArray().ToList();
            if (start.Count >= 2 && end.Count >= 2)
            {
                var dx = end[0].GetDouble() - start[0].GetDouble();
                var dy = end[1].GetDouble() - start[1].GetDouble();
                length = Math.Sqrt(dx * dx + dy * dy);
            }
        }

        var lineStyle = input.TryGetProperty("line_style", out var styleElem) ? styleElem.GetString() : null;

        if (lineStyle != null && length.HasValue)
            return $"Would place a {length.Value:F2}' '{lineStyle}' detail line.";
        if (length.HasValue)
            return $"Would place a {length.Value:F2}' detail line.";
        return "Would place a detail line.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("start", out var startElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: start"));

        if (!input.TryGetProperty("end", out var endElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: end"));

        try
        {
            // Resolve view
            View? view = null;
            if (input.TryGetProperty("view_id", out var viewIdElement))
            {
                var viewId = new ElementId(viewIdElement.GetInt64());
                view = doc.GetElement(viewId) as View;
                if (view == null)
                    return Task.FromResult(ToolResult.Error($"View with ID {viewIdElement.GetInt64()} not found."));
            }
            else
            {
                view = doc.ActiveView;
            }

            if (view == null)
                return Task.FromResult(ToolResult.Error("No active view available."));

            // Detail lines cannot be placed in 3D views
            if (view.ViewType == ViewType.ThreeD)
                return Task.FromResult(ToolResult.Error("Detail lines cannot be placed in 3D views. Switch to a plan, section, elevation, or drafting view."));

            // Parse start point
            var startArray = startElement.EnumerateArray().ToList();
            if (startArray.Count < 2 || startArray.Count > 3)
                return Task.FromResult(ToolResult.Error("start must be an array of 2 or 3 numbers [x, y] or [x, y, z]."));
            var startX = startArray[0].GetDouble();
            var startY = startArray[1].GetDouble();
            var startZ = startArray.Count == 3 ? startArray[2].GetDouble() : 0;

            // Parse end point
            var endArray = endElement.EnumerateArray().ToList();
            if (endArray.Count < 2 || endArray.Count > 3)
                return Task.FromResult(ToolResult.Error("end must be an array of 2 or 3 numbers [x, y] or [x, y, z]."));
            var endX = endArray[0].GetDouble();
            var endY = endArray[1].GetDouble();
            var endZ = endArray.Count == 3 ? endArray[2].GetDouble() : 0;

            // Validate points are different
            var startPoint = new XYZ(startX, startY, startZ);
            var endPoint = new XYZ(endX, endY, endZ);
            if (startPoint.DistanceTo(endPoint) < 0.001)
                return Task.FromResult(ToolResult.Error("start and end points must be different."));

            var line = Line.CreateBound(startPoint, endPoint);

            // Validate line style before creating the detail curve
            GraphicsStyle? graphicsStyle = null;
            string? appliedStyle = null;
            if (input.TryGetProperty("line_style", out var lineStyleElement))
            {
                var lineStyleName = lineStyleElement.GetString();
                if (!string.IsNullOrWhiteSpace(lineStyleName))
                {
                    graphicsStyle = ElementLookupHelper.FindLineStyle(doc, lineStyleName);
                    if (graphicsStyle == null)
                    {
                        var available = ElementLookupHelper.GetAvailableLineStyleNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Line style '{lineStyleName}' not found. Available styles: {available}"));
                    }
                    appliedStyle = lineStyleName;
                }
            }

            // Create the detail curve
            var detailCurve = doc.Create.NewDetailCurve(view, line);

            // Apply line style if specified
            if (graphicsStyle != null)
                detailCurve.LineStyle = graphicsStyle;

            var lineLength = line.Length;
            var result = new PlaceDetailLineResult
            {
                DetailLineId = detailCurve.Id.Value,
                ViewId = view.Id.Value,
                ViewName = view.Name,
                Start = new[] { startX, startY },
                End = new[] { endX, endY },
                Length = Math.Round(lineLength, 4),
                LineStyle = appliedStyle,
                Message = appliedStyle != null
                    ? $"Created {lineLength:F2}' detail line with '{appliedStyle}' style in '{view.Name}'."
                    : $"Created {lineLength:F2}' detail line in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), new[] { detailCurve.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create detail line: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceDetailLineResult
    {
        public long DetailLineId { get; set; }
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public double[] Start { get; set; } = Array.Empty<double>();
        public double[] End { get; set; } = Array.Empty<double>();
        public double Length { get; set; }
        public string? LineStyle { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
