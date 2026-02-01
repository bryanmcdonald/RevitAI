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
