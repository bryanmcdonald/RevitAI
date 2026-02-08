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

using Autodesk.Revit.DB;

namespace RevitAI.Services;

/// <summary>
/// Static utility class for resolving natural position references
/// (grid intersections, relative positions) to Revit XYZ coordinates.
/// </summary>
public static class GeometryResolver
{
    /// <summary>
    /// Resolves the intersection point of two named grids.
    /// Uses mathematical 2D line intersection (not Curve.Intersect) to avoid
    /// issues with bounded grid extents.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="grid1Name">Name of the first grid.</param>
    /// <param name="grid2Name">Name of the second grid.</param>
    /// <returns>The intersection point (Z=0) and any error message.</returns>
    public static (XYZ? Point, string? Error) ResolveGridIntersection(
        Document doc, string grid1Name, string grid2Name)
    {
        if (string.IsNullOrWhiteSpace(grid1Name) || string.IsNullOrWhiteSpace(grid2Name))
            return (null, "Both grid names must be provided.");

        var grids = new FilteredElementCollector(doc)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .ToList();

        var grid1 = grids.FirstOrDefault(g =>
            string.Equals(g.Name, grid1Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (grid1 == null)
        {
            var available = string.Join(", ", grids.Select(g => g.Name).OrderBy(n => n));
            return (null, $"Grid '{grid1Name}' not found. Available grids: {available}");
        }

        var grid2 = grids.FirstOrDefault(g =>
            string.Equals(g.Name, grid2Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (grid2 == null)
        {
            var available = string.Join(", ", grids.Select(g => g.Name).OrderBy(n => n));
            return (null, $"Grid '{grid2Name}' not found. Available grids: {available}");
        }

        // Get grid curves
        var curve1 = grid1.Curve;
        var curve2 = grid2.Curve;

        if (curve1 is not Line line1 || curve2 is not Line line2)
            return (null, "Grid intersection is only supported for straight (linear) grids.");

        // 2D line intersection using parametric form:
        // P = P1 + t * D1, Q = P2 + s * D2
        // Solve for t where P1 + t*D1 = P2 + s*D2
        var p1 = line1.GetEndPoint(0);
        var d1 = line1.Direction;
        var p2 = line2.GetEndPoint(0);
        var d2 = line2.Direction;

        // Cross product in 2D: d1.X * d2.Y - d1.Y * d2.X
        var cross = d1.X * d2.Y - d1.Y * d2.X;

        if (Math.Abs(cross) < 1e-10)
            return (null, $"Grids '{grid1Name}' and '{grid2Name}' are parallel and do not intersect.");

        // Solve for t: t = ((p2-p1) x d2) / (d1 x d2)
        var dp = new XYZ(p2.X - p1.X, p2.Y - p1.Y, 0);
        var t = (dp.X * d2.Y - dp.Y * d2.X) / cross;

        var intersection = new XYZ(p1.X + t * d1.X, p1.Y + t * d1.Y, 0);

        return (intersection, null);
    }

    /// <summary>
    /// Resolves a position relative to an existing element.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="referenceElementId">The element ID of the reference element.</param>
    /// <param name="direction">The direction: north, south, east, west, up, down.</param>
    /// <param name="distanceFeet">The distance in feet.</param>
    /// <returns>The resolved point and any error message.</returns>
    public static (XYZ? Point, string? Error) ResolveRelativePosition(
        Document doc, long referenceElementId, string direction, double distanceFeet)
    {
        var elementId = new ElementId(referenceElementId);
        var element = doc.GetElement(elementId);

        if (element == null)
            return (null, $"Reference element with ID {referenceElementId} not found.");

        // Get element's base point
        XYZ? basePoint = null;

        if (element.Location is LocationPoint locationPoint)
        {
            basePoint = locationPoint.Point;
        }
        else if (element.Location is LocationCurve locationCurve)
        {
            // Use midpoint of the curve
            var curve = locationCurve.Curve;
            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);
            basePoint = new XYZ((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2);
        }

        if (basePoint == null)
            return (null, $"Cannot determine location of element {referenceElementId}. Element may not have a point or curve location.");

        // Map direction to offset vector
        var dirLower = direction.Trim().ToLowerInvariant();
        XYZ offset = dirLower switch
        {
            "east" or "e" or "+x" => new XYZ(distanceFeet, 0, 0),
            "west" or "w" or "-x" => new XYZ(-distanceFeet, 0, 0),
            "north" or "n" or "+y" => new XYZ(0, distanceFeet, 0),
            "south" or "s" or "-y" => new XYZ(0, -distanceFeet, 0),
            "up" or "+z" => new XYZ(0, 0, distanceFeet),
            "down" or "-z" => new XYZ(0, 0, -distanceFeet),
            _ => XYZ.Zero
        };

        if (offset.IsZeroLength())
            return (null, $"Unknown direction '{direction}'. Use: north, south, east, west, up, or down.");

        var result = basePoint + offset;
        return (result, null);
    }

    /// <summary>
    /// Classifies all grids in the document by orientation (horizontal vs vertical).
    /// Horizontal grids run roughly east-west (angle near 0 or 180 degrees).
    /// Vertical grids run roughly north-south (angle near 90 degrees).
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <returns>Lists of horizontal and vertical grid names.</returns>
    public static (List<string> Horizontal, List<string> Vertical) GetGridNamesByOrientation(Document doc)
    {
        var horizontal = new List<string>();
        var vertical = new List<string>();

        var grids = new FilteredElementCollector(doc)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .ToList();

        foreach (var grid in grids)
        {
            if (grid.Curve is not Line line)
                continue;

            var direction = line.Direction;
            var angleRad = Math.Atan2(Math.Abs(direction.Y), Math.Abs(direction.X));
            var angleDeg = angleRad * 180.0 / Math.PI;

            // Angle < 45 from X-axis = horizontal (east-west), >= 45 = vertical (north-south)
            if (angleDeg < 45)
                horizontal.Add(grid.Name);
            else
                vertical.Add(grid.Name);
        }

        // Sort for consistent output
        horizontal.Sort(StringComparer.OrdinalIgnoreCase);
        vertical.Sort(StringComparer.OrdinalIgnoreCase);

        return (horizontal, vertical);
    }
}
