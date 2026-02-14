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
/// Tool that places a view on a sheet as a viewport.
/// Supports resolution by sheet number or ID, and view name or ID.
/// </summary>
public sealed class PlaceViewportTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceViewportTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "description": "Requires at least one of sheet_id/sheet_number and one of view_id/view_name.",
                "properties": {
                    "sheet_id": {
                        "type": "integer",
                        "description": "Sheet element ID. Provide either sheet_id or sheet_number."
                    },
                    "sheet_number": {
                        "type": "string",
                        "description": "Sheet number (e.g., 'A101'). Provide either sheet_id or sheet_number."
                    },
                    "view_id": {
                        "type": "integer",
                        "description": "View element ID. Provide either view_id or view_name."
                    },
                    "view_name": {
                        "type": "string",
                        "description": "View name (e.g., 'Floor Plan - Level 1'). Case-insensitive with fuzzy matching. Provide either view_id or view_name."
                    },
                    "center": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Placement center on sheet as [x, y] in feet. Defaults to center of sheet if not specified."
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

    public string Name => "place_viewport";

    public string Description => "Places a view on a sheet as a viewport. Specify the sheet by number or ID, " +
        "and the view by name or ID. Use get_sheet_list to find sheets and their numbers.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var viewName = input.TryGetProperty("view_name", out var vn) ? vn.GetString() : null;
        var viewId = input.TryGetProperty("view_id", out var vi) ? vi.GetInt64().ToString() : null;
        var sheetNumber = input.TryGetProperty("sheet_number", out var sn) ? sn.GetString() : null;
        var sheetId = input.TryGetProperty("sheet_id", out var si) ? si.GetInt64().ToString() : null;

        var viewDesc = viewName ?? $"view ID {viewId}";
        var sheetDesc = sheetNumber ?? $"sheet ID {sheetId}";
        return $"Would place view '{viewDesc}' on sheet '{sheetDesc}'.";
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

            // Resolve view
            var (view, viewError) = DraftingHelper.ResolveViewForViewport(doc, input);
            if (viewError != null) return Task.FromResult(viewError);

            // Validate the view can be added to this sheet
            if (!Viewport.CanAddViewToSheet(doc, sheet!.Id, view!.Id))
            {
                // Try to give a helpful error message
                var existingSheet = FindSheetContainingView(doc, view.Id);
                if (existingSheet != null)
                    return Task.FromResult(ToolResult.Error(
                        $"View '{view.Name}' is already placed on sheet '{existingSheet.SheetNumber} - {existingSheet.Name}'. " +
                        "A view can only appear on one sheet. Remove it from the existing sheet first, or duplicate the view."));

                return Task.FromResult(ToolResult.Error(
                    $"View '{view.Name}' cannot be placed on sheet '{sheet.SheetNumber}'. " +
                    "It may be a template, a sheet, or otherwise ineligible for viewport placement."));
            }

            // Determine center point
            XYZ centerPoint;
            if (input.TryGetProperty("center", out var centerElement))
            {
                var coords = centerElement.EnumerateArray().ToList();
                centerPoint = new XYZ(coords[0].GetDouble(), coords[1].GetDouble(), 0);
            }
            else
            {
                // Default to center of sheet usable area
                var (min, max) = DraftingHelper.GetSheetUsableArea(doc, sheet);
                centerPoint = new XYZ((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, 0);
            }

            // Create the viewport
            var viewport = Viewport.Create(doc, sheet.Id, view.Id, centerPoint);

            var finalCenter = viewport.GetBoxCenter();
            var result = new PlaceViewportResult
            {
                ViewportId = viewport.Id.Value,
                SheetId = sheet.Id.Value,
                SheetNumber = sheet.SheetNumber,
                SheetName = sheet.Name,
                ViewId = view.Id.Value,
                ViewName = view.Name,
                Center = new[] { Math.Round(finalCenter.X, 4), Math.Round(finalCenter.Y, 4) },
                Message = $"Placed view '{view.Name}' on sheet '{sheet.SheetNumber} - {sheet.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { viewport.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to place viewport: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Finds the sheet that already contains a given view, if any.
    /// </summary>
    private static ViewSheet? FindSheetContainingView(Document doc, ElementId viewId)
    {
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(s => !s.IsPlaceholder);

        foreach (var sheet in sheets)
        {
            var viewportIds = sheet.GetAllViewports();
            foreach (var vpId in viewportIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp != null && vp.ViewId == viewId)
                    return sheet;
            }
        }

        return null;
    }

    private sealed class PlaceViewportResult
    {
        public long ViewportId { get; set; }
        public long SheetId { get; set; }
        public string SheetNumber { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public double[] Center { get; set; } = Array.Empty<double>();
        public string Message { get; set; } = string.Empty;
    }
}
