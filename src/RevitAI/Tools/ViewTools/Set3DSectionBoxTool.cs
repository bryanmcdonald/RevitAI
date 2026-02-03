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
/// Tool that sets a 3D section box on the active 3D view, either by fitting to elements or specifying explicit bounds.
/// </summary>
public sealed class Set3DSectionBoxTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static Set3DSectionBoxTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to fit the section box around. Cannot be used with min/max."
                    },
                    "padding": {
                        "type": "number",
                        "description": "Padding in feet to add around elements (default 5). Only used with element_ids."
                    },
                    "min": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" },
                            "z": { "type": "number" }
                        },
                        "required": ["x", "y", "z"],
                        "description": "Minimum corner of the section box in feet (project coordinates). Use with max."
                    },
                    "max": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" },
                            "z": { "type": "number" }
                        },
                        "required": ["x", "y", "z"],
                        "description": "Maximum corner of the section box in feet (project coordinates). Use with min."
                    }
                },
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

    public string Name => "set_3d_section_box";

    public string Description =>
        "Sets a 3D section box on the active 3D view. " +
        "Provide element_ids to fit the box around specific elements with optional padding, " +
        "or provide explicit min/max coordinates in feet.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;
        var activeView = uiDoc.ActiveView;

        // Verify view is a 3D view
        if (activeView is not View3D view3D)
        {
            return Task.FromResult(ToolResult.Error(
                $"Section box can only be set on 3D views. Current view '{activeView.Name}' is a {activeView.ViewType}."));
        }

        // Check if view is a locked template
        if (view3D.IsTemplate)
        {
            return Task.FromResult(ToolResult.Error("Cannot modify section box on a view template."));
        }

        // Parse input - either element_ids or min/max
        var hasElementIds = input.TryGetProperty("element_ids", out var elementIdsElement);
        var hasMin = input.TryGetProperty("min", out var minElement);
        var hasMax = input.TryGetProperty("max", out var maxElement);

        if (hasElementIds && (hasMin || hasMax))
        {
            return Task.FromResult(ToolResult.Error(
                "Cannot specify both element_ids and min/max. Use one approach or the other."));
        }

        if (!hasElementIds && !hasMin && !hasMax)
        {
            return Task.FromResult(ToolResult.Error(
                "Must specify either element_ids or both min and max coordinates."));
        }

        if ((hasMin && !hasMax) || (!hasMin && hasMax))
        {
            return Task.FromResult(ToolResult.Error(
                "Both min and max must be specified together."));
        }

        try
        {
            BoundingBoxXYZ sectionBox;

            if (hasElementIds)
            {
                // Get padding (default 5 feet)
                var padding = 5.0;
                if (input.TryGetProperty("padding", out var paddingElement))
                {
                    padding = paddingElement.GetDouble();
                    if (padding < 0)
                        return Task.FromResult(ToolResult.Error("Padding cannot be negative."));
                }

                // Collect element IDs
                var requestedIds = new List<long>();
                foreach (var idElement in elementIdsElement.EnumerateArray())
                {
                    requestedIds.Add(idElement.GetInt64());
                }

                if (requestedIds.Count == 0)
                    return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

                // Calculate combined bounding box
                var result = CalculateBoundingBox(doc, requestedIds, padding, cancellationToken);
                if (result.Error != null)
                    return Task.FromResult(ToolResult.Error(result.Error));

                sectionBox = result.BoundingBox!;
            }
            else
            {
                // Use explicit min/max coordinates
                var minX = minElement.GetProperty("x").GetDouble();
                var minY = minElement.GetProperty("y").GetDouble();
                var minZ = minElement.GetProperty("z").GetDouble();
                var maxX = maxElement.GetProperty("x").GetDouble();
                var maxY = maxElement.GetProperty("y").GetDouble();
                var maxZ = maxElement.GetProperty("z").GetDouble();

                // Validate min < max
                if (minX >= maxX || minY >= maxY || minZ >= maxZ)
                {
                    return Task.FromResult(ToolResult.Error(
                        "Invalid bounds: min coordinates must be less than max coordinates in all dimensions."));
                }

                sectionBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(minX, minY, minZ),
                    Max = new XYZ(maxX, maxY, maxZ)
                };
            }

            // Apply section box (transaction is handled by the framework)
            view3D.SetSectionBox(sectionBox);

            var resultObj = new Set3DSectionBoxResult
            {
                MinX = Math.Round(sectionBox.Min.X, 4),
                MinY = Math.Round(sectionBox.Min.Y, 4),
                MinZ = Math.Round(sectionBox.Min.Z, 4),
                MaxX = Math.Round(sectionBox.Max.X, 4),
                MaxY = Math.Round(sectionBox.Max.Y, 4),
                MaxZ = Math.Round(sectionBox.Max.Z, 4),
                ViewName = view3D.Name,
                Message = $"Section box set on view '{view3D.Name}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(resultObj, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static (BoundingBoxXYZ? BoundingBox, string? Error) CalculateBoundingBox(
        Document doc,
        List<long> requestedIds,
        double padding,
        CancellationToken ct)
    {
        var validIds = new List<long>();
        var invalidIds = new List<long>();
        BoundingBoxXYZ? combinedBounds = null;

        foreach (var id in requestedIds)
        {
            ct.ThrowIfCancellationRequested();

            var elementId = new ElementId(id);
            var element = doc.GetElement(elementId);

            if (element == null)
            {
                invalidIds.Add(id);
                continue;
            }

            // Get bounding box without view context for 3D bounds
            var bbox = element.get_BoundingBox(null);

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
            return (null, $"Could not get bounding boxes for any of the specified elements. Invalid IDs: {string.Join(", ", invalidIds)}");
        }

        // Apply padding
        var paddedBounds = new BoundingBoxXYZ
        {
            Min = new XYZ(
                combinedBounds.Min.X - padding,
                combinedBounds.Min.Y - padding,
                combinedBounds.Min.Z - padding
            ),
            Max = new XYZ(
                combinedBounds.Max.X + padding,
                combinedBounds.Max.Y + padding,
                combinedBounds.Max.Z + padding
            )
        };

        return (paddedBounds, null);
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

    private sealed class Set3DSectionBoxResult
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
