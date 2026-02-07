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

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that mirrors elements about a vertical plane defined by two points.
/// </summary>
public sealed class MirrorElementTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static MirrorElementTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to mirror."
                    },
                    "axis_start": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Start point [x, y] in feet of the mirror axis line."
                    },
                    "axis_end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "End point [x, y] in feet of the mirror axis line."
                    },
                    "copy": {
                        "type": "boolean",
                        "description": "If true (default), creates mirrored copies. If false, moves elements to mirrored position."
                    }
                },
                "required": ["element_ids", "axis_start", "axis_end"],
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

    public string Name => "mirror_element";

    public string Description => "Mirrors one or more elements about a vertical plane defined by two points [x, y]. By default creates mirrored copies; set copy=false to move elements to the mirrored position instead.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        var isCopy = !input.TryGetProperty("copy", out var copyElem) || copyElem.GetBoolean();
        var copyText = isCopy ? "with copies" : "without copies (in place)";

        if (input.TryGetProperty("axis_start", out var startElem) && input.TryGetProperty("axis_end", out var endElem))
        {
            var start = startElem.EnumerateArray().ToList();
            var end = endElem.EnumerateArray().ToList();
            if (start.Count == 2 && end.Count == 2)
            {
                return $"Would mirror {count} element(s) about axis from ({start[0].GetDouble():F2}, {start[1].GetDouble():F2}) to ({end[0].GetDouble():F2}, {end[1].GetDouble():F2}) {copyText}.";
            }
        }
        return $"Would mirror {count} element(s) {copyText}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        if (!input.TryGetProperty("axis_start", out var axisStartElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: axis_start"));

        if (!input.TryGetProperty("axis_end", out var axisEndElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: axis_end"));

        try
        {
            // Parse element IDs
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
                requestedIds.Add(idElement.GetInt64());

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            // Parse axis points
            var startArray = axisStartElement.EnumerateArray().ToList();
            var endArray = axisEndElement.EnumerateArray().ToList();

            if (startArray.Count != 2)
                return Task.FromResult(ToolResult.Error("axis_start must be an array of exactly 2 numbers [x, y]."));
            if (endArray.Count != 2)
                return Task.FromResult(ToolResult.Error("axis_end must be an array of exactly 2 numbers [x, y]."));

            var startX = startArray[0].GetDouble();
            var startY = startArray[1].GetDouble();
            var endX = endArray[0].GetDouble();
            var endY = endArray[1].GetDouble();

            if (Math.Abs(startX - endX) < 1e-9 && Math.Abs(startY - endY) < 1e-9)
                return Task.FromResult(ToolResult.Error("axis_start and axis_end must be different points."));

            var isCopy = !input.TryGetProperty("copy", out var copyElem) || copyElem.GetBoolean();

            // Validate element IDs
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();

            foreach (var id in requestedIds)
            {
                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);
                if (element != null)
                    validIds.Add(elementId);
                else
                    invalidIds.Add(id);
            }

            if (validIds.Count == 0)
                return Task.FromResult(ToolResult.Error($"None of the specified element IDs are valid: {string.Join(", ", invalidIds)}"));

            // Create the mirror plane (vertical plane containing the axis line)
            var startPt = new XYZ(startX, startY, 0);
            var endPt = new XYZ(endX, endY, 0);
            var axisDirection = (endPt - startPt).Normalize();
            var planeNormal = axisDirection.CrossProduct(XYZ.BasisZ).Normalize();
            var mirrorPlane = Plane.CreateByNormalAndOrigin(planeNormal, startPt);

            List<long>? newElementIds = null;

            if (isCopy)
            {
                // Copy then mirror: copy in place, then mirror the copies
                var copiedIds = ElementTransformUtils.CopyElements(doc, validIds, XYZ.Zero);
                ElementTransformUtils.MirrorElements(doc, copiedIds, mirrorPlane, false);
                newElementIds = copiedIds.Select(id => id.Value).ToList();
            }
            else
            {
                // Mirror in place (moves elements)
                ElementTransformUtils.MirrorElements(doc, validIds, mirrorPlane, false);
            }

            var result = new MirrorElementResult
            {
                MirroredCount = validIds.Count,
                IsCopy = isCopy,
                NewElementIds = newElementIds,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                AxisStart = new[] { startX, startY },
                AxisEnd = new[] { endX, endY },
                Message = isCopy
                    ? $"Created {validIds.Count} mirrored copy/copies about axis from ({startX:F2}, {startY:F2}) to ({endX:F2}, {endY:F2})."
                    : $"Mirrored {validIds.Count} element(s) in place about axis from ({startX:F2}, {startY:F2}) to ({endX:F2}, {endY:F2})."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class MirrorElementResult
    {
        public int MirroredCount { get; set; }
        public bool IsCopy { get; set; }
        public List<long>? NewElementIds { get; set; }
        public List<long>? InvalidIds { get; set; }
        public double[] AxisStart { get; set; } = Array.Empty<double>();
        public double[] AxisEnd { get; set; } = Array.Empty<double>();
        public string Message { get; set; } = string.Empty;
    }
}
