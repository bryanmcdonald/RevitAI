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

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that zooms the view to frame specific elements with optional padding.
/// </summary>
public sealed class ZoomToElementsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ZoomToElementsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to zoom to."
                    },
                    "factor": {
                        "type": "number",
                        "description": "Padding factor around elements. 1.0 = tight fit, 1.2 = 20% padding (default). Must be >= 1.0."
                    }
                },
                "required": ["element_ids"],
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

    public string Name => "zoom_to_elements";

    public string Description =>
        "Zooms the current view to frame the specified elements with optional padding. " +
        "Use factor > 1.0 for padding around elements (default 1.2 = 20% padding).";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;
        var activeView = uiDoc.ActiveView;

        // Get element_ids parameter
        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        // Get optional factor parameter (default 1.2)
        var factor = 1.2;
        if (input.TryGetProperty("factor", out var factorElement))
        {
            factor = factorElement.GetDouble();
            if (factor < 1.0)
                return Task.FromResult(ToolResult.Error("Factor must be >= 1.0."));
        }

        try
        {
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
            {
                requestedIds.Add(idElement.GetInt64());
            }

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            var uiView = GetActiveUIView(uiDoc);
            if (uiView == null)
                return Task.FromResult(ToolResult.Error("Cannot access the active view for zoom operations."));

            // Collect bounding boxes for valid elements
            var validIds = new List<long>();
            var invalidIds = new List<long>();
            BoundingBoxXYZ? combinedBounds = null;

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

                // Get bounding box in current view
                var bbox = element.get_BoundingBox(activeView);
                if (bbox == null)
                {
                    // Try without view context (element may not be visible in current view)
                    bbox = element.get_BoundingBox(null);
                }

                if (bbox != null)
                {
                    validIds.Add(id);
                    combinedBounds = CombineBoundingBoxes(combinedBounds, bbox);
                }
                else
                {
                    invalidIds.Add(id);
                }
            }

            if (validIds.Count == 0 || combinedBounds == null)
            {
                return Task.FromResult(ToolResult.Error(
                    $"Could not get bounding boxes for any of the specified elements. Invalid IDs: {string.Join(", ", invalidIds)}"));
            }

            // Apply padding factor
            var expandedBounds = ExpandBoundingBox(combinedBounds, factor);

            // Zoom to the expanded bounds
            uiView.ZoomAndCenterRectangle(expandedBounds.Min, expandedBounds.Max);

            var result = new ZoomToElementsResult
            {
                ZoomedToCount = validIds.Count,
                Factor = factor,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                Message = invalidIds.Count == 0
                    ? $"Zoomed to {validIds.Count} element(s) with {factor}x padding."
                    : $"Zoomed to {validIds.Count} element(s). {invalidIds.Count} ID(s) were invalid or had no bounding box."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static UIView? GetActiveUIView(UIDocument uiDoc)
    {
        var uiViews = uiDoc.GetOpenUIViews();
        return uiViews.FirstOrDefault(v => v.ViewId == uiDoc.ActiveView.Id);
    }

    private static BoundingBoxXYZ CombineBoundingBoxes(BoundingBoxXYZ? existing, BoundingBoxXYZ newBox)
    {
        if (existing == null)
            return newBox;

        return new BoundingBoxXYZ
        {
            Min = new XYZ(
                Math.Min(existing.Min.X, newBox.Min.X),
                Math.Min(existing.Min.Y, newBox.Min.Y),
                Math.Min(existing.Min.Z, newBox.Min.Z)
            ),
            Max = new XYZ(
                Math.Max(existing.Max.X, newBox.Max.X),
                Math.Max(existing.Max.Y, newBox.Max.Y),
                Math.Max(existing.Max.Z, newBox.Max.Z)
            )
        };
    }

    private static BoundingBoxXYZ ExpandBoundingBox(BoundingBoxXYZ bounds, double factor)
    {
        var center = new XYZ(
            (bounds.Min.X + bounds.Max.X) / 2,
            (bounds.Min.Y + bounds.Max.Y) / 2,
            (bounds.Min.Z + bounds.Max.Z) / 2
        );

        var halfSize = new XYZ(
            (bounds.Max.X - bounds.Min.X) / 2 * factor,
            (bounds.Max.Y - bounds.Min.Y) / 2 * factor,
            (bounds.Max.Z - bounds.Min.Z) / 2 * factor
        );

        return new BoundingBoxXYZ
        {
            Min = center - halfSize,
            Max = center + halfSize
        };
    }

    private sealed class ZoomToElementsResult
    {
        public int ZoomedToCount { get; set; }
        public double Factor { get; set; }
        public List<long>? InvalidIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
