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
/// Tool that places a dimension between references (grids, levels, wall faces/centerlines).
/// Simplified V1: supports grids, levels, and walls only.
/// </summary>
public sealed class PlaceDimensionTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDimensionTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "references": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "element_id": {
                                    "type": "integer",
                                    "description": "The ID of the element to reference (grid, level, or wall)."
                                },
                                "reference_type": {
                                    "type": "string",
                                    "enum": ["center", "exterior", "interior"],
                                    "description": "Type of reference. 'center' (default) for grid lines, level lines, and wall centerlines. 'exterior'/'interior' for wall faces only."
                                }
                            },
                            "required": ["element_id"]
                        },
                        "minItems": 2,
                        "description": "Array of references to dimension between. Minimum 2 required. Supports grids, levels, and walls."
                    },
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the dimension in. Optional - uses active view if not specified."
                    },
                    "dimension_type": {
                        "type": "string",
                        "description": "Dimension type name. Optional - uses default if not specified."
                    },
                    "offset": {
                        "type": "number",
                        "description": "Offset distance in feet for the dimension line from the references. Default is 3."
                    }
                },
                "required": ["references"],
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

    public string Name => "place_dimension";

    public string Description => "Places a dimension between referenced elements (grids, levels, walls). V1 supports grids, levels, and wall centerlines/faces. Use get_grids, get_levels, or get_elements_by_category to find element IDs.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var refCount = input.TryGetProperty("references", out var refsElem) ? refsElem.GetArrayLength() : 0;
        var dimType = input.TryGetProperty("dimension_type", out var typeElem) ? typeElem.GetString() : null;

        if (dimType != null)
            return $"Would place a '{dimType}' dimension between {refCount} references.";
        return $"Would place a dimension between {refCount} references.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("references", out var refsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: references"));

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

            // Dimensions cannot be placed in 3D views
            if (view.ViewType == ViewType.ThreeD)
                return Task.FromResult(ToolResult.Error("Dimensions cannot be placed in 3D views. Switch to a plan, section, or elevation view."));

            // Parse references
            var refArray = new ReferenceArray();
            var refPoints = new List<XYZ>(); // For computing dimension line placement

            foreach (var refEntry in refsElement.EnumerateArray())
            {
                if (!refEntry.TryGetProperty("element_id", out var elemIdElem))
                    return Task.FromResult(ToolResult.Error("Each reference must have an element_id."));

                var elemId = new ElementId(elemIdElem.GetInt64());
                var element = doc.GetElement(elemId);
                if (element == null)
                    return Task.FromResult(ToolResult.Error($"Element with ID {elemIdElem.GetInt64()} not found."));

                var refType = "center";
                if (refEntry.TryGetProperty("reference_type", out var refTypeElem))
                    refType = refTypeElem.GetString()?.ToLowerInvariant() ?? "center";

                var refResult = GetReference(doc, element, refType, view, out var refPoint);
                if (refResult.error != null)
                    return Task.FromResult(ToolResult.Error(refResult.error));

                refArray.Append(refResult.reference!);
                if (refPoint != null)
                    refPoints.Add(refPoint);
            }

            if (refArray.Size < 2)
                return Task.FromResult(ToolResult.Error("At least 2 valid references are required."));

            if (refPoints.Count < 2)
                return Task.FromResult(ToolResult.Error("Could not determine positions for dimension line placement."));

            // Calculate dimension line
            var offset = 3.0; // Default offset in feet
            if (input.TryGetProperty("offset", out var offsetElement))
                offset = offsetElement.GetDouble();

            var dimLine = CalculateDimensionLine(refPoints, offset, view);

            // Find dimension type
            DimensionType? dimType = null;
            if (input.TryGetProperty("dimension_type", out var dimTypeElement))
            {
                var dimTypeName = dimTypeElement.GetString();
                if (!string.IsNullOrWhiteSpace(dimTypeName))
                {
                    dimType = ElementLookupHelper.FindDimensionType(doc, dimTypeName);
                    if (dimType == null)
                    {
                        var available = ElementLookupHelper.GetAvailableDimensionTypeNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Dimension type '{dimTypeName}' not found. Available types: {available}"));
                    }
                }
            }

            // Create the dimension
            Dimension dimension;
            if (dimType != null)
            {
                dimension = doc.Create.NewDimension(view, dimLine, refArray, dimType);
            }
            else
            {
                dimension = doc.Create.NewDimension(view, dimLine, refArray);
            }

            // Get dimension value
            string? dimValue = null;
            if (dimension.Value.HasValue)
                dimValue = $"{dimension.Value.Value:F4}'";

            var result = new PlaceDimensionResult
            {
                DimensionId = dimension.Id.Value,
                ViewId = view.Id.Value,
                ViewName = view.Name,
                ReferenceCount = refArray.Size,
                DimensionValue = dimValue,
                DimensionType = dimType?.Name,
                Message = dimValue != null
                    ? $"Created dimension ({dimValue}) between {refArray.Size} references in '{view.Name}'."
                    : $"Created dimension between {refArray.Size} references in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create dimension: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static (Reference? reference, string? error) GetReference(
        Document doc, Element element, string refType, View view, out XYZ? referencePoint)
    {
        referencePoint = null;

        if (element is Grid grid)
        {
            referencePoint = grid.Curve.Evaluate(0.5, true);
            return (new Reference(grid), null);
        }

        if (element is Level level)
        {
            // Levels are horizontal datum lines — reference is the level itself
            referencePoint = new XYZ(0, 0, level.Elevation);
            return (new Reference(level), null);
        }

        if (element is Wall wall)
        {
            return GetWallReference(wall, refType, view, out referencePoint);
        }

        return (null, $"Element '{element.Name}' (ID: {element.Id.Value}, Category: {element.Category?.Name ?? "unknown"}) " +
                      "is not supported for dimensioning in V1. Supported types: Grid, Level, Wall.");
    }

    private static (Reference? reference, string? error) GetWallReference(
        Wall wall, string refType, View view, out XYZ? referencePoint)
    {
        referencePoint = null;

        // Get wall location curve midpoint for reference positioning
        if (wall.Location is LocationCurve locCurve)
            referencePoint = locCurve.Curve.Evaluate(0.5, true);

        // For wall centerline, use the wall's curve reference directly
        if (refType == "center")
        {
            // Get geometry with ComputeReferences to get stable references
            var options = new Options
            {
                ComputeReferences = true,
                View = view
            };

            var geomElem = wall.get_Geometry(options);
            if (geomElem == null)
                return (null, $"Could not get geometry for wall (ID: {wall.Id.Value}).");

            // Look for the wall's center reference among geometry objects
            foreach (var geomObj in geomElem)
            {
                if (geomObj is Line line && line.Reference != null)
                {
                    return (line.Reference, null);
                }

                if (geomObj is Solid solid)
                {
                    // For center reference, look for a line in the solid
                    foreach (Edge edge in solid.Edges)
                    {
                        if (edge.Reference != null)
                        {
                            return (edge.Reference, null);
                        }
                    }
                }
            }

            // Fall back to element reference
            return (new Reference(wall), null);
        }

        // For exterior/interior wall faces
        if (refType == "exterior" || refType == "interior")
        {
            var options = new Options
            {
                ComputeReferences = true,
                View = view
            };

            var geomElem = wall.get_Geometry(options);
            if (geomElem == null)
                return (null, $"Could not get geometry for wall (ID: {wall.Id.Value}).");

            // Collect all planar faces
            var faces = new List<(PlanarFace face, Reference reference)>();
            foreach (var geomObj in geomElem)
            {
                if (geomObj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace planarFace && face.Reference != null)
                        {
                            // Only consider faces whose normal is roughly perpendicular to the wall direction
                            // (i.e., the exterior/interior faces, not top/bottom)
                            var normal = planarFace.FaceNormal;
                            if (Math.Abs(normal.Z) < 0.1) // Roughly horizontal normal = vertical face
                            {
                                faces.Add((planarFace, face.Reference));
                            }
                        }
                    }
                }
            }

            if (faces.Count < 2)
                return (null, $"Could not find exterior/interior faces for wall (ID: {wall.Id.Value}).");

            // Sort faces by distance along the wall's normal direction
            // The wall's orientation gives us which face is exterior vs interior
            var wallNormal = wall.Orientation;
            faces.Sort((a, b) =>
            {
                var dotA = a.face.Origin.DotProduct(wallNormal);
                var dotB = b.face.Origin.DotProduct(wallNormal);
                return dotA.CompareTo(dotB);
            });

            if (refType == "exterior")
            {
                var face = faces.Last();
                referencePoint = face.face.Origin;
                return (face.reference, null);
            }
            else
            {
                var face = faces.First();
                referencePoint = face.face.Origin;
                return (face.reference, null);
            }
        }

        return (null, $"Unknown reference_type '{refType}'. Use 'center', 'exterior', or 'interior'.");
    }

    private static Line CalculateDimensionLine(List<XYZ> refPoints, double offset, View view)
    {
        // Calculate the direction between the first and last reference points
        var first = refPoints.First();
        var last = refPoints.Last();
        var diff = last - first;

        // Handle coincident reference points
        XYZ direction;
        if (diff.IsZeroLength())
        {
            // Fall back to X-axis direction for coincident points
            direction = XYZ.BasisX;
        }
        else
        {
            direction = diff.Normalize();
        }

        // Calculate perpendicular offset direction
        // For plan views, offset upward in Y; for sections/elevations, use view's up direction
        XYZ offsetDir;
        if (view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.CeilingPlan ||
            view.ViewType == ViewType.AreaPlan || view.ViewType == ViewType.EngineeringPlan)
        {
            // In plan views, perpendicular to the reference direction in the XY plane
            offsetDir = new XYZ(-direction.Y, direction.X, 0).Normalize();
        }
        else
        {
            // For sections/elevations, offset perpendicular in the view plane
            var viewDir = view.ViewDirection;
            offsetDir = direction.CrossProduct(viewDir).Normalize();
            if (offsetDir.IsZeroLength())
                offsetDir = new XYZ(0, 0, 1); // Fallback
        }

        // Create the dimension line offset from the references
        var startOffset = first + offsetDir * offset;
        var endOffset = last + offsetDir * offset;

        // Ensure the dimension line has non-zero length
        if (startOffset.DistanceTo(endOffset) < 0.001)
        {
            // If references are at the same point, create a short line
            endOffset = startOffset + direction * 1.0;
        }

        return Line.CreateBound(startOffset, endOffset);
    }

    private sealed class PlaceDimensionResult
    {
        public long DimensionId { get; set; }
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public int ReferenceCount { get; set; }
        public string? DimensionValue { get; set; }
        public string? DimensionType { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
