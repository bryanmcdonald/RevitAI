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
/// Tool that rotates elements by a specified angle around a point.
/// </summary>
public sealed class RotateElementTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static RotateElementTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to rotate."
                    },
                    "angle": {
                        "type": "number",
                        "description": "Rotation angle in degrees (positive = counterclockwise when viewed from above)."
                    },
                    "center": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Optional rotation center [x, y] in feet. If omitted, uses the combined bounding box center of the elements."
                    }
                },
                "required": ["element_ids", "angle"],
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

    public string Name => "rotate_element";

    public string Description => "Rotates one or more elements by a specified angle in degrees around a center point. Positive angle is counterclockwise when viewed from above. If no center is specified, rotates around the combined bounding box center.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        var angle = input.TryGetProperty("angle", out var angleElem) ? angleElem.GetDouble() : 0;

        if (input.TryGetProperty("center", out var centerElem))
        {
            var coords = centerElem.EnumerateArray().ToList();
            if (coords.Count == 2)
                return $"Would rotate {count} element(s) by {angle:F1} degrees around ({coords[0].GetDouble():F2}, {coords[1].GetDouble():F2}).";
        }
        return $"Would rotate {count} element(s) by {angle:F1} degrees around their bounding box center.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        if (!input.TryGetProperty("angle", out var angleElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: angle"));

        try
        {
            // Parse element IDs
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
                requestedIds.Add(idElement.GetInt64());

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            // Parse angle
            var angleDegrees = angleElement.GetDouble();
            if (Math.Abs(angleDegrees) < 1e-9)
                return Task.FromResult(ToolResult.Error("Angle must be non-zero."));

            var angleRadians = angleDegrees * Math.PI / 180.0;

            // Validate element IDs and check for pinned
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();
            var pinnedIds = new List<long>();

            foreach (var id in requestedIds)
            {
                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);
                if (element == null)
                {
                    invalidIds.Add(id);
                }
                else if (element.Pinned)
                {
                    pinnedIds.Add(id);
                }
                else
                {
                    validIds.Add(elementId);
                }
            }

            if (validIds.Count == 0)
            {
                if (pinnedIds.Count > 0)
                    return Task.FromResult(ToolResult.Error($"All valid elements are pinned and cannot be rotated. Pinned IDs: {string.Join(", ", pinnedIds)}. Unpin them first."));
                return Task.FromResult(ToolResult.Error($"None of the specified element IDs are valid: {string.Join(", ", invalidIds)}"));
            }

            // Determine center point
            double centerX, centerY;
            if (input.TryGetProperty("center", out var centerElement))
            {
                var centerArray = centerElement.EnumerateArray().ToList();
                if (centerArray.Count != 2)
                    return Task.FromResult(ToolResult.Error("center must be an array of exactly 2 numbers [x, y]."));
                centerX = centerArray[0].GetDouble();
                centerY = centerArray[1].GetDouble();
            }
            else
            {
                // Compute combined bounding box center
                var (cx, cy, found) = ComputeBoundingBoxCenter(doc, validIds);
                if (!found)
                    return Task.FromResult(ToolResult.Error("Could not compute bounding box center for the elements. Provide a center point explicitly."));
                centerX = cx;
                centerY = cy;
            }

            // Create vertical axis through the center point
            var axisPt = new XYZ(centerX, centerY, 0);
            var axis = Line.CreateBound(axisPt, axisPt + XYZ.BasisZ * 10);

            // Rotate elements
            ElementTransformUtils.RotateElements(doc, validIds, axis, angleRadians);

            var result = new RotateElementResult
            {
                RotatedCount = validIds.Count,
                AngleDegrees = angleDegrees,
                Center = new[] { Math.Round(centerX, 4), Math.Round(centerY, 4) },
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                PinnedIds = pinnedIds.Count > 0 ? pinnedIds : null,
                Message = $"Rotated {validIds.Count} element(s) by {angleDegrees:F1} degrees around ({centerX:F2}, {centerY:F2})."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static (double x, double y, bool found) ComputeBoundingBoxCenter(Document doc, List<ElementId> elementIds)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool anyFound = false;

        foreach (var id in elementIds)
        {
            var element = doc.GetElement(id);
            var bbox = element?.get_BoundingBox(null);
            if (bbox == null) continue;

            anyFound = true;
            minX = Math.Min(minX, bbox.Min.X);
            minY = Math.Min(minY, bbox.Min.Y);
            maxX = Math.Max(maxX, bbox.Max.X);
            maxY = Math.Max(maxY, bbox.Max.Y);
        }

        if (!anyFound)
            return (0, 0, false);

        return ((minX + maxX) / 2.0, (minY + maxY) / 2.0, true);
    }

    private sealed class RotateElementResult
    {
        public int RotatedCount { get; set; }
        public double AngleDegrees { get; set; }
        public double[] Center { get; set; } = Array.Empty<double>();
        public List<long>? InvalidIds { get; set; }
        public List<long>? PinnedIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
