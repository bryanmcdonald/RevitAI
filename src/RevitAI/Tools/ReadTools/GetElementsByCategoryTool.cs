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
using RevitAI.Tools.ReadTools.Helpers;

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Tool that returns elements by category with optional level filter.
/// </summary>
public sealed class GetElementsByCategoryTool : IRevitTool
{
    private const int MaxElements = 100;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetElementsByCategoryTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "description": "The category to get elements from (e.g., 'Walls', 'Doors', 'Windows', 'Rooms')"
                    },
                    "level": {
                        "type": "string",
                        "description": "Optional level name to filter elements (e.g., 'Level 1', 'Ground Floor')"
                    }
                },
                "required": ["category"],
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

    public string Name => "get_elements_by_category";

    public string Description => "Returns elements from a specific category with optional level filter. Returns ID, family, type, level, and location for each element. Maximum 100 elements returned.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get category parameter
        if (!input.TryGetProperty("category", out var categoryElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: category"));

        var categoryName = categoryElement.GetString();
        if (string.IsNullOrWhiteSpace(categoryName))
            return Task.FromResult(ToolResult.Error("Parameter 'category' cannot be empty"));

        // Resolve category
        if (!CategoryHelper.TryGetCategory(categoryName, out var builtInCategory))
            return Task.FromResult(ToolResult.Error(CategoryHelper.GetInvalidCategoryError(categoryName)));

        // Get optional level filter
        string? levelFilter = null;
        Level? filterLevel = null;
        if (input.TryGetProperty("level", out var levelElement))
        {
            levelFilter = levelElement.GetString();
            if (!string.IsNullOrWhiteSpace(levelFilter))
            {
                filterLevel = FindLevelByName(doc, levelFilter);
                if (filterLevel == null)
                {
                    var availableLevels = GetAvailableLevelNames(doc);
                    return Task.FromResult(ToolResult.Error(
                        $"Level '{levelFilter}' not found. Available levels: {string.Join(", ", availableLevels)}"));
                }
            }
        }

        try
        {
            // Build collector - get all elements first, then filter by level
            // We can't rely solely on ElementLevelFilter because many elements
            // (structural framing, MEP, etc.) use Reference Level parameters instead of LevelId
            var collector = new FilteredElementCollector(doc)
                .OfCategory(builtInCategory)
                .WhereElementIsNotElementType();

            var allElements = collector.ToElements();

            // Apply level filter manually if specified (checks multiple level associations)
            if (filterLevel != null)
            {
                allElements = allElements
                    .Where(e => IsElementOnLevel(e, filterLevel, doc))
                    .ToList();
            }

            var totalCount = allElements.Count;
            var truncated = totalCount > MaxElements;

            var elements = allElements
                .Take(MaxElements)
                .Select(e => ExtractElementData(e, doc))
                .Where(e => e != null)
                .Cast<ElementData>()
                .ToList();

            var result = new GetElementsByCategoryResult
            {
                Category = CategoryHelper.GetDisplayName(builtInCategory),
                LevelFilter = filterLevel?.Name,
                Elements = elements,
                Count = totalCount,
                Truncated = truncated,
                TruncatedMessage = truncated ? $"Showing {MaxElements} of {totalCount} elements." : null
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get elements: {ex.Message}"));
        }
    }

    private static Level? FindLevelByName(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetAvailableLevelNames(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => l.Name)
            .ToList();
    }

    /// <summary>
    /// Checks if an element is associated with the specified level.
    /// Handles multiple level association methods used by different element types.
    /// </summary>
    private static bool IsElementOnLevel(Element elem, Level targetLevel, Document doc)
    {
        var targetLevelId = targetLevel.Id;

        // Check 1: Direct LevelId property (walls, floors, ceilings, etc.)
        if (elem.LevelId == targetLevelId)
            return true;

        // Check 2: Reference Level parameter (structural framing, columns, etc.)
        var refLevelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
        if (refLevelParam != null && refLevelParam.HasValue && refLevelParam.AsElementId() == targetLevelId)
            return true;

        // Check 3: Schedule Level parameter (many family instances)
        var scheduleLevelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
        if (scheduleLevelParam != null && scheduleLevelParam.HasValue && scheduleLevelParam.AsElementId() == targetLevelId)
            return true;

        // Check 4: Family Level parameter (hosted families)
        var familyLevelParam = elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (familyLevelParam != null && familyLevelParam.HasValue && familyLevelParam.AsElementId() == targetLevelId)
            return true;

        // Check 5: Base Level parameter (some structural elements)
        var baseLevelParam = elem.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
        if (baseLevelParam != null && baseLevelParam.HasValue && baseLevelParam.AsElementId() == targetLevelId)
            return true;

        // Check 6: Level parameter (generic)
        var levelParam = elem.get_Parameter(BuiltInParameter.LEVEL_PARAM);
        if (levelParam != null && levelParam.HasValue && levelParam.AsElementId() == targetLevelId)
            return true;

        return false;
    }

    private static ElementData? ExtractElementData(Element elem, Document doc)
    {
        var data = new ElementData
        {
            Id = elem.Id.Value,
            Category = elem.Category?.Name ?? "Unknown"
        };

        // Get family and type names
        if (elem is FamilyInstance familyInstance)
        {
            data.Family = familyInstance.Symbol?.Family?.Name;
            data.Type = familyInstance.Symbol?.Name;
        }
        else if (elem.GetTypeId() != ElementId.InvalidElementId)
        {
            var elemType = doc.GetElement(elem.GetTypeId());
            if (elemType != null)
            {
                data.Type = elemType.Name;
                var familyParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                data.Family = familyParam?.AsString();
            }
        }

        data.FullTypeName = string.IsNullOrEmpty(data.Family)
            ? data.Type ?? elem.Name ?? "Unknown"
            : $"{data.Family}: {data.Type}";

        // Get level
        var levelId = elem.LevelId;
        if (levelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(levelId) as Level;
            data.Level = level?.Name;
        }
        else
        {
            // Try to get level from parameters
            var levelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)
                          ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                          ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            if (levelParam != null && levelParam.HasValue)
            {
                var level = doc.GetElement(levelParam.AsElementId()) as Level;
                data.Level = level?.Name;
            }
        }

        // Get location
        data.Location = ExtractLocation(elem);

        // Get mark if available
        var markParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
        if (markParam != null && markParam.HasValue)
        {
            var mark = markParam.AsString();
            if (!string.IsNullOrEmpty(mark))
                data.Mark = mark;
        }

        return data;
    }

    private static LocationData? ExtractLocation(Element elem)
    {
        var location = elem.Location;

        if (location is LocationPoint locationPoint)
        {
            var pt = locationPoint.Point;
            return new LocationData
            {
                Type = "point",
                X = Math.Round(pt.X, 4),
                Y = Math.Round(pt.Y, 4),
                Z = Math.Round(pt.Z, 4)
            };
        }
        else if (location is LocationCurve locationCurve)
        {
            var curve = locationCurve.Curve;
            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);
            return new LocationData
            {
                Type = "curve",
                StartX = Math.Round(start.X, 4),
                StartY = Math.Round(start.Y, 4),
                EndX = Math.Round(end.X, 4),
                EndY = Math.Round(end.Y, 4)
            };
        }

        return null;
    }

    private sealed class LocationData
    {
        public string Type { get; set; } = string.Empty;
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public double? StartX { get; set; }
        public double? StartY { get; set; }
        public double? EndX { get; set; }
        public double? EndY { get; set; }
    }

    private sealed class ElementData
    {
        public long Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Family { get; set; }
        public string? Type { get; set; }
        public string FullTypeName { get; set; } = string.Empty;
        public string? Level { get; set; }
        public string? Mark { get; set; }
        public LocationData? Location { get; set; }
    }

    private sealed class GetElementsByCategoryResult
    {
        public string Category { get; set; } = string.Empty;
        public string? LevelFilter { get; set; }
        public List<ElementData> Elements { get; set; } = new();
        public int Count { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
    }
}
