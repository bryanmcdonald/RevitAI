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
/// Tool that returns element quantity takeoff grouped by category and type.
/// </summary>
public sealed class GetElementQuantityTakeoffTool : IRevitTool
{
    private const int MaxCategories = 50;
    private const int MaxTypesPerCategory = 100;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Categories to include in takeoff when no filter is specified.
    /// </summary>
    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Ceilings,
        BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_Stairs,
        BuiltInCategory.OST_Railings,
        BuiltInCategory.OST_Ramps,
        BuiltInCategory.OST_Furniture,
        BuiltInCategory.OST_Casework,
        BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_DuctCurves
    };

    static GetElementQuantityTakeoffTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "description": "Optional category to filter takeoff (e.g., 'Walls', 'Doors'). If not specified, returns counts for all major categories."
                    },
                    "level": {
                        "type": "string",
                        "description": "Optional level name to filter elements (e.g., 'Level 1')"
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

    public string Name => "get_element_quantity_takeoff";

    public string Description => "Returns element quantity takeoff grouped by category and type. Can filter by specific category and/or level. Shows total counts and breakdowns by family type.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Get optional category filter
            BuiltInCategory? categoryFilter = null;
            string? categoryFilterName = null;
            if (input.TryGetProperty("category", out var categoryElement))
            {
                var catName = categoryElement.GetString();
                if (!string.IsNullOrWhiteSpace(catName))
                {
                    if (!CategoryHelper.TryGetCategory(catName, out var builtInCat))
                        return Task.FromResult(ToolResult.Error(CategoryHelper.GetInvalidCategoryError(catName)));
                    categoryFilter = builtInCat;
                    categoryFilterName = CategoryHelper.GetDisplayName(builtInCat);
                }
            }

            // Get optional level filter
            Level? levelFilter = null;
            string? levelFilterName = null;
            if (input.TryGetProperty("level", out var levelElement))
            {
                var levelName = levelElement.GetString();
                if (!string.IsNullOrWhiteSpace(levelName))
                {
                    levelFilter = FindLevelByName(doc, levelName);
                    if (levelFilter == null)
                    {
                        var availableLevels = GetAvailableLevelNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Level '{levelName}' not found. Available levels: {string.Join(", ", availableLevels)}"));
                    }
                    levelFilterName = levelFilter.Name;
                }
            }

            // Determine which categories to process
            var categoriesToProcess = categoryFilter.HasValue
                ? new[] { categoryFilter.Value }
                : DefaultCategories;

            var summary = new Dictionary<string, CategoryTakeoff>();
            var totalElements = 0;

            foreach (var category in categoriesToProcess.Take(MaxCategories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var categoryTakeoff = ProcessCategory(doc, category, levelFilter);
                    if (categoryTakeoff != null && categoryTakeoff.Total > 0)
                    {
                        var displayName = CategoryHelper.GetDisplayName(category);
                        summary[displayName] = categoryTakeoff;
                        totalElements += categoryTakeoff.Total;
                    }
                }
                catch
                {
                    // Skip categories that fail
                }
            }

            var result = new GetQuantityTakeoffResult
            {
                Summary = summary,
                TotalElements = totalElements,
                CategoryFilter = categoryFilterName,
                LevelFilter = levelFilterName,
                CategoriesIncluded = summary.Count
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get quantity takeoff: {ex.Message}"));
        }
    }

    private static CategoryTakeoff? ProcessCategory(Document doc, BuiltInCategory category, Level? levelFilter)
    {
        var collector = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType();

        // Apply level filter if specified
        if (levelFilter != null)
        {
            collector = collector.WherePasses(new ElementLevelFilter(levelFilter.Id));
        }

        var elements = collector.ToElements();
        if (elements.Count == 0)
            return null;

        // Group by type
        var byType = new Dictionary<string, TypeCount>();

        foreach (var elem in elements)
        {
            var typeName = GetFullTypeName(elem, doc);

            if (!byType.TryGetValue(typeName, out var typeCount))
            {
                typeCount = new TypeCount { TypeName = typeName, Count = 0 };
                byType[typeName] = typeCount;
            }

            typeCount.Count++;
        }

        // Sort by count descending, limit results
        var sortedTypes = byType.Values
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.TypeName)
            .Take(MaxTypesPerCategory)
            .ToDictionary(t => t.TypeName, t => t.Count);

        return new CategoryTakeoff
        {
            Total = elements.Count,
            ByType = sortedTypes,
            TypeCount = byType.Count,
            Truncated = byType.Count > MaxTypesPerCategory
        };
    }

    private static string GetFullTypeName(Element elem, Document doc)
    {
        string? familyName = null;
        string? typeName = null;

        if (elem is FamilyInstance familyInstance)
        {
            familyName = familyInstance.Symbol?.Family?.Name;
            typeName = familyInstance.Symbol?.Name;
        }
        else if (elem.GetTypeId() != ElementId.InvalidElementId)
        {
            var elemType = doc.GetElement(elem.GetTypeId());
            if (elemType != null)
            {
                typeName = elemType.Name;
                var familyParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                familyName = familyParam?.AsString();
            }
        }

        if (string.IsNullOrEmpty(typeName))
            typeName = elem.Name ?? "Unknown";

        return string.IsNullOrEmpty(familyName)
            ? typeName
            : $"{familyName}: {typeName}";
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

    private sealed class TypeCount
    {
        public string TypeName { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class CategoryTakeoff
    {
        public int Total { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new();
        public int TypeCount { get; set; }
        public bool Truncated { get; set; }
    }

    private sealed class GetQuantityTakeoffResult
    {
        public Dictionary<string, CategoryTakeoff> Summary { get; set; } = new();
        public int TotalElements { get; set; }
        public string? CategoryFilter { get; set; }
        public string? LevelFilter { get; set; }
        public int CategoriesIncluded { get; set; }
    }
}
