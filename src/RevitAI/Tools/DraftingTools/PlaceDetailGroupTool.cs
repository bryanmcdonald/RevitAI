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
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that places a detail group instance in a view.
/// Filters GroupType to detail groups only (OST_IOSDetailGroups category).
/// </summary>
public sealed class PlaceDetailGroupTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailGroupTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "group_type_name": {
                        "type": "string",
                        "description": "Name of the detail group type to place."
                    },
                    "location": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Placement location [x, y] or [x, y, z] in feet."
                    },
                    "rotation": {
                        "type": "number",
                        "description": "Rotation angle in degrees (counter-clockwise). Optional - defaults to 0."
                    }
                },
                "required": ["group_type_name", "location"],
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

    public string Name => "place_detail_group";

    public string Description => "Places a detail group instance in the active view. Detail groups contain reusable arrangements of detail elements. The active view must be a 2D view (plan, section, elevation, or drafting). Coordinates are in feet.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var groupName = input.TryGetProperty("group_type_name", out var g) ? g.GetString() : "unknown";
        var rotation = input.TryGetProperty("rotation", out var r) ? r.GetDouble() : 0;

        if (rotation != 0)
            return $"Would place detail group '{groupName}' rotated {rotation:F1} degrees.";
        return $"Would place detail group '{groupName}'.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // PlaceGroup always uses the active view (no view_id parameter in API)
            var view = doc.ActiveView;
            if (view == null)
                return Task.FromResult(ToolResult.Error("No active view available."));
            if (view.ViewType == ViewType.ThreeD)
                return Task.FromResult(ToolResult.Error("Detail groups cannot be placed in 3D views. Switch to a plan, section, elevation, or drafting view."));
            if (view.ViewType == ViewType.Schedule || view.ViewType == ViewType.ColumnSchedule ||
                view.ViewType == ViewType.PanelSchedule)
                return Task.FromResult(ToolResult.Error("Detail groups cannot be placed in schedule views."));
            if (view.ViewType == ViewType.DrawingSheet)
                return Task.FromResult(ToolResult.Error("Detail groups cannot be placed directly on sheets."));

            // Parse group type name
            if (!input.TryGetProperty("group_type_name", out var groupNameElement) || string.IsNullOrWhiteSpace(groupNameElement.GetString()))
                return Task.FromResult(ToolResult.Error("Missing required parameter: group_type_name"));
            var groupTypeName = groupNameElement.GetString()!.Trim();

            // Parse location
            var (location, locationError) = DraftingHelper.ParsePoint(input, "location");
            if (locationError != null) return Task.FromResult(locationError);

            // Find detail group type (filter to detail groups only)
            var groupType = new FilteredElementCollector(doc)
                .OfClass(typeof(GroupType))
                .Cast<GroupType>()
                .Where(gt =>
                {
                    var cat = gt.Category;
                    if (cat == null) return false;
                    return cat.BuiltInCategory == BuiltInCategory.OST_IOSDetailGroups;
                })
                .FirstOrDefault(gt => string.Equals(gt.Name, groupTypeName, StringComparison.OrdinalIgnoreCase));

            if (groupType == null)
            {
                var available = ElementLookupHelper.GetAvailableDetailGroupNames(doc);
                return Task.FromResult(ToolResult.Error(
                    $"Detail group type '{groupTypeName}' not found. Available detail groups: {available}"));
            }

            // Place the group
            var group = doc.Create.PlaceGroup(location!, groupType);

            // Apply rotation if specified
            var rotation = 0.0;
            if (input.TryGetProperty("rotation", out var rotationElement))
            {
                rotation = rotationElement.GetDouble();
                if (Math.Abs(rotation) > 0.001)
                {
                    var radians = DraftingHelper.DegreesToRadians(rotation);
                    var axis = Line.CreateBound(location!, location! + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, group.Id, axis, radians);
                }
            }

            // Count members
            var memberCount = group.GetMemberIds().Count;

            var result = new PlaceDetailGroupResult
            {
                ElementIds = new[] { group.Id.Value },
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                GroupTypeName = groupType.Name,
                Location = new[] { location!.X, location.Y },
                Rotation = Math.Abs(rotation) > 0.001 ? Math.Round(rotation, 2) : null,
                MemberCount = memberCount,
                Message = $"Placed detail group '{groupType.Name}' ({memberCount} members) in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { group.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to place detail group: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceDetailGroupResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string GroupTypeName { get; set; } = string.Empty;
        public double[] Location { get; set; } = Array.Empty<double>();
        public double? Rotation { get; set; }
        public int MemberCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
