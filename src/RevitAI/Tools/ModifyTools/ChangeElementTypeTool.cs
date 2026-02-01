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

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that changes the type of an element.
/// </summary>
public sealed class ChangeElementTypeTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ChangeElementTypeTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_id": {
                        "type": "integer",
                        "description": "The element ID of the element to change."
                    },
                    "new_type_name": {
                        "type": "string",
                        "description": "The name of the new type to apply (e.g., 'Basic Wall: Generic - 8\"')."
                    }
                },
                "required": ["element_id", "new_type_name"],
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

    public string Name => "change_element_type";

    public string Description => "Changes the type of an element to a different type within the same category. Use get_available_types to see valid type names. For walls, returns old/new width and perpendicular direction to help calculate face compensation moves.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get element_id parameter
        if (!input.TryGetProperty("element_id", out var elementIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_id"));

        // Get new_type_name parameter
        if (!input.TryGetProperty("new_type_name", out var newTypeNameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: new_type_name"));

        try
        {
            var elementId = new ElementId(elementIdElement.GetInt64());
            var element = doc.GetElement(elementId);

            if (element == null)
                return Task.FromResult(ToolResult.Error($"Element with ID {elementId.Value} not found."));

            var newTypeName = newTypeNameElement.GetString();
            if (string.IsNullOrWhiteSpace(newTypeName))
                return Task.FromResult(ToolResult.Error("new_type_name cannot be empty."));

            // Get current type info
            var currentTypeId = element.GetTypeId();
            var currentType = currentTypeId != ElementId.InvalidElementId
                ? doc.GetElement(currentTypeId) as ElementType
                : null;
            var oldTypeName = GetFullTypeName(currentType);

            // Get the element's category to filter available types
            var category = element.Category;
            if (category == null)
                return Task.FromResult(ToolResult.Error($"Element {elementId.Value} does not have a category and cannot have its type changed."));

            // Find the new type
            var newType = FindMatchingType(doc, category, newTypeName);
            if (newType == null)
            {
                var availableTypes = GetAvailableTypeNames(doc, category, 20);
                return Task.FromResult(ToolResult.Error(
                    $"Type '{newTypeName}' not found for category '{category.Name}'. " +
                    $"Available types: {availableTypes}"));
            }

            // Check if it's the same type
            if (currentTypeId == newType.Id)
            {
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(new ChangeTypeResult
                {
                    ElementId = elementId.Value,
                    Category = category.Name,
                    OldTypeName = oldTypeName,
                    NewTypeName = GetFullTypeName(newType),
                    Message = $"Element {elementId.Value} is already of type '{GetFullTypeName(newType)}'."
                }, _jsonOptions)));
            }

            // For walls, capture thickness info and location line for compensation guidance
            double? oldWidth = null;
            double? newWidth = null;
            string? locationLineSetting = null;
            double? locationLineOffsetFromExterior = null;
            double? locationLineOffsetFromInterior = null;
            double[]? wallDirection = null;
            double[]? perpendicularToExterior = null;

            if (element is Wall wall && currentType is WallType oldWallType && newType is WallType newWallType)
            {
                oldWidth = oldWallType.Width;
                newWidth = newWallType.Width;

                // Get wall's location line setting - this determines what stays fixed during type change
                var locationLineParam = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
                if (locationLineParam != null)
                {
                    var locationLineValue = (WallLocationLine)locationLineParam.AsInteger();
                    locationLineSetting = locationLineValue switch
                    {
                        WallLocationLine.WallCenterline => "Wall Centerline",
                        WallLocationLine.CoreCenterline => "Core Centerline",
                        WallLocationLine.FinishFaceExterior => "Finish Face: Exterior",
                        WallLocationLine.FinishFaceInterior => "Finish Face: Interior",
                        WallLocationLine.CoreExterior => "Core Face: Exterior",
                        WallLocationLine.CoreInterior => "Core Face: Interior",
                        _ => $"Unknown ({locationLineParam.AsInteger()})"
                    };

                    // Calculate offset from location line to each face for the NEW wall type
                    // This tells us how much to move to keep a specific face fixed
                    var newHalfWidth = newWallType.Width / 2;
                    locationLineOffsetFromExterior = locationLineValue switch
                    {
                        WallLocationLine.WallCenterline => newHalfWidth,
                        WallLocationLine.CoreCenterline => newHalfWidth, // Approximation
                        WallLocationLine.FinishFaceExterior => 0,
                        WallLocationLine.FinishFaceInterior => newWallType.Width,
                        WallLocationLine.CoreExterior => 0, // Approximation
                        WallLocationLine.CoreInterior => newWallType.Width, // Approximation
                        _ => newHalfWidth
                    };
                    locationLineOffsetFromInterior = newWallType.Width - locationLineOffsetFromExterior;
                }

                // Get wall direction for move compensation
                if (wall.Location is LocationCurve locationCurve)
                {
                    var curve = locationCurve.Curve;
                    var start = curve.GetEndPoint(0);
                    var end = curve.GetEndPoint(1);
                    var direction = (end - start).Normalize();

                    // Wall direction (along the wall)
                    wallDirection = new[] { Math.Round(direction.X, 4), Math.Round(direction.Y, 4) };

                    // Perpendicular direction pointing toward exterior (right side of wall direction)
                    perpendicularToExterior = new[] { Math.Round(direction.Y, 4), Math.Round(-direction.X, 4) };
                }
            }

            // Change the type
            element.ChangeTypeId(newType.Id);

            var result = new ChangeTypeResult
            {
                ElementId = elementId.Value,
                Category = category.Name,
                OldTypeName = oldTypeName,
                NewTypeName = GetFullTypeName(newType),
                OldWidth = oldWidth,
                NewWidth = newWidth,
                WidthDifference = (oldWidth.HasValue && newWidth.HasValue) ? newWidth.Value - oldWidth.Value : null,
                LocationLineSetting = locationLineSetting,
                LocationLineOffsetFromExterior = locationLineOffsetFromExterior,
                LocationLineOffsetFromInterior = locationLineOffsetFromInterior,
                WallDirection = wallDirection,
                PerpendicularToExterior = perpendicularToExterior,
                Message = BuildWallMessage(elementId.Value, oldTypeName, GetFullTypeName(newType),
                    oldWidth, newWidth, locationLineSetting)
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Cannot change element type: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static ElementType? FindMatchingType(Document doc, Category category, string typeName)
    {
        var trimmedName = typeName.Trim();
        var builtInCategory = (BuiltInCategory)category.Id.Value;

        // Try to find exact match first
        var types = new FilteredElementCollector(doc)
            .OfCategory(builtInCategory)
            .WhereElementIsElementType()
            .Cast<ElementType>()
            .ToList();

        // Try matching full name (Family: Type) or just type name
        return types.FirstOrDefault(t =>
            string.Equals(GetFullTypeName(t), trimmedName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetAvailableTypeNames(Document doc, Category category, int maxCount)
    {
        var builtInCategory = (BuiltInCategory)category.Id.Value;

        var types = new FilteredElementCollector(doc)
            .OfCategory(builtInCategory)
            .WhereElementIsElementType()
            .Cast<ElementType>()
            .OrderBy(t => GetFullTypeName(t))
            .Take(maxCount)
            .Select(t => GetFullTypeName(t))
            .ToList();

        if (types.Count == 0)
            return "No types available.";

        var result = string.Join(", ", types);
        if (types.Count == maxCount)
            result += ", ...";

        return result;
    }

    private static string GetFullTypeName(ElementType? elemType)
    {
        if (elemType == null)
            return "(none)";

        var familyParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
        var familyName = familyParam?.AsString();

        return string.IsNullOrEmpty(familyName)
            ? elemType.Name
            : $"{familyName}: {elemType.Name}";
    }

    private static string BuildWallMessage(long elementId, string oldType, string newType,
        double? oldWidth, double? newWidth, string? locationLine)
    {
        var msg = $"Changed element {elementId} from '{oldType}' to '{newType}'.";

        if (oldWidth.HasValue && newWidth.HasValue && locationLine != null)
        {
            var diff = newWidth.Value - oldWidth.Value;
            msg += $" Wall thickness: {oldWidth.Value * 12:F2}\" -> {newWidth.Value * 12:F2}\".";
            msg += $" Location line: '{locationLine}' (this face/line stayed fixed).";

            if (Math.Abs(diff) > 0.001)
            {
                // Provide guidance based on location line
                if (locationLine == "Finish Face: Exterior")
                {
                    msg += " EXTERIOR face is already fixed. To keep INTERIOR face fixed instead, ";
                    msg += $"move wall {Math.Abs(diff * 12):F2}\" toward exterior (use perpendicular_to_exterior * {(diff > 0 ? 1 : -1)}).";
                }
                else if (locationLine == "Finish Face: Interior")
                {
                    msg += " INTERIOR face is already fixed. To keep EXTERIOR face fixed instead, ";
                    msg += $"move wall {Math.Abs(diff * 12):F2}\" toward interior (use perpendicular_to_exterior * {(diff > 0 ? -1 : 1)}).";
                }
                else if (locationLine == "Wall Centerline")
                {
                    var halfDiff = Math.Abs(diff) / 2;
                    msg += $" Wall grew {Math.Abs(diff * 12):F2}\" total ({halfDiff * 12:F2}\" each side). ";
                    msg += $"To keep EXTERIOR fixed: move {halfDiff * 12:F2}\" toward interior. ";
                    msg += $"To keep INTERIOR fixed: move {halfDiff * 12:F2}\" toward exterior.";
                }
                else
                {
                    msg += $" Wall thickness changed by {diff * 12:F2}\". Check location_line_offset values for compensation.";
                }
            }
            else
            {
                msg += " No thickness change, no compensation needed.";
            }
        }

        return msg;
    }

    private sealed class ChangeTypeResult
    {
        public long ElementId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string OldTypeName { get; set; } = string.Empty;
        public string NewTypeName { get; set; } = string.Empty;
        public double? OldWidth { get; set; }
        public double? NewWidth { get; set; }
        public double? WidthDifference { get; set; }
        public string? LocationLineSetting { get; set; }
        public double? LocationLineOffsetFromExterior { get; set; }
        public double? LocationLineOffsetFromInterior { get; set; }
        public double[]? WallDirection { get; set; }
        public double[]? PerpendicularToExterior { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
