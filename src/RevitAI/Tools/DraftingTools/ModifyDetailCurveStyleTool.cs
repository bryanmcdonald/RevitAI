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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that modifies the line style of existing detail curves.
/// </summary>
public sealed class ModifyDetailCurveStyleTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ModifyDetailCurveStyleTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "minItems": 1,
                        "description": "Array of element IDs of detail curves to modify."
                    },
                    "line_style": {
                        "type": "string",
                        "description": "Name of the line style to apply (e.g., 'Thin Lines', 'Medium Lines')."
                    }
                },
                "required": ["element_ids", "line_style"],
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

    public string Name => "modify_detail_curve_style";

    public string Description => "Changes the line style of existing detail curves. Only works on DetailCurve elements (detail lines, arcs, circles, etc.). Use get_line_styles to discover available styles.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        var style = input.TryGetProperty("line_style", out var s) ? s.GetString() : "unknown";
        return $"Would change the line style of {count} element(s) to '{style}'.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            if (!input.TryGetProperty("element_ids", out var idsElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));
            if (!input.TryGetProperty("line_style", out var styleElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: line_style"));

            var lineStyleName = styleElement.GetString();
            if (string.IsNullOrWhiteSpace(lineStyleName))
                return Task.FromResult(ToolResult.Error("line_style must not be empty."));

            // Resolve style once (fail fast)
            var graphicsStyle = ElementLookupHelper.FindLineStyle(doc, lineStyleName);
            if (graphicsStyle == null)
            {
                var available = ElementLookupHelper.GetAvailableLineStyleNames(doc);
                return Task.FromResult(ToolResult.Error($"Line style '{lineStyleName}' not found. Available styles: {available}"));
            }

            var modifiedIds = new List<long>();
            var invalidIds = new List<long>();
            var skippedElements = new List<SkippedElement>();

            foreach (var idElement in idsElement.EnumerateArray())
            {
                var id = idElement.GetInt64();
                var element = doc.GetElement(new ElementId(id));

                if (element == null)
                {
                    invalidIds.Add(id);
                    continue;
                }

                if (element is not DetailCurve detailCurve)
                {
                    skippedElements.Add(new SkippedElement
                    {
                        ElementId = id,
                        Reason = $"Element is {element.GetType().Name}, not a DetailCurve."
                    });
                    continue;
                }

                detailCurve.LineStyle = graphicsStyle;
                modifiedIds.Add(id);
            }

            if (modifiedIds.Count == 0 && invalidIds.Count == 0 && skippedElements.Count == 0)
                return Task.FromResult(ToolResult.Error("No element IDs provided."));

            if (modifiedIds.Count == 0)
            {
                var detail = invalidIds.Count > 0 ? $" {invalidIds.Count} ID(s) not found." : "";
                detail += skippedElements.Count > 0 ? $" {skippedElements.Count} element(s) are not DetailCurves." : "";
                return Task.FromResult(ToolResult.Error($"No elements were modified.{detail}"));
            }

            var result = new ModifyDetailCurveStyleResult
            {
                ModifiedIds = modifiedIds.ToArray(),
                ModifiedCount = modifiedIds.Count,
                InvalidIds = invalidIds.Count > 0 ? invalidIds.ToArray() : null,
                SkippedElements = skippedElements.Count > 0 ? skippedElements.ToArray() : null,
                LineStyle = lineStyleName,
                Message = $"Changed line style to '{lineStyleName}' on {modifiedIds.Count} element(s)."
                    + (invalidIds.Count > 0 ? $" {invalidIds.Count} ID(s) not found." : "")
                    + (skippedElements.Count > 0 ? $" {skippedElements.Count} element(s) skipped (not DetailCurve)." : "")
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), modifiedIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to modify detail curve style: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class ModifyDetailCurveStyleResult
    {
        public long[] ModifiedIds { get; set; } = Array.Empty<long>();
        public int ModifiedCount { get; set; }
        public long[]? InvalidIds { get; set; }
        public SkippedElement[]? SkippedElements { get; set; }
        public string LineStyle { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    private sealed class SkippedElement
    {
        public long ElementId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
