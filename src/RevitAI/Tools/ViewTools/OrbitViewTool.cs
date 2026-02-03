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
using RevitAI.Helpers;

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that orbits a 3D view around the model.
/// </summary>
public sealed class OrbitViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static OrbitViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "horizontal_degrees": {
                        "type": "number",
                        "description": "Rotation around the world Z-axis in degrees. Positive = counter-clockwise when viewed from above."
                    },
                    "vertical_degrees": {
                        "type": "number",
                        "description": "Rotation up/down (pitch) in degrees. Positive = look up, negative = look down."
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

    public string Name => "orbit_view";

    public string Description =>
        "Orbits a 3D view around the model. Specify horizontal_degrees to rotate around the Z-axis, " +
        "and/or vertical_degrees to rotate up/down. At least one parameter is required. " +
        "Only works in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Verify this is a 3D view
        if (uiDoc.ActiveView is not View3D view3d)
            return Task.FromResult(ToolResult.Error("This tool requires a 3D view. Please switch to a 3D view first."));

        if (view3d.IsLocked)
            return Task.FromResult(ToolResult.Error("Cannot modify a locked 3D view. Please unlock the view or use a different 3D view."));

        // Get rotation parameters
        var hasHorizontal = input.TryGetProperty("horizontal_degrees", out var horizontalElement);
        var hasVertical = input.TryGetProperty("vertical_degrees", out var verticalElement);

        if (!hasHorizontal && !hasVertical)
            return Task.FromResult(ToolResult.Error("At least one of horizontal_degrees or vertical_degrees is required."));

        var horizontalDegrees = hasHorizontal ? horizontalElement.GetDouble() : 0;
        var verticalDegrees = hasVertical ? verticalElement.GetDouble() : 0;

        try
        {
            // Get current orientation
            var currentOrientation = view3d.GetOrientation();

            // Calculate the target point (what we're orbiting around)
            // Priority: selected elements > ray cast > section box > visible model center > fallback
            var target = GetOrbitCenter(uiDoc, view3d);

            // Apply rotations
            var newOrientation = currentOrientation;

            if (hasHorizontal && horizontalDegrees != 0)
            {
                newOrientation = RotateAroundTarget(newOrientation, target, XYZ.BasisZ, horizontalDegrees);
            }

            if (hasVertical && verticalDegrees != 0)
            {
                // Calculate right axis for vertical rotation
                var rightAxis = newOrientation.ForwardDirection.CrossProduct(newOrientation.UpDirection).Normalize();
                newOrientation = RotateAroundTarget(newOrientation, target, rightAxis, verticalDegrees);
            }

            // Apply the new orientation
            view3d.SetOrientation(newOrientation);

            // Force the view to refresh by getting the UIView and re-applying zoom
            var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view3d.Id);
            if (uiView != null)
            {
                // Re-apply current zoom to force redraw
                var corners = uiView.GetZoomCorners();
                uiView.ZoomAndCenterRectangle(corners[0], corners[1]);
            }

            var result = new OrbitViewResult
            {
                ViewName = view3d.Name,
                HorizontalDegrees = hasHorizontal ? horizontalDegrees : null,
                VerticalDegrees = hasVertical ? verticalDegrees : null,
                Message = BuildOrbitMessage(horizontalDegrees, verticalDegrees, hasHorizontal, hasVertical)
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
        {
            return Task.FromResult(ToolResult.Error($"Cannot modify view orientation: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Gets the center point to orbit around.
    /// Priority: selected elements > section box > model centroid.
    /// </summary>
    private static XYZ GetOrbitCenter(UIDocument uiDoc, View3D view3d)
    {
        var doc = uiDoc.Document;

        // First priority: selected elements
        var selectedIds = uiDoc.Selection.GetElementIds();
        if (selectedIds.Count > 0)
        {
            var selectionCenter = GetBoundingBoxCenter(doc, selectedIds);
            if (selectionCenter != null)
                return selectionCenter;
        }

        // Second priority: ray cast to find element at center of screen (Navisworks-style)
        var hitPoint = GetCenterScreenHitPoint(doc, view3d);
        if (hitPoint != null)
            return hitPoint;

        // Third priority: section box center if active
        if (view3d.IsSectionBoxActive)
        {
            var sectionBox = view3d.GetSectionBox();
            if (sectionBox != null)
            {
                return new XYZ(
                    (sectionBox.Min.X + sectionBox.Max.X) / 2,
                    (sectionBox.Min.Y + sectionBox.Max.Y) / 2,
                    (sectionBox.Min.Z + sectionBox.Max.Z) / 2
                );
            }
        }

        // Fourth priority: center of visible model elements
        var modelCenter = GetVisibleModelCenter(doc, view3d);
        if (modelCenter != null)
            return modelCenter;

        // Last resort fallback: point along view direction
        var orientation = view3d.GetOrientation();
        return orientation.EyePosition + orientation.ForwardDirection * 100;
    }

    /// <summary>
    /// Casts a ray from the camera through the center of the screen to find the nearest element.
    /// Returns the hit point, or null if nothing is hit.
    /// </summary>
    private static XYZ? GetCenterScreenHitPoint(Document doc, View3D view3d)
    {
        try
        {
            var orientation = view3d.GetOrientation();
            var eyePosition = orientation.EyePosition;
            var forwardDirection = orientation.ForwardDirection;

            // Create element filter for 3D elements (not annotations, not types)
            var filter = new ElementCategoryFilter(BuiltInCategory.OST_GenericModel, true); // inverted = exclude
            var classFilter = new ElementClassFilter(typeof(FamilyInstance));
            var wallFilter = new ElementCategoryFilter(BuiltInCategory.OST_Walls);
            var floorFilter = new ElementCategoryFilter(BuiltInCategory.OST_Floors);
            var roofFilter = new ElementCategoryFilter(BuiltInCategory.OST_Roofs);
            var columnFilter = new ElementCategoryFilter(BuiltInCategory.OST_Columns);
            var structColumnFilter = new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns);

            var combinedFilter = new LogicalOrFilter(new List<ElementFilter>
            {
                wallFilter, floorFilter, roofFilter, columnFilter, structColumnFilter, classFilter
            });

            // Create a ray from eye position in the forward direction
            var intersector = new ReferenceIntersector(combinedFilter, FindReferenceTarget.Face, view3d)
            {
                FindReferencesInRevitLinks = true
            };

            var results = intersector.Find(eyePosition, forwardDirection);

            if (results != null && results.Count > 0)
            {
                // Get the closest hit
                var closest = results.OrderBy(r => r.Proximity).FirstOrDefault();
                if (closest != null && closest.Proximity > 0)
                {
                    // Calculate hit point from eye position + direction * distance
                    return eyePosition + forwardDirection * closest.Proximity;
                }
            }
        }
        catch
        {
            // ReferenceIntersector can fail in some view configurations
        }

        return null;
    }

    /// <summary>
    /// Gets the center of all visible model elements in the view.
    /// </summary>
    private static XYZ? GetVisibleModelCenter(Document doc, View3D view3d)
    {
        try
        {
            // Get elements visible in this view, excluding site/topo
            var collector = new FilteredElementCollector(doc, view3d.Id)
                .WhereElementIsNotElementType();

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool foundAny = false;

            foreach (var element in collector)
            {
                // Skip certain categories
                var category = element.Category;
                if (category == null) continue;

                var catId = category.Id.Value;
                if (catId == (long)BuiltInCategory.OST_Topography ||
                    catId == (long)BuiltInCategory.OST_Site ||
                    catId == (long)BuiltInCategory.OST_Planting ||
                    catId == (long)BuiltInCategory.OST_Entourage ||
                    catId == (long)BuiltInCategory.OST_Cameras)
                {
                    continue;
                }

                var bbox = element.get_BoundingBox(view3d);
                if (bbox != null)
                {
                    foundAny = true;
                    minX = Math.Min(minX, bbox.Min.X);
                    minY = Math.Min(minY, bbox.Min.Y);
                    minZ = Math.Min(minZ, bbox.Min.Z);
                    maxX = Math.Max(maxX, bbox.Max.X);
                    maxY = Math.Max(maxY, bbox.Max.Y);
                    maxZ = Math.Max(maxZ, bbox.Max.Z);
                }
            }

            if (foundAny)
            {
                return new XYZ(
                    (minX + maxX) / 2,
                    (minY + maxY) / 2,
                    (minZ + maxZ) / 2
                );
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    /// <summary>
    /// Gets the center of the bounding box for the given element IDs.
    /// </summary>
    private static XYZ? GetBoundingBoxCenter(Document doc, ICollection<ElementId> elementIds)
    {
        BoundingBoxXYZ? combinedBounds = null;

        foreach (var id in elementIds)
        {
            var element = doc.GetElement(id);
            var bbox = element?.get_BoundingBox(null);
            if (bbox != null)
            {
                combinedBounds = CombineBounds(combinedBounds, bbox);
            }
        }

        if (combinedBounds == null)
            return null;

        return new XYZ(
            (combinedBounds.Min.X + combinedBounds.Max.X) / 2,
            (combinedBounds.Min.Y + combinedBounds.Max.Y) / 2,
            (combinedBounds.Min.Z + combinedBounds.Max.Z) / 2
        );
    }

    /// <summary>
    /// Combines two bounding boxes into one that contains both.
    /// </summary>
    private static BoundingBoxXYZ CombineBounds(BoundingBoxXYZ? existing, BoundingBoxXYZ newBox)
    {
        if (existing == null)
        {
            return new BoundingBoxXYZ
            {
                Min = newBox.Min,
                Max = newBox.Max
            };
        }

        existing.Min = new XYZ(
            Math.Min(existing.Min.X, newBox.Min.X),
            Math.Min(existing.Min.Y, newBox.Min.Y),
            Math.Min(existing.Min.Z, newBox.Min.Z)
        );
        existing.Max = new XYZ(
            Math.Max(existing.Max.X, newBox.Max.X),
            Math.Max(existing.Max.Y, newBox.Max.Y),
            Math.Max(existing.Max.Z, newBox.Max.Z)
        );

        return existing;
    }

    /// <summary>
    /// Rotates the view orientation around a target point along an axis.
    /// </summary>
    private static ViewOrientation3D RotateAroundTarget(ViewOrientation3D orientation, XYZ target, XYZ axis, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;

        // Get current distance from eye to target - we'll maintain this distance
        var eyeToTarget = target - orientation.EyePosition;
        var distance = eyeToTarget.GetLength();

        // Create rotation transform centered on target
        var rotation = Transform.CreateRotationAtPoint(axis, radians, target);

        // Transform eye position - this moves the camera in an arc around the target
        var newEyePosition = rotation.OfPoint(orientation.EyePosition);

        // Recalculate forward direction to point at target
        var newForward = (target - newEyePosition).Normalize();

        // Rotate the up direction, then orthogonalize
        var newUp = rotation.OfVector(orientation.UpDirection).Normalize();

        // Ensure up is perpendicular to forward (Gram-Schmidt)
        var right = newForward.CrossProduct(newUp);
        if (right.GetLength() < 0.001)
        {
            // Forward and up are parallel, pick a different up
            right = newForward.CrossProduct(XYZ.BasisX);
            if (right.GetLength() < 0.001)
                right = newForward.CrossProduct(XYZ.BasisY);
        }
        right = right.Normalize();
        newUp = right.CrossProduct(newForward).Normalize();

        return new ViewOrientation3D(newEyePosition, newUp, newForward);
    }

    private static string BuildOrbitMessage(double horizontal, double vertical, bool hasH, bool hasV)
    {
        var parts = new List<string>();

        if (hasH && horizontal != 0)
        {
            var direction = horizontal > 0 ? "counter-clockwise" : "clockwise";
            parts.Add($"rotated {Math.Abs(horizontal):F1}° {direction} horizontally");
        }

        if (hasV && vertical != 0)
        {
            var direction = vertical > 0 ? "up" : "down";
            parts.Add($"rotated {Math.Abs(vertical):F1}° {direction}");
        }

        if (parts.Count == 0)
            return "View orientation unchanged.";

        return "View " + string.Join(" and ", parts) + ".";
    }

    private sealed class OrbitViewResult
    {
        public string ViewName { get; set; } = string.Empty;
        public double? HorizontalDegrees { get; set; }
        public double? VerticalDegrees { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
