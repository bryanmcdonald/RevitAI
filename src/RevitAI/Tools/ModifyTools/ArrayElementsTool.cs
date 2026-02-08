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
/// Tool that creates linear or radial arrays of elements.
/// </summary>
public sealed class ArrayElementsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ArrayElementsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to array."
                    },
                    "count": {
                        "type": "integer",
                        "minimum": 1,
                        "maximum": 100,
                        "description": "Number of additional copies to create."
                    },
                    "array_type": {
                        "type": "string",
                        "enum": ["linear", "radial"],
                        "description": "Type of array: 'linear' for straight-line copies, 'radial' for circular arrangement."
                    },
                    "spacing": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 3,
                        "maxItems": 3,
                        "description": "Spacing vector [x, y, z] in feet between each copy (required for linear arrays)."
                    },
                    "center": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Center point [x, y] in feet for radial arrays."
                    },
                    "total_angle": {
                        "type": "number",
                        "description": "Total angle in degrees for radial array (default 360). Ignored if angle_between is specified."
                    },
                    "angle_between": {
                        "type": "number",
                        "description": "Angle in degrees between each copy in a radial array. Overrides total_angle."
                    }
                },
                "required": ["element_ids", "count", "array_type"],
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

    public string Name => "array_elements";

    public string Description => "Creates linear or radial arrays of elements. Linear arrays require a spacing vector [x, y, z] in feet. Radial arrays require a center point [x, y] and optionally total_angle (default 360) or angle_between in degrees.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var elementCount = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        var count = input.TryGetProperty("count", out var countElem) ? countElem.GetInt32() : 0;
        var arrayType = input.TryGetProperty("array_type", out var typeElem) ? typeElem.GetString() : "unknown";

        if (arrayType == "linear" && input.TryGetProperty("spacing", out var spacingElem))
        {
            var coords = spacingElem.EnumerateArray().ToList();
            if (coords.Count == 3)
                return $"Would create {count} copies of {elementCount} element(s) with spacing ({coords[0].GetDouble():F2}, {coords[1].GetDouble():F2}, {coords[2].GetDouble():F2}) feet.";
        }
        else if (arrayType == "radial" && input.TryGetProperty("center", out var centerElem))
        {
            var coords = centerElem.EnumerateArray().ToList();
            if (coords.Count == 2)
                return $"Would create {count} copies of {elementCount} element(s) in a radial array around ({coords[0].GetDouble():F2}, {coords[1].GetDouble():F2}).";
        }

        return $"Would create {count} copies of {elementCount} element(s) in a {arrayType} array.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        if (!input.TryGetProperty("count", out var countElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: count"));

        if (!input.TryGetProperty("array_type", out var arrayTypeElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: array_type"));

        try
        {
            // Parse element IDs
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
                requestedIds.Add(idElement.GetInt64());

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            var count = countElement.GetInt32();
            if (count < 1 || count > 100)
                return Task.FromResult(ToolResult.Error("count must be between 1 and 100."));

            var arrayType = arrayTypeElement.GetString();
            if (arrayType != "linear" && arrayType != "radial")
                return Task.FromResult(ToolResult.Error("array_type must be 'linear' or 'radial'."));

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

            var allNewIds = new List<long>();

            if (arrayType == "linear")
            {
                return ExecuteLinearArray(doc, input, validIds, invalidIds, count, allNewIds, cancellationToken);
            }
            else
            {
                return ExecuteRadialArray(doc, input, validIds, invalidIds, count, allNewIds, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private Task<ToolResult> ExecuteLinearArray(Document doc, JsonElement input, List<ElementId> validIds, List<long> invalidIds, int count, List<long> allNewIds, CancellationToken cancellationToken)
    {
        if (!input.TryGetProperty("spacing", out var spacingElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter for linear array: spacing"));

        var spacingArray = spacingElement.EnumerateArray().ToList();
        if (spacingArray.Count != 3)
            return Task.FromResult(ToolResult.Error("spacing must be an array of exactly 3 numbers [x, y, z]."));

        var sx = spacingArray[0].GetDouble();
        var sy = spacingArray[1].GetDouble();
        var sz = spacingArray[2].GetDouble();

        if (Math.Abs(sx) < 1e-9 && Math.Abs(sy) < 1e-9 && Math.Abs(sz) < 1e-9)
            return Task.FromResult(ToolResult.Error("spacing vector cannot be zero."));

        var spacingVector = new XYZ(sx, sy, sz);

        for (int i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = spacingVector * i;
            var copiedIds = ElementTransformUtils.CopyElements(doc, validIds, offset);
            allNewIds.AddRange(copiedIds.Select(id => id.Value));
        }

        var result = new ArrayElementsResult
        {
            ArrayType = "linear",
            CopyCount = count,
            TotalNewElements = allNewIds.Count,
            NewElementIds = allNewIds,
            InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
            Spacing = new[] { sx, sy, sz },
            Message = $"Created {count} linear copies of {validIds.Count} element(s) with spacing ({sx:F2}, {sy:F2}, {sz:F2}) feet. {allNewIds.Count} total new elements."
        };

        return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), allNewIds));
    }

    private Task<ToolResult> ExecuteRadialArray(Document doc, JsonElement input, List<ElementId> validIds, List<long> invalidIds, int count, List<long> allNewIds, CancellationToken cancellationToken)
    {
        if (!input.TryGetProperty("center", out var centerElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter for radial array: center"));

        var centerArray = centerElement.EnumerateArray().ToList();
        if (centerArray.Count != 2)
            return Task.FromResult(ToolResult.Error("center must be an array of exactly 2 numbers [x, y]."));

        var centerX = centerArray[0].GetDouble();
        var centerY = centerArray[1].GetDouble();

        // Determine angle between copies
        double angleBetweenDegrees;
        if (input.TryGetProperty("angle_between", out var angleBetweenElem))
        {
            angleBetweenDegrees = angleBetweenElem.GetDouble();
        }
        else
        {
            var totalAngle = input.TryGetProperty("total_angle", out var totalAngleElem)
                ? totalAngleElem.GetDouble()
                : 360.0;
            // count + 1 because total_angle spans from original to last copy
            angleBetweenDegrees = totalAngle / (count + 1);
        }

        if (Math.Abs(angleBetweenDegrees) < 1e-9)
            return Task.FromResult(ToolResult.Error("Computed angle between copies is zero. Check total_angle and count."));

        var angleBetweenRadians = angleBetweenDegrees * Math.PI / 180.0;
        var axisPt = new XYZ(centerX, centerY, 0);
        var axis = Line.CreateBound(axisPt, axisPt + XYZ.BasisZ * 10);

        for (int i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copiedIds = ElementTransformUtils.CopyElements(doc, validIds, XYZ.Zero);
            ElementTransformUtils.RotateElements(doc, copiedIds, axis, angleBetweenRadians * i);
            allNewIds.AddRange(copiedIds.Select(id => id.Value));
        }

        var result = new ArrayElementsResult
        {
            ArrayType = "radial",
            CopyCount = count,
            TotalNewElements = allNewIds.Count,
            NewElementIds = allNewIds,
            InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
            Center = new[] { centerX, centerY },
            AngleBetween = Math.Round(angleBetweenDegrees, 4),
            Message = $"Created {count} radial copies of {validIds.Count} element(s) around ({centerX:F2}, {centerY:F2}) with {angleBetweenDegrees:F1} degrees between each. {allNewIds.Count} total new elements."
        };

        return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), allNewIds));
    }

    private sealed class ArrayElementsResult
    {
        public string ArrayType { get; set; } = string.Empty;
        public int CopyCount { get; set; }
        public int TotalNewElements { get; set; }
        public List<long> NewElementIds { get; set; } = new();
        public List<long>? InvalidIds { get; set; }
        public double[]? Spacing { get; set; }
        public double[]? Center { get; set; }
        public double? AngleBetween { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
