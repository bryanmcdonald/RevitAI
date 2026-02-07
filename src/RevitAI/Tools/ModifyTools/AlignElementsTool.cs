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
/// Tool that aligns elements to a reference element along an axis.
/// </summary>
public sealed class AlignElementsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static AlignElementsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to align."
                    },
                    "reference_id": {
                        "type": "integer",
                        "description": "Element ID of the reference element to align to."
                    },
                    "alignment": {
                        "type": "string",
                        "enum": ["left", "right", "top", "bottom", "center_horizontal", "center_vertical"],
                        "description": "Alignment mode: 'left' (min X), 'right' (max X), 'top' (max Y), 'bottom' (min Y), 'center_horizontal' (center X), 'center_vertical' (center Y)."
                    }
                },
                "required": ["element_ids", "reference_id", "alignment"],
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

    public string Name => "align_elements";

    public string Description => "Aligns elements to a reference element. Alignment options: 'left' (min X), 'right' (max X), 'top' (max Y), 'bottom' (min Y), 'center_horizontal' (center X), 'center_vertical' (center Y). Elements are moved to match the reference element's corresponding coordinate.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        var refId = input.TryGetProperty("reference_id", out var refElem) ? refElem.GetInt64().ToString() : "unknown";
        var alignment = input.TryGetProperty("alignment", out var alignElem) ? alignElem.GetString() : "unknown";
        return $"Would align {count} element(s) to the {alignment} of element {refId}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        if (!input.TryGetProperty("reference_id", out var referenceIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: reference_id"));

        if (!input.TryGetProperty("alignment", out var alignmentElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: alignment"));

        try
        {
            var alignment = alignmentElement.GetString();
            var validAlignments = new[] { "left", "right", "top", "bottom", "center_horizontal", "center_vertical" };
            if (alignment == null || !validAlignments.Contains(alignment))
                return Task.FromResult(ToolResult.Error($"Invalid alignment. Must be one of: {string.Join(", ", validAlignments)}"));

            // Parse element IDs
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
                requestedIds.Add(idElement.GetInt64());

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            // Get reference element
            var referenceId = new ElementId(referenceIdElement.GetInt64());
            var referenceElement = doc.GetElement(referenceId);
            if (referenceElement == null)
                return Task.FromResult(ToolResult.Error($"Reference element with ID {referenceId.Value} not found."));

            var referenceBBox = referenceElement.get_BoundingBox(null);
            if (referenceBBox == null)
                return Task.FromResult(ToolResult.Error($"Reference element {referenceId.Value} has no bounding box."));

            // Get the target coordinate from the reference element
            var targetValue = GetTargetValue(referenceBBox, alignment);

            // Validate and align each element
            var alignedCount = 0;
            var invalidIds = new List<long>();
            var skippedPinnedIds = new List<long>();
            var skippedNoBBoxIds = new List<long>();

            foreach (var id in requestedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);

                if (element == null)
                {
                    invalidIds.Add(id);
                    continue;
                }

                if (element.Pinned)
                {
                    skippedPinnedIds.Add(id);
                    continue;
                }

                var bbox = element.get_BoundingBox(null);
                if (bbox == null)
                {
                    skippedNoBBoxIds.Add(id);
                    continue;
                }

                // Skip the reference element itself
                if (elementId == referenceId)
                    continue;

                var currentValue = GetTargetValue(bbox, alignment);
                var delta = ComputeDelta(alignment, targetValue - currentValue);

                if (delta.GetLength() > 1e-9)
                {
                    ElementTransformUtils.MoveElement(doc, elementId, delta);
                    alignedCount++;
                }
            }

            var result = new AlignElementsResult
            {
                AlignedCount = alignedCount,
                Alignment = alignment,
                ReferenceId = referenceId.Value,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                SkippedPinnedIds = skippedPinnedIds.Count > 0 ? skippedPinnedIds : null,
                SkippedNoBboxIds = skippedNoBBoxIds.Count > 0 ? skippedNoBBoxIds : null,
                Message = $"Aligned {alignedCount} element(s) to the {alignment} of element {referenceId.Value}."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static double GetTargetValue(BoundingBoxXYZ bbox, string alignment)
    {
        return alignment switch
        {
            "left" => bbox.Min.X,
            "right" => bbox.Max.X,
            "bottom" => bbox.Min.Y,
            "top" => bbox.Max.Y,
            "center_horizontal" => (bbox.Min.X + bbox.Max.X) / 2.0,
            "center_vertical" => (bbox.Min.Y + bbox.Max.Y) / 2.0,
            _ => 0
        };
    }

    private static XYZ ComputeDelta(string alignment, double offset)
    {
        return alignment switch
        {
            "left" or "right" or "center_horizontal" => new XYZ(offset, 0, 0),
            "top" or "bottom" or "center_vertical" => new XYZ(0, offset, 0),
            _ => XYZ.Zero
        };
    }

    private sealed class AlignElementsResult
    {
        public int AlignedCount { get; set; }
        public string Alignment { get; set; } = string.Empty;
        public long ReferenceId { get; set; }
        public List<long>? InvalidIds { get; set; }
        public List<long>? SkippedPinnedIds { get; set; }
        public List<long>? SkippedNoBboxIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
