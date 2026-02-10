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
using Autodesk.Revit.UI;

namespace RevitAI.Tools.ModifyTools.Helpers;

/// <summary>
/// Helper class for looking up elements, types, and levels in a Revit document.
/// All lookups are case-insensitive and return helpful error messages with available options.
/// </summary>
public static class ElementLookupHelper
{
    /// <summary>
    /// Finds a level by its name (case-insensitive).
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="name">The level name to search for.</param>
    /// <returns>The Level if found, null otherwise.</returns>
    public static Level? FindLevelByName(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(l => string.Equals(l.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a comma-separated list of all available level names in the document.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <returns>A string listing all level names.</returns>
    public static string GetAvailableLevelNames(Document doc)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => l.Name)
            .ToList();

        return levels.Count > 0
            ? string.Join(", ", levels)
            : "No levels found in the document.";
    }

    /// <summary>
    /// Infers the active level from the current view.
    /// Returns the GenLevel of a ViewPlan, or null if not a plan view.
    /// </summary>
    /// <param name="app">The Revit UIApplication.</param>
    /// <returns>The inferred Level, or null.</returns>
    public static Level? InferLevelFromActiveView(UIApplication app)
    {
        var view = app.ActiveUIDocument?.ActiveView;
        if (view is ViewPlan viewPlan && viewPlan.GenLevel != null)
            return viewPlan.GenLevel;
        return null;
    }

    /// <summary>
    /// Finds a wall type by its name (case-insensitive).
    /// Supports both "Family: Type" format and just "Type" name.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="name">The wall type name to search for.</param>
    /// <returns>The WallType if found, null otherwise.</returns>
    public static WallType? FindWallTypeByName(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmedName = name.Trim();

        return new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .FirstOrDefault(wt =>
                string.Equals(wt.Name, trimmedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetFullTypeName(wt), trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets available wall type names in the document.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="maxCount">Maximum number of types to return.</param>
    /// <returns>A string listing available wall type names.</returns>
    public static string GetAvailableWallTypeNames(Document doc, int maxCount = 20)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .OrderBy(t => GetFullTypeName(t))
            .Take(maxCount)
            .Select(t => GetFullTypeName(t))
            .ToList();

        if (types.Count == 0)
            return "No wall types found in the document.";

        var result = string.Join(", ", types);
        if (types.Count == maxCount)
            result += ", ...";

        return result;
    }

    /// <summary>
    /// Finds a floor type by its name (case-insensitive).
    /// Supports both "Family: Type" format and just "Type" name.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="name">The floor type name to search for.</param>
    /// <returns>The FloorType if found, null otherwise.</returns>
    public static FloorType? FindFloorTypeByName(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmedName = name.Trim();

        return new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault(ft =>
                string.Equals(ft.Name, trimmedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetFullTypeName(ft), trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets available floor type names in the document.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="maxCount">Maximum number of types to return.</param>
    /// <returns>A string listing available floor type names.</returns>
    public static string GetAvailableFloorTypeNames(Document doc, int maxCount = 20)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .OrderBy(t => GetFullTypeName(t))
            .Take(maxCount)
            .Select(t => GetFullTypeName(t))
            .ToList();

        if (types.Count == 0)
            return "No floor types found in the document.";

        var result = string.Join(", ", types);
        if (types.Count == maxCount)
            result += ", ...";

        return result;
    }

    /// <summary>
    /// Finds a family symbol by family name and type name (case-insensitive).
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="familyName">The family name.</param>
    /// <param name="typeName">The type name.</param>
    /// <returns>The FamilySymbol if found, null otherwise.</returns>
    public static FamilySymbol? FindFamilySymbol(Document doc, string familyName, string typeName)
    {
        if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName))
            return null;

        var trimmedFamily = familyName.Trim();
        var trimmedType = typeName.Trim();

        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(fs =>
                string.Equals(fs.Family?.Name, trimmedFamily, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fs.Name, trimmedType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds a family symbol by a combined "Family: Type" name format (case-insensitive).
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="fullName">The full name in "Family: Type" format.</param>
    /// <returns>The FamilySymbol if found, null otherwise.</returns>
    public static FamilySymbol? FindFamilySymbolByFullName(Document doc, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var parts = fullName.Split(':');
        if (parts.Length == 2)
        {
            return FindFamilySymbol(doc, parts[0].Trim(), parts[1].Trim());
        }

        // Try to match against full name directly
        var trimmedName = fullName.Trim();
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(fs =>
                string.Equals(GetFullSymbolName(fs), trimmedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fs.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets available family symbols for a specific category.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="category">The built-in category.</param>
    /// <param name="maxCount">Maximum number of types to return.</param>
    /// <returns>A string listing available family symbol names.</returns>
    public static string GetAvailableTypeNames(Document doc, BuiltInCategory category, int maxCount = 20)
    {
        var types = new FilteredElementCollector(doc)
            .OfCategory(category)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .OrderBy(fs => GetFullSymbolName(fs))
            .Take(maxCount)
            .Select(fs => GetFullSymbolName(fs))
            .ToList();

        if (types.Count == 0)
            return $"No family types found for the specified category.";

        var result = string.Join(", ", types);
        if (types.Count == maxCount)
            result += ", ...";

        return result;
    }

    /// <summary>
    /// Finds a family symbol in a specific category by full name (case-insensitive).
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="category">The built-in category to search in.</param>
    /// <param name="fullName">The full name in "Family: Type" format.</param>
    /// <returns>The FamilySymbol if found, null otherwise.</returns>
    public static FamilySymbol? FindFamilySymbolInCategory(Document doc, BuiltInCategory category, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var trimmedName = fullName.Trim();

        // Try to parse "Family: Type" format
        var colonIndex = trimmedName.IndexOf(':');
        if (colonIndex > 0)
        {
            var familyName = trimmedName[..colonIndex].Trim();
            var typeName = trimmedName[(colonIndex + 1)..].Trim();

            var result = new FilteredElementCollector(doc)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    string.Equals(fs.Family?.Name, familyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(fs.Name, typeName, StringComparison.OrdinalIgnoreCase));

            if (result != null)
                return result;
        }

        // Fall back to matching full name or just type name
        return new FilteredElementCollector(doc)
            .OfCategory(category)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(fs =>
                string.Equals(GetFullSymbolName(fs), trimmedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fs.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    // ────────────────────────────────────────────────────────────────
    // Title Block lookups (P2-01)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a title block FamilySymbol by name (case-insensitive).
    /// Supports "Family: Type" or just the family/type name.
    /// </summary>
    public static FamilySymbol? FindTitleBlockType(Document doc, string name)
    {
        return FindFamilySymbolInCategory(doc, BuiltInCategory.OST_TitleBlocks, name);
    }

    /// <summary>
    /// Gets available title block names in the document.
    /// </summary>
    public static string GetAvailableTitleBlockNames(Document doc, int maxCount = 20)
    {
        return GetAvailableTypeNames(doc, BuiltInCategory.OST_TitleBlocks, maxCount);
    }

    // ────────────────────────────────────────────────────────────────
    // TextNoteType lookups (P2-01)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a TextNoteType by name (case-insensitive).
    /// </summary>
    public static TextNoteType? FindTextNoteType(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmedName = name.Trim();

        return new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault(t =>
                string.Equals(t.Name, trimmedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetFullTypeName(t), trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets available text note type names in the document.
    /// </summary>
    public static string GetAvailableTextNoteTypeNames(Document doc, int maxCount = 20)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .OrderBy(t => t.Name)
            .Take(maxCount)
            .Select(t => t.Name)
            .ToList();

        if (types.Count == 0)
            return "No text note types found in the document.";

        var result = string.Join(", ", types);
        if (types.Count == maxCount)
            result += ", ...";

        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // DimensionType lookups (P2-01)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a DimensionType by name (case-insensitive).
    /// </summary>
    public static DimensionType? FindDimensionType(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmedName = name.Trim();

        return new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .Cast<DimensionType>()
            .FirstOrDefault(t =>
                string.Equals(t.Name, trimmedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetFullTypeName(t), trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets available dimension type names in the document.
    /// </summary>
    public static string GetAvailableDimensionTypeNames(Document doc, int maxCount = 20)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .Cast<DimensionType>()
            .OrderBy(t => t.Name)
            .Take(maxCount)
            .Select(t => t.Name)
            .ToList();

        if (types.Count == 0)
            return "No dimension types found in the document.";

        var result = string.Join(", ", types);
        if (types.Count == maxCount)
            result += ", ...";

        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // Line Style lookups (P2-01)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a line style (GraphicsStyle) by name (case-insensitive).
    /// Searches among line-type subcategories of the Lines category.
    /// </summary>
    public static GraphicsStyle? FindLineStyle(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmedName = name.Trim();

        var lineCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        if (lineCategory == null)
            return null;

        foreach (Category subCat in lineCategory.SubCategories)
        {
            if (string.Equals(subCat.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
            {
                return subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets available line style names in the document.
    /// </summary>
    public static string GetAvailableLineStyleNames(Document doc, int maxCount = 20)
    {
        var lineCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        if (lineCategory == null)
            return "No line styles found in the document.";

        var names = new List<string>();
        foreach (Category subCat in lineCategory.SubCategories)
        {
            names.Add(subCat.Name);
            if (names.Count >= maxCount)
                break;
        }

        if (names.Count == 0)
            return "No line styles found in the document.";

        names.Sort(StringComparer.OrdinalIgnoreCase);
        var result = string.Join(", ", names.Take(maxCount));
        if (names.Count >= maxCount)
            result += ", ...";

        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // Tag type lookups (P2-01)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps element categories to their corresponding tag categories.
    /// </summary>
    public static readonly IReadOnlyDictionary<BuiltInCategory, BuiltInCategory> TagCategoryMap = new Dictionary<BuiltInCategory, BuiltInCategory>()
    {
        { BuiltInCategory.OST_Walls, BuiltInCategory.OST_WallTags },
        { BuiltInCategory.OST_Doors, BuiltInCategory.OST_DoorTags },
        { BuiltInCategory.OST_Windows, BuiltInCategory.OST_WindowTags },
        { BuiltInCategory.OST_Rooms, BuiltInCategory.OST_RoomTags },
        { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralColumnTags },
        { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFramingTags },
        { BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralFoundationTags },
        { BuiltInCategory.OST_Floors, BuiltInCategory.OST_FloorTags },
        { BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_CeilingTags },
        { BuiltInCategory.OST_Roofs, BuiltInCategory.OST_RoofTags },
        { BuiltInCategory.OST_Columns, BuiltInCategory.OST_ColumnTags },
        { BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureTags },
        { BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_MechanicalEquipmentTags },
        { BuiltInCategory.OST_Parking, BuiltInCategory.OST_ParkingTags },
        { BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_GenericModelTags },
    };

    /// <summary>
    /// Finds an appropriate tag FamilySymbol for the given element's category.
    /// If tagTypeName is specified, searches for that specific type; otherwise returns the first available.
    /// </summary>
    public static FamilySymbol? FindTagTypeForElement(Document doc, Element element, string? tagTypeName = null)
    {
        var elementCategoryId = element.Category?.BuiltInCategory;
        if (elementCategoryId == null)
            return null;

        if (!TagCategoryMap.TryGetValue(elementCategoryId.Value, out var tagCategory))
            return null;

        if (!string.IsNullOrWhiteSpace(tagTypeName))
        {
            return FindFamilySymbolInCategory(doc, tagCategory, tagTypeName);
        }

        // Return first available tag type for this category
        return new FilteredElementCollector(doc)
            .OfCategory(tagCategory)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets available tag type names for the given element's category.
    /// </summary>
    public static string GetAvailableTagTypesForCategory(Document doc, Element element, int maxCount = 20)
    {
        var elementCategoryId = element.Category?.BuiltInCategory;
        if (elementCategoryId == null)
            return "Element has no category.";

        if (!TagCategoryMap.TryGetValue(elementCategoryId.Value, out var tagCategory))
            return $"No tag types available for category '{element.Category?.Name}'.";

        return GetAvailableTypeNames(doc, tagCategory, maxCount);
    }

    // ────────────────────────────────────────────────────────────────
    // FilledRegionType lookups (P2-08.3)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets available non-masking FilledRegionType names in the document.
    /// </summary>
    public static string GetAvailableFilledRegionTypeNames(Document doc, int maxCount = 20)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegionType))
            .Cast<FilledRegionType>()
            .Where(frt => !frt.IsMasking)
            .OrderBy(frt => frt.Name)
            .Take(maxCount)
            .Select(frt => frt.Name)
            .ToList();

        if (types.Count == 0)
            return "No filled region types found in the document.";

        var result = string.Join(", ", types);
        if (types.Count == maxCount)
            result += ", ...";

        return result;
    }

    /// <summary>
    /// Gets available detail group (GroupType) names in the document.
    /// </summary>
    public static string GetAvailableDetailGroupNames(Document doc, int maxCount = 20)
    {
        var names = new FilteredElementCollector(doc)
            .OfClass(typeof(GroupType))
            .Cast<GroupType>()
            .Where(gt =>
            {
                // Filter to detail groups only
                var cat = gt.Category;
                if (cat == null) return false;
                return cat.BuiltInCategory == BuiltInCategory.OST_IOSDetailGroups;
            })
            .OrderBy(gt => gt.Name)
            .Take(maxCount)
            .Select(gt => gt.Name)
            .ToList();

        if (names.Count == 0)
            return "No detail groups found in the document.";

        var result = string.Join(", ", names);
        if (names.Count == maxCount)
            result += ", ...";

        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // Fuzzy matching (P2-04)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a family symbol in a specific category using fuzzy matching.
    /// Search order: exact "Family: Type" -> exact full name -> exact type name
    /// -> partial contains -> Levenshtein distance.
    /// </summary>
    /// <param name="doc">The Revit document.</param>
    /// <param name="category">The built-in category to search in.</param>
    /// <param name="fullName">The name to search for.</param>
    /// <returns>The matched FamilySymbol, whether the match was fuzzy, and the matched name.</returns>
    public static (FamilySymbol? Symbol, bool IsFuzzy, string? MatchedName) FindFamilySymbolInCategoryFuzzy(
        Document doc, BuiltInCategory category, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, false, null);

        // Try exact match first
        var exact = FindFamilySymbolInCategory(doc, category, fullName);
        if (exact != null)
            return (exact, false, GetFullSymbolName(exact));

        var trimmedName = fullName.Trim();
        var symbols = new FilteredElementCollector(doc)
            .OfCategory(category)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .ToList();

        // Partial contains match (case-insensitive)
        var containsMatch = symbols.FirstOrDefault(fs =>
            GetFullSymbolName(fs).Contains(trimmedName, StringComparison.OrdinalIgnoreCase) ||
            fs.Name.Contains(trimmedName, StringComparison.OrdinalIgnoreCase));

        if (containsMatch != null)
            return (containsMatch, true, GetFullSymbolName(containsMatch));

        // Levenshtein distance match - compare against type name only for short inputs
        var maxDistance = Math.Max(2, trimmedName.Length / 3);
        FamilySymbol? bestMatch = null;
        var bestDistance = int.MaxValue;
        string? bestName = null;

        foreach (var fs in symbols)
        {
            var typeNameDist = LevenshteinDistance(trimmedName, fs.Name);
            if (typeNameDist < bestDistance)
            {
                bestDistance = typeNameDist;
                bestMatch = fs;
                bestName = GetFullSymbolName(fs);
            }

            var fullNameStr = GetFullSymbolName(fs);
            var fullDist = LevenshteinDistance(trimmedName, fullNameStr);
            if (fullDist < bestDistance)
            {
                bestDistance = fullDist;
                bestMatch = fs;
                bestName = fullNameStr;
            }
        }

        if (bestMatch != null && bestDistance <= maxDistance)
            return (bestMatch, true, bestName);

        return (null, false, null);
    }

    /// <summary>
    /// Finds a wall type by name using fuzzy matching.
    /// </summary>
    public static (WallType? Type, bool IsFuzzy, string? MatchedName) FindWallTypeByNameFuzzy(
        Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, false, null);

        // Try exact match first
        var exact = FindWallTypeByName(doc, name);
        if (exact != null)
            return (exact, false, GetFullTypeName(exact));

        var trimmedName = name.Trim();
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .ToList();

        // Partial contains
        var containsMatch = types.FirstOrDefault(wt =>
            GetFullTypeName(wt).Contains(trimmedName, StringComparison.OrdinalIgnoreCase) ||
            wt.Name.Contains(trimmedName, StringComparison.OrdinalIgnoreCase));

        if (containsMatch != null)
            return (containsMatch, true, GetFullTypeName(containsMatch));

        // Levenshtein
        var maxDistance = Math.Max(2, trimmedName.Length / 3);
        WallType? bestMatch = null;
        var bestDistance = int.MaxValue;
        string? bestName = null;

        foreach (var wt in types)
        {
            var dist = LevenshteinDistance(trimmedName, wt.Name);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestMatch = wt;
                bestName = GetFullTypeName(wt);
            }

            var fullDist = LevenshteinDistance(trimmedName, GetFullTypeName(wt));
            if (fullDist < bestDistance)
            {
                bestDistance = fullDist;
                bestMatch = wt;
                bestName = GetFullTypeName(wt);
            }
        }

        if (bestMatch != null && bestDistance <= maxDistance)
            return (bestMatch, true, bestName);

        return (null, false, null);
    }

    /// <summary>
    /// Finds a floor type by name using fuzzy matching.
    /// </summary>
    public static (FloorType? Type, bool IsFuzzy, string? MatchedName) FindFloorTypeByNameFuzzy(
        Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, false, null);

        // Try exact match first
        var exact = FindFloorTypeByName(doc, name);
        if (exact != null)
            return (exact, false, GetFullTypeName(exact));

        var trimmedName = name.Trim();
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .ToList();

        // Partial contains
        var containsMatch = types.FirstOrDefault(ft =>
            GetFullTypeName(ft).Contains(trimmedName, StringComparison.OrdinalIgnoreCase) ||
            ft.Name.Contains(trimmedName, StringComparison.OrdinalIgnoreCase));

        if (containsMatch != null)
            return (containsMatch, true, GetFullTypeName(containsMatch));

        // Levenshtein
        var maxDistance = Math.Max(2, trimmedName.Length / 3);
        FloorType? bestMatch = null;
        var bestDistance = int.MaxValue;
        string? bestName = null;

        foreach (var ft in types)
        {
            var dist = LevenshteinDistance(trimmedName, ft.Name);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestMatch = ft;
                bestName = GetFullTypeName(ft);
            }

            var fullDist = LevenshteinDistance(trimmedName, GetFullTypeName(ft));
            if (fullDist < bestDistance)
            {
                bestDistance = fullDist;
                bestMatch = ft;
                bestName = GetFullTypeName(ft);
            }
        }

        if (bestMatch != null && bestDistance <= maxDistance)
            return (bestMatch, true, bestName);

        return (null, false, null);
    }

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings (case-insensitive).
    /// </summary>
    private static int LevenshteinDistance(string s, string t)
    {
        s = s.ToLowerInvariant();
        t = t.ToLowerInvariant();

        var n = s.Length;
        var m = t.Length;

        if (n == 0) return m;
        if (m == 0) return n;

        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    // ────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the full type name for an element type (Family: Type format).
    /// </summary>
    private static string GetFullTypeName(ElementType elemType)
    {
        var familyParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
        var familyName = familyParam?.AsString();

        return string.IsNullOrEmpty(familyName)
            ? elemType.Name
            : $"{familyName}: {elemType.Name}";
    }

    /// <summary>
    /// Gets the full symbol name (Family: Type format).
    /// </summary>
    private static string GetFullSymbolName(FamilySymbol symbol)
    {
        var familyName = symbol.Family?.Name;
        return string.IsNullOrEmpty(familyName)
            ? symbol.Name
            : $"{familyName}: {symbol.Name}";
    }
}
