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
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that places up to 200 detail lines in a single call.
/// Supports per-line line styles with cached lookups and partial-success tracking.
/// </summary>
public sealed class BatchPlaceDetailLinesTool : IRevitTool
{
    private const int MaxLines = 200;
    private const int MaxErrors = 5;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static BatchPlaceDetailLinesTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place lines in. Optional - uses active view if not specified."
                    },
                    "lines": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
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
                                    "description": "Line style name for this line. Optional - uses default if not specified."
                                }
                            },
                            "required": ["start", "end"]
                        },
                        "minItems": 1,
                        "maxItems": 200,
                        "description": "Array of lines to place. Each has start/end points and optional line_style."
                    }
                },
                "required": ["lines"],
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

    public string Name => "batch_place_detail_lines";

    public string Description => "Places up to 200 detail lines in a single call. Each line has start/end points and an optional per-line line_style. All lines are placed in one atomic transaction. Much more efficient than calling place_detail_line repeatedly. Coordinates are in feet.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("lines", out var lines) ? lines.GetArrayLength() : 0;
        return $"Would place {count} detail line(s).";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Resolve view
            var (view, viewError) = DraftingHelper.ResolveDetailView(doc, input);
            if (viewError != null) return Task.FromResult(viewError);

            // Validate lines array
            if (!input.TryGetProperty("lines", out var linesArray) || linesArray.ValueKind != JsonValueKind.Array)
                return Task.FromResult(ToolResult.Error("Missing required parameter: lines"));

            var lineCount = linesArray.GetArrayLength();
            if (lineCount == 0)
                return Task.FromResult(ToolResult.Error("Parameter 'lines' must contain at least 1 item."));
            if (lineCount > MaxLines)
                return Task.FromResult(ToolResult.Error($"Too many lines ({lineCount}). Maximum is {MaxLines} per call."));

            // Pre-cache line styles: resolve each unique style name once
            var styleCache = new Dictionary<string, GraphicsStyle?>(StringComparer.OrdinalIgnoreCase);
            foreach (var lineItem in linesArray.EnumerateArray())
            {
                if (lineItem.TryGetProperty("line_style", out var styleElem))
                {
                    var styleName = styleElem.GetString();
                    if (!string.IsNullOrWhiteSpace(styleName) && !styleCache.ContainsKey(styleName))
                    {
                        styleCache[styleName] = ElementLookupHelper.FindLineStyle(doc, styleName);
                    }
                }
            }

            // Place lines
            var succeeded = 0;
            var failed = 0;
            var elementIds = new List<long>();
            var errors = new List<string>();

            var index = 0;
            foreach (var lineItem in linesArray.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Parse start point
                    var (startPt, startErr) = ParsePointFromItem(lineItem, "start", index);
                    if (startErr != null)
                    {
                        failed++;
                        if (errors.Count < MaxErrors) errors.Add(startErr);
                        index++;
                        continue;
                    }

                    // Parse end point
                    var (endPt, endErr) = ParsePointFromItem(lineItem, "end", index);
                    if (endErr != null)
                    {
                        failed++;
                        if (errors.Count < MaxErrors) errors.Add(endErr);
                        index++;
                        continue;
                    }

                    // Validate points are different
                    if (startPt!.DistanceTo(endPt!) < 0.001)
                    {
                        failed++;
                        if (errors.Count < MaxErrors)
                            errors.Add($"Line {index}: start and end points are too close together.");
                        index++;
                        continue;
                    }

                    // Create detail line
                    var line = Line.CreateBound(startPt, endPt);
                    var detailCurve = doc.Create.NewDetailCurve(view!, line);

                    // Apply per-line style if specified
                    if (lineItem.TryGetProperty("line_style", out var styleElem))
                    {
                        var styleName = styleElem.GetString();
                        if (!string.IsNullOrWhiteSpace(styleName))
                        {
                            if (styleCache.TryGetValue(styleName, out var graphicsStyle) && graphicsStyle != null)
                            {
                                detailCurve.LineStyle = graphicsStyle;
                            }
                            else
                            {
                                // Style not found — line placed with default style, warn but count as success
                                if (errors.Count < MaxErrors)
                                    errors.Add($"Line {index}: line style '{styleName}' not found, used default.");
                            }
                        }
                    }

                    elementIds.Add(detailCurve.Id.Value);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (errors.Count < MaxErrors)
                        errors.Add($"Line {index}: {ex.Message}");
                }

                index++;
            }

            // Build result
            if (succeeded == 0 && elementIds.Count == 0)
            {
                return Task.FromResult(ToolResult.Error(
                    $"All {lineCount} lines failed. Errors: {string.Join("; ", errors)}"));
            }

            var result = new BatchResult
            {
                Succeeded = succeeded,
                Failed = failed,
                Total = lineCount,
                Errors = errors.Count > 0 ? errors : null,
                ElementIds = elementIds.ToArray(),
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                Message = failed == 0
                    ? $"Placed {succeeded} detail line(s) in '{view.Name}'."
                    : $"Placed {succeeded} of {lineCount} detail line(s) in '{view.Name}'. {failed} failed."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), elementIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to place detail lines: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Parses a point from a line item's property (inline to avoid tuple overhead per-item).
    /// Returns the point or an error message string.
    /// </summary>
    private static (XYZ? Point, string? Error) ParsePointFromItem(JsonElement item, string paramName, int lineIndex)
    {
        if (!item.TryGetProperty(paramName, out var element))
            return (null, $"Line {lineIndex}: missing '{paramName}'.");

        if (element.ValueKind != JsonValueKind.Array)
            return (null, $"Line {lineIndex}: '{paramName}' must be an array [x, y].");

        var length = element.GetArrayLength();
        if (length < 2 || length > 3)
            return (null, $"Line {lineIndex}: '{paramName}' must have 2 or 3 numbers.");

        var x = element[0].GetDouble();
        var y = element[1].GetDouble();
        var z = length == 3 ? element[2].GetDouble() : 0;

        return (new XYZ(x, y, z), null);
    }

    private sealed class BatchResult
    {
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public List<string>? Errors { get; set; }
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
