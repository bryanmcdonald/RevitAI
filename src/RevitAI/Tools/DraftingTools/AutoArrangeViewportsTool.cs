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
using RevitAI.Tools.DraftingTools.Helpers;

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that auto-arranges viewports on a sheet using various layout algorithms.
/// Supports auto (row-based bin packing), grid, and column layouts.
/// </summary>
public sealed class AutoArrangeViewportsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static AutoArrangeViewportsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "description": "Requires at least one of sheet_id or sheet_number.",
                "properties": {
                    "sheet_id": {
                        "type": "integer",
                        "description": "Sheet element ID. Provide either sheet_id or sheet_number."
                    },
                    "sheet_number": {
                        "type": "string",
                        "description": "Sheet number (e.g., 'A101'). Provide either sheet_id or sheet_number."
                    },
                    "layout_mode": {
                        "type": "string",
                        "enum": ["auto", "grid", "column"],
                        "description": "Layout mode: 'auto' (row-based bin packing, default), 'grid' (equal-sized cells), or 'column' (single vertical stack)."
                    },
                    "spacing": {
                        "type": "number",
                        "description": "Gap between viewports in inches. Default: 0.5 inches."
                    },
                    "margin": {
                        "type": "number",
                        "description": "Margin from sheet edges in inches. Default: 1.5 inches. Increase to clear title block border/stamp area."
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

    public string Name => "auto_arrange_viewports";

    public string Description => "Auto-arranges all viewports on a sheet using a layout algorithm. " +
        "Modes: 'auto' (row-based, tallest first), 'grid' (equal cells), 'column' (vertical stack). " +
        "Use get_sheet_list to find sheets.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var sheetNumber = input.TryGetProperty("sheet_number", out var sn) ? sn.GetString() : null;
        var sheetId = input.TryGetProperty("sheet_id", out var si) ? si.GetInt64().ToString() : null;
        var mode = input.TryGetProperty("layout_mode", out var lm) ? lm.GetString() : "auto";
        var sheetDesc = sheetNumber ?? $"sheet ID {sheetId}";
        return $"Would auto-arrange viewports on sheet '{sheetDesc}' using {mode} layout.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Resolve sheet
            var (sheet, sheetError) = DraftingHelper.ResolveSheet(doc, input);
            if (sheetError != null) return Task.FromResult(sheetError);

            // Get all viewports on the sheet
            var viewportIds = sheet!.GetAllViewports();
            if (viewportIds.Count == 0)
                return Task.FromResult(ToolResult.Ok("No viewports found on sheet to arrange."));

            // Get layout parameters
            var mode = input.TryGetProperty("layout_mode", out var modeElement)
                ? modeElement.GetString() ?? "auto" : "auto";
            var spacingInches = input.TryGetProperty("spacing", out var spacingElement)
                ? spacingElement.GetDouble() : 0.5;
            var marginInches = input.TryGetProperty("margin", out var marginElement)
                ? marginElement.GetDouble() : 1.5;

            // Convert inches to feet
            var spacing = spacingInches / 12.0;
            var margin = marginInches / 12.0;

            // Get usable area with margin applied
            var (areaMin, areaMax) = DraftingHelper.GetSheetUsableArea(doc, sheet);
            var usableMin = new XYZ(areaMin.X + margin, areaMin.Y + margin, 0);
            var usableMax = new XYZ(areaMax.X - margin, areaMax.Y - margin, 0);

            var usableWidth = usableMax.X - usableMin.X;
            var usableHeight = usableMax.Y - usableMin.Y;

            if (usableWidth <= 0 || usableHeight <= 0)
                return Task.FromResult(ToolResult.Error(
                    "Margin is too large for the sheet size. Reduce the margin value."));

            // Collect viewport info
            var vpInfos = new List<ViewportInfo>();
            foreach (var vpId in viewportIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;

                var outline = vp.GetBoxOutline();
                var width = outline.MaximumPoint.X - outline.MinimumPoint.X;
                var height = outline.MaximumPoint.Y - outline.MinimumPoint.Y;

                vpInfos.Add(new ViewportInfo
                {
                    Viewport = vp,
                    Width = width,
                    Height = height
                });
            }

            if (vpInfos.Count == 0)
                return Task.FromResult(ToolResult.Ok("No viewports found on sheet to arrange."));

            // Single viewport: just center it
            if (vpInfos.Count == 1)
            {
                var center = new XYZ(
                    (usableMin.X + usableMax.X) / 2.0,
                    (usableMin.Y + usableMax.Y) / 2.0, 0);
                vpInfos[0].Viewport.SetBoxCenter(center);

                return Task.FromResult(ToolResult.OkWithElements(
                    JsonSerializer.Serialize(new ArrangeResult
                    {
                        SheetNumber = sheet.SheetNumber,
                        SheetName = sheet.Name,
                        ViewportsArranged = 1,
                        LayoutMode = mode,
                        Message = $"Centered 1 viewport on sheet '{sheet.SheetNumber}'."
                    }, _jsonOptions),
                    new[] { vpInfos[0].Viewport.Id.Value }));
            }

            // Run layout algorithm
            switch (mode.ToLowerInvariant())
            {
                case "grid":
                    LayoutGrid(vpInfos, usableMin, usableWidth, usableHeight, spacing);
                    break;
                case "column":
                    LayoutColumn(vpInfos, usableMin, usableWidth, usableHeight, spacing);
                    break;
                default: // "auto"
                    LayoutAuto(vpInfos, usableMin, usableWidth, usableHeight, spacing);
                    break;
            }

            // Apply positions and collect results
            var elementIds = new List<long>();
            var viewportPositions = new List<ViewportPosition>();
            foreach (var vpi in vpInfos)
            {
                vpi.Viewport.SetBoxCenter(vpi.NewCenter!);
                elementIds.Add(vpi.Viewport.Id.Value);
                viewportPositions.Add(new ViewportPosition
                {
                    ViewportId = vpi.Viewport.Id.Value,
                    Center = new[] { Math.Round(vpi.NewCenter!.X, 4), Math.Round(vpi.NewCenter.Y, 4) }
                });
            }

            var result = new ArrangeResult
            {
                SheetNumber = sheet.SheetNumber,
                SheetName = sheet.Name,
                ViewportsArranged = vpInfos.Count,
                LayoutMode = mode,
                Viewports = viewportPositions,
                Message = $"Arranged {vpInfos.Count} viewports on sheet '{sheet.SheetNumber}' using {mode} layout."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), elementIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to arrange viewports: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Auto layout: row-based bin packing. Sort by height descending, place left-to-right,
    /// new row when width exceeded, center each row horizontally.
    /// </summary>
    private static void LayoutAuto(List<ViewportInfo> vpInfos, XYZ usableMin,
        double usableWidth, double usableHeight, double spacing)
    {
        // Sort by height descending (tallest first)
        vpInfos.Sort((a, b) => b.Height.CompareTo(a.Height));

        var rows = new List<List<ViewportInfo>>();
        var rowHeights = new List<double>();

        var currentRow = new List<ViewportInfo>();
        var currentRowWidth = 0.0;
        var currentRowHeight = 0.0;

        foreach (var vpi in vpInfos)
        {
            var neededWidth = currentRow.Count > 0 ? vpi.Width + spacing : vpi.Width;

            if (currentRow.Count > 0 && currentRowWidth + neededWidth > usableWidth)
            {
                // Start a new row
                rows.Add(currentRow);
                rowHeights.Add(currentRowHeight);
                currentRow = new List<ViewportInfo>();
                currentRowWidth = 0;
                currentRowHeight = 0;
            }

            currentRow.Add(vpi);
            if (currentRow.Count > 1)
                currentRowWidth += spacing;
            currentRowWidth += vpi.Width;
            currentRowHeight = Math.Max(currentRowHeight, vpi.Height);
        }

        if (currentRow.Count > 0)
        {
            rows.Add(currentRow);
            rowHeights.Add(currentRowHeight);
        }

        // Position rows top-to-bottom, clamped to top edge when content overflows
        var totalRowsHeight = rowHeights.Sum() + spacing * (rows.Count - 1);
        double yPos;
        if (totalRowsHeight >= usableHeight)
            yPos = usableMin.Y + usableHeight;
        else
            yPos = usableMin.Y + usableHeight - (usableHeight - totalRowsHeight) / 2.0;

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var rowHeight = rowHeights[r];
            yPos -= rowHeight / 2.0; // Center of this row

            // Calculate total row width for centering
            var totalWidth = row.Sum(v => v.Width) + spacing * (row.Count - 1);
            var xStart = usableMin.X + (usableWidth - totalWidth) / 2.0;

            var xPos = xStart;
            foreach (var vpi in row)
            {
                vpi.NewCenter = new XYZ(xPos + vpi.Width / 2.0, yPos, 0);
                xPos += vpi.Width + spacing;
            }

            yPos -= rowHeight / 2.0 + spacing;
        }
    }

    /// <summary>
    /// Grid layout: equal-sized cells. Sort by area descending, calculate grid dimensions,
    /// center each viewport in its cell.
    /// </summary>
    private static void LayoutGrid(List<ViewportInfo> vpInfos, XYZ usableMin,
        double usableWidth, double usableHeight, double spacing)
    {
        // Sort by area descending so largest viewports get prominent positions
        vpInfos.Sort((a, b) => (b.Width * b.Height).CompareTo(a.Width * a.Height));

        var count = vpInfos.Count;
        var cols = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling((double)count / cols);

        var cellWidth = (usableWidth - spacing * (cols - 1)) / cols;
        var cellHeight = (usableHeight - spacing * (rows - 1)) / rows;

        for (int i = 0; i < vpInfos.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;

            // Cell center (top-to-bottom, left-to-right)
            var cx = usableMin.X + col * (cellWidth + spacing) + cellWidth / 2.0;
            var cy = usableMin.Y + usableHeight - (row * (cellHeight + spacing) + cellHeight / 2.0);

            vpInfos[i].NewCenter = new XYZ(cx, cy, 0);
        }
    }

    /// <summary>
    /// Column layout: single vertical stack, sorted by height descending, centered horizontally.
    /// Clamps to top edge when content overflows.
    /// </summary>
    private static void LayoutColumn(List<ViewportInfo> vpInfos, XYZ usableMin,
        double usableWidth, double usableHeight, double spacing)
    {
        // Sort by height descending
        vpInfos.Sort((a, b) => b.Height.CompareTo(a.Height));

        var totalHeight = vpInfos.Sum(v => v.Height) + spacing * (vpInfos.Count - 1);
        var xCenter = usableMin.X + usableWidth / 2.0;
        double yPos;
        if (totalHeight >= usableHeight)
            yPos = usableMin.Y + usableHeight;
        else
            yPos = usableMin.Y + usableHeight - (usableHeight - totalHeight) / 2.0;

        foreach (var vpi in vpInfos)
        {
            yPos -= vpi.Height / 2.0;
            vpi.NewCenter = new XYZ(xCenter, yPos, 0);
            yPos -= vpi.Height / 2.0 + spacing;
        }
    }

    private sealed class ViewportInfo
    {
        public Viewport Viewport { get; set; } = null!;
        public double Width { get; set; }
        public double Height { get; set; }
        public XYZ? NewCenter { get; set; }
    }

    private sealed class ViewportPosition
    {
        public long ViewportId { get; set; }
        public double[] Center { get; set; } = Array.Empty<double>();
    }

    private sealed class ArrangeResult
    {
        public string SheetNumber { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public int ViewportsArranged { get; set; }
        public string LayoutMode { get; set; } = string.Empty;
        public List<ViewportPosition>? Viewports { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
