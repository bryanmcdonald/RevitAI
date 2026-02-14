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
    /// Resolves a view from input parameters for general-purpose operations.
    /// Uses optional view_id from input, falls back to active view.
    /// Only rejects view templates — callers validate view type suitability themselves.
    /// </summary>
    public static (View? View, ToolResult? Error) ResolveView(Document doc, JsonElement input, string paramName = "view_id")
    {
        View? view;

        if (input.TryGetProperty(paramName, out var viewIdElement))
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

        if (view.IsTemplate)
            return (null, ToolResult.Error($"View '{view.Name}' is a template and cannot be used for this operation."));

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
    /// Creates multiple detail curves from a list of Revit curves.
    /// Returns the created DetailCurves, or an error if creation fails.
    /// </summary>
    public static (IList<DetailCurve>? Curves, ToolResult? Error) CreateDetailCurves(
        Document doc, View view, IList<Curve> curves)
    {
        if (curves == null || curves.Count == 0)
            return (null, ToolResult.Error("No curves provided to create detail curves."));

        var detailCurves = new List<DetailCurve>();
        for (int i = 0; i < curves.Count; i++)
        {
            try
            {
                var detailCurve = doc.Create.NewDetailCurve(view, curves[i]);
                detailCurves.Add(detailCurve);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return (null, ToolResult.Error($"Failed to create detail curve at index {i}: {ex.Message}"));
            }
        }

        return (detailCurves, null);
    }

    /// <summary>
    /// Applies an optional line style to multiple detail curves.
    /// Reads the "line_style" parameter from input if present.
    /// Resolves the GraphicsStyle once and applies to all curves.
    /// </summary>
    public static (string? AppliedStyle, ToolResult? Error) ApplyLineStyleToAll(
        Document doc, IList<DetailCurve> curves, JsonElement input)
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

        foreach (var curve in curves)
        {
            curve.LineStyle = graphicsStyle;
        }

        return (lineStyleName, null);
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    // ────────────────────────────────────────────────────────────────
    //  Sheet & Viewport helpers (P2-08.4)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a ViewSheet from input parameters.
    /// Tries sheet_id first, then sheet_number (case-insensitive).
    /// </summary>
    public static (ViewSheet? Sheet, ToolResult? Error) ResolveSheet(Document doc, JsonElement input)
    {
        // Try sheet_id first
        if (input.TryGetProperty("sheet_id", out var sheetIdElement))
        {
            var sheetId = new ElementId(sheetIdElement.GetInt64());
            var sheet = doc.GetElement(sheetId) as ViewSheet;
            if (sheet == null)
                return (null, ToolResult.Error($"Sheet with ID {sheetIdElement.GetInt64()} not found."));
            return (sheet, null);
        }

        // Try sheet_number
        if (input.TryGetProperty("sheet_number", out var sheetNumElement))
        {
            var sheetNumber = sheetNumElement.GetString();
            if (string.IsNullOrWhiteSpace(sheetNumber))
                return (null, ToolResult.Error("Parameter 'sheet_number' cannot be empty."));

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            var match = sheets.FirstOrDefault(s =>
                string.Equals(s.SheetNumber, sheetNumber, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return (match, null);

            var available = string.Join(", ", sheets
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .Select(s => $"'{s.SheetNumber} - {s.Name}'"));
            return (null, ToolResult.Error(
                $"Sheet number '{sheetNumber}' not found. Available sheets: {available}"));
        }

        return (null, ToolResult.Error("Either 'sheet_id' or 'sheet_number' must be provided."));
    }

    /// <summary>
    /// Resolves a View suitable for viewport placement from input parameters.
    /// Tries view_id first, then view_name (case-insensitive, then fuzzy).
    /// Rejects view templates and sheets.
    /// </summary>
    public static (View? View, ToolResult? Error) ResolveViewForViewport(Document doc, JsonElement input)
    {
        // Try view_id first
        if (input.TryGetProperty("view_id", out var viewIdElement))
        {
            var viewId = new ElementId(viewIdElement.GetInt64());
            var view = doc.GetElement(viewId) as View;
            if (view == null)
                return (null, ToolResult.Error($"View with ID {viewIdElement.GetInt64()} not found."));
            if (view.IsTemplate)
                return (null, ToolResult.Error($"View '{view.Name}' is a template and cannot be placed on a sheet."));
            if (view is ViewSheet)
                return (null, ToolResult.Error($"'{view.Name}' is a sheet, not a view. Sheets cannot be placed on other sheets."));
            return (view, null);
        }

        // Try view_name
        if (input.TryGetProperty("view_name", out var viewNameElement))
        {
            var viewName = viewNameElement.GetString();
            if (string.IsNullOrWhiteSpace(viewName))
                return (null, ToolResult.Error("Parameter 'view_name' cannot be empty."));

            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v is not ViewSheet)
                .ToList();

            // Exact case-insensitive match
            var exactMatch = allViews.FirstOrDefault(v =>
                string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
                return (exactMatch, null);

            // Contains match — prefer shortest name to avoid "Level 1" matching "Level 10"
            var containsMatch = allViews
                .Where(v => v.Name.Contains(viewName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => v.Name.Length)
                .FirstOrDefault();
            if (containsMatch != null)
                return (containsMatch, null);

            // Fuzzy match via Levenshtein distance
            var maxDistance = Math.Max(2, viewName.Length / 3);
            View? bestMatch = null;
            var bestDistance = int.MaxValue;

            foreach (var v in allViews)
            {
                var dist = ElementLookupHelper.LevenshteinDistance(viewName, v.Name);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestMatch = v;
                }
            }

            if (bestMatch != null && bestDistance <= maxDistance)
                return (bestMatch, null);

            var available = string.Join(", ", allViews
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .Select(v => $"'{v.Name}'"));
            var moreNote = allViews.Count > 20 ? $" (showing 20 of {allViews.Count})" : "";
            return (null, ToolResult.Error(
                $"View '{viewName}' not found. Available views{moreNote}: {available}"));
        }

        return (null, ToolResult.Error("Either 'view_id' or 'view_name' must be provided."));
    }

    /// <summary>
    /// Gets the area of a sheet based on its title block bounding box.
    /// Note: The title block BB covers the full sheet including border/stamp area.
    /// Callers should apply sufficient margin to clear the title block frame.
    /// Falls back to standard D-size sheet dimensions (34" x 22") if no title block found.
    /// Returns min/max corners in sheet coordinates (feet).
    /// </summary>
    public static (XYZ Min, XYZ Max) GetSheetUsableArea(Document doc, ViewSheet sheet)
    {
        // Try to find title block on the sheet
        var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .ToList();

        if (titleBlocks.Count > 0)
        {
            var bb = titleBlocks[0].get_BoundingBox(sheet);
            if (bb != null)
                return (bb.Min, bb.Max);
        }

        // Fallback: standard D-size sheet (34" x 22")
        var width = 34.0 / 12.0;  // 2.833 feet
        var height = 22.0 / 12.0; // 1.833 feet
        return (new XYZ(0, 0, 0), new XYZ(width, height, 0));
    }
}
