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
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.DraftingTools.Helpers;

/// <summary>
/// Shared utilities for all drafting tools (P2-08).
/// Methods return (T?, ToolResult?) tuples — if error is non-null, callers should return immediately.
/// </summary>
public static class DraftingHelper
{
    /// <summary>
    /// Resolves the target view for a drafting operation.
    /// Uses optional view_id from input, falls back to active view.
    /// Validates the view is suitable for 2D drafting (not 3D, schedule, or sheet).
    /// </summary>
    public static (View? View, ToolResult? Error) ResolveDetailView(Document doc, JsonElement input)
    {
        View? view;

        if (input.TryGetProperty("view_id", out var viewIdElement))
        {
            var viewId = new ElementId(viewIdElement.GetInt64());
            view = doc.GetElement(viewId) as View;
            if (view == null)
                return (null, ToolResult.Error($"View with ID {viewIdElement.GetInt64()} not found."));
        }
        else
        {
            view = doc.ActiveView;
        }

        if (view == null)
            return (null, ToolResult.Error("No active view available."));

        // Validate view is suitable for 2D drafting
        if (view.ViewType == ViewType.ThreeD)
            return (null, ToolResult.Error("Detail/drafting operations cannot be performed in 3D views. Switch to a plan, section, elevation, or drafting view."));

        if (view.ViewType == ViewType.Schedule || view.ViewType == ViewType.ColumnSchedule ||
            view.ViewType == ViewType.PanelSchedule)
            return (null, ToolResult.Error("Detail/drafting operations cannot be performed in schedule views."));

        if (view.ViewType == ViewType.DrawingSheet)
            return (null, ToolResult.Error("Detail/drafting operations cannot be performed directly on sheets. Use a detail or drafting view instead."));

        return (view, null);
    }

    /// <summary>
    /// Parses a JSON array element as an XYZ point ([x, y] or [x, y, z]) in feet.
    /// </summary>
    public static (XYZ? Point, ToolResult? Error) ParsePoint(JsonElement input, string paramName)
    {
        if (!input.TryGetProperty(paramName, out var element))
            return (null, ToolResult.Error($"Missing required parameter: {paramName}"));

        if (element.ValueKind != JsonValueKind.Array)
            return (null, ToolResult.Error($"Parameter '{paramName}' must be an array of numbers [x, y] or [x, y, z]."));

        var coords = element.EnumerateArray().ToList();
        if (coords.Count < 2 || coords.Count > 3)
            return (null, ToolResult.Error($"Parameter '{paramName}' must be an array of 2 or 3 numbers [x, y] or [x, y, z]."));

        var x = coords[0].GetDouble();
        var y = coords[1].GetDouble();
        var z = coords.Count == 3 ? coords[2].GetDouble() : 0;

        return (new XYZ(x, y, z), null);
    }

    /// <summary>
    /// Parses a JSON array of point arrays as a list of XYZ points.
    /// </summary>
    public static (List<XYZ>? Points, ToolResult? Error) ParsePointArray(JsonElement input, string paramName, int minPoints = 2)
    {
        if (!input.TryGetProperty(paramName, out var element))
            return (null, ToolResult.Error($"Missing required parameter: {paramName}"));

        if (element.ValueKind != JsonValueKind.Array)
            return (null, ToolResult.Error($"Parameter '{paramName}' must be an array of points."));

        var points = new List<XYZ>();
        var index = 0;
        foreach (var ptElement in element.EnumerateArray())
        {
            var coords = ptElement.EnumerateArray().ToList();
            if (coords.Count < 2 || coords.Count > 3)
                return (null, ToolResult.Error($"Point at index {index} in '{paramName}' must be [x, y] or [x, y, z]."));

            var x = coords[0].GetDouble();
            var y = coords[1].GetDouble();
            var z = coords.Count == 3 ? coords[2].GetDouble() : 0;
            points.Add(new XYZ(x, y, z));
            index++;
        }

        if (points.Count < minPoints)
            return (null, ToolResult.Error($"Parameter '{paramName}' requires at least {minPoints} points, got {points.Count}."));

        return (points, null);
    }

    /// <summary>
    /// Builds a closed CurveLoop from a list of points.
    /// Auto-closes the boundary if first != last point.
    /// </summary>
    public static (CurveLoop? Loop, ToolResult? Error) BuildClosedCurveLoop(List<XYZ> points)
    {
        if (points.Count < 3)
            return (null, ToolResult.Error("At least 3 points are required to form a closed boundary."));

        // Auto-close if needed
        var workingPoints = new List<XYZ>(points);
        if (workingPoints[0].DistanceTo(workingPoints[^1]) > 0.001)
        {
            workingPoints.Add(workingPoints[0]);
        }

        try
        {
            var curveLoop = new CurveLoop();
            for (int i = 0; i < workingPoints.Count - 1; i++)
            {
                if (workingPoints[i].DistanceTo(workingPoints[i + 1]) < 0.001)
                    return (null, ToolResult.Error($"Points at index {i} and {i + 1} are too close together (less than 0.001 feet)."));

                curveLoop.Append(Line.CreateBound(workingPoints[i], workingPoints[i + 1]));
            }
            return (curveLoop, null);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return (null, ToolResult.Error($"Failed to create closed boundary: {ex.Message}"));
        }
    }

    /// <summary>
    /// Applies an optional line style to a detail curve.
    /// Reads the "line_style" parameter from input if present.
    /// Returns the applied style name, or an error if the style was not found.
    /// </summary>
    public static (string? AppliedStyle, ToolResult? Error) ApplyLineStyle(Document doc, DetailCurve curve, JsonElement input)
    {
        if (!input.TryGetProperty("line_style", out var lineStyleElement))
            return (null, null);

        var lineStyleName = lineStyleElement.GetString();
        if (string.IsNullOrWhiteSpace(lineStyleName))
            return (null, null);

        var graphicsStyle = ElementLookupHelper.FindLineStyle(doc, lineStyleName);
        if (graphicsStyle == null)
        {
            var available = ElementLookupHelper.GetAvailableLineStyleNames(doc);
            return (null, ToolResult.Error($"Line style '{lineStyleName}' not found. Available styles: {available}"));
        }

        curve.LineStyle = graphicsStyle;
        return (lineStyleName, null);
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
