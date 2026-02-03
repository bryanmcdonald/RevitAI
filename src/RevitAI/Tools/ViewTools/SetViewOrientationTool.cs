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
/// Tool that sets a 3D view to a preset orientation.
/// </summary>
public sealed class SetViewOrientationTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static SetViewOrientationTool()
    {
        // Build the enum list from the helper
        var presetsList = string.Join(", ", ViewOrientationHelper.ValidPresets.Select(p => $"\"{p}\""));

        var schemaJson = $$"""
            {
                "type": "object",
                "properties": {
                    "orientation": {
                        "type": "string",
                        "enum": [{{presetsList}}],
                        "description": "The preset orientation to apply. Options: isometric (SE corner, default), isometric_ne, isometric_nw, isometric_se, isometric_sw, front, back, left, right, top, bottom."
                    }
                },
                "required": ["orientation"],
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

    public string Name => "set_view_orientation";

    public string Description =>
        "Sets a 3D view to a preset orientation. Available presets: isometric (SE corner), " +
        "isometric_ne, isometric_nw, isometric_se, isometric_sw (corner views), " +
        "front, back, left, right (orthographic sides), top, bottom. Only works in 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;

        // Verify this is a 3D view
        if (uiDoc.ActiveView is not View3D view3d)
            return Task.FromResult(ToolResult.Error("This tool requires a 3D view. Please switch to a 3D view first."));

        if (view3d.IsLocked)
            return Task.FromResult(ToolResult.Error("Cannot modify a locked 3D view. Please unlock the view or use a different 3D view."));

        // Get orientation parameter
        if (!input.TryGetProperty("orientation", out var orientationElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: orientation"));

        var preset = orientationElement.GetString()!;

        if (!ViewOrientationHelper.IsValidPreset(preset))
        {
            var validPresets = string.Join(", ", ViewOrientationHelper.ValidPresets);
            return Task.FromResult(ToolResult.Error($"Invalid orientation preset: '{preset}'. Valid options: {validPresets}"));
        }

        try
        {
            // Calculate the model center to use as the view target
            // Priority: selected elements > section box > visible model center
            var modelCenter = GetViewTarget(uiDoc, view3d);

            // Get the preset orientation
            var newOrientation = ViewOrientationHelper.GetPresetOrientation(preset, modelCenter);

            if (newOrientation == null)
                return Task.FromResult(ToolResult.Error($"Failed to calculate orientation for preset: {preset}"));

            // Apply the new orientation
            view3d.SetOrientation(newOrientation);

            // Force the view to refresh by getting the UIView and zooming to fit
            var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view3d.Id);
            uiView?.ZoomToFit();

            var result = new SetViewOrientationResult
            {
                ViewName = view3d.Name,
                Orientation = preset,
                Message = $"View orientation set to '{preset}'."
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
    /// Gets the target point for view orientation.
    /// Priority: selected elements > section box > visible model center > origin.
    /// </summary>
    private static XYZ GetViewTarget(UIDocument uiDoc, View3D view3d)
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

        // Second priority: section box center if active
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

        // Third priority: center of visible model elements
        var modelCenter = GetVisibleModelCenter(doc, view3d);
        if (modelCenter != null)
            return modelCenter;

        // Fallback to origin
        return XYZ.Zero;
    }

    /// <summary>
    /// Gets the center of the bounding box for the given element IDs.
    /// </summary>
    private static XYZ? GetBoundingBoxCenter(Document doc, ICollection<ElementId> elementIds)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool foundAny = false;

        foreach (var id in elementIds)
        {
            var element = doc.GetElement(id);
            var bbox = element?.get_BoundingBox(null);
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

        if (!foundAny)
            return null;

        return new XYZ(
            (minX + maxX) / 2,
            (minY + maxY) / 2,
            (minZ + maxZ) / 2
        );
    }

    /// <summary>
    /// Gets the center of all visible model elements in the view.
    /// </summary>
    private static XYZ? GetVisibleModelCenter(Document doc, View3D view3d)
    {
        try
        {
            var collector = new FilteredElementCollector(doc, view3d.Id)
                .WhereElementIsNotElementType();

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool foundAny = false;

            foreach (var element in collector)
            {
                // Skip site/topography categories
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

    private sealed class SetViewOrientationResult
    {
        public string ViewName { get; set; } = string.Empty;
        public string Orientation { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
