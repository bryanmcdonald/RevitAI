using Autodesk.Revit.DB;

namespace RevitAI.Tools.ReadTools.Helpers;

/// <summary>
/// Helper class for mapping user-friendly category names to BuiltInCategory enum values.
/// Supports case-insensitive lookup with singular/plural variations.
/// </summary>
public static class CategoryHelper
{
    /// <summary>
    /// Mapping of user-friendly names (lowercase) to BuiltInCategory.
    /// Includes both singular and plural forms.
    /// </summary>
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Walls
        { "wall", BuiltInCategory.OST_Walls },
        { "walls", BuiltInCategory.OST_Walls },

        // Doors
        { "door", BuiltInCategory.OST_Doors },
        { "doors", BuiltInCategory.OST_Doors },

        // Windows
        { "window", BuiltInCategory.OST_Windows },
        { "windows", BuiltInCategory.OST_Windows },

        // Floors
        { "floor", BuiltInCategory.OST_Floors },
        { "floors", BuiltInCategory.OST_Floors },

        // Ceilings
        { "ceiling", BuiltInCategory.OST_Ceilings },
        { "ceilings", BuiltInCategory.OST_Ceilings },

        // Roofs
        { "roof", BuiltInCategory.OST_Roofs },
        { "roofs", BuiltInCategory.OST_Roofs },

        // Rooms
        { "room", BuiltInCategory.OST_Rooms },
        { "rooms", BuiltInCategory.OST_Rooms },

        // Columns
        { "column", BuiltInCategory.OST_Columns },
        { "columns", BuiltInCategory.OST_Columns },
        { "structural column", BuiltInCategory.OST_StructuralColumns },
        { "structural columns", BuiltInCategory.OST_StructuralColumns },

        // Framing/Beams
        { "beam", BuiltInCategory.OST_StructuralFraming },
        { "beams", BuiltInCategory.OST_StructuralFraming },
        { "framing", BuiltInCategory.OST_StructuralFraming },
        { "structural framing", BuiltInCategory.OST_StructuralFraming },

        // Foundations
        { "foundation", BuiltInCategory.OST_StructuralFoundation },
        { "foundations", BuiltInCategory.OST_StructuralFoundation },
        { "structural foundation", BuiltInCategory.OST_StructuralFoundation },
        { "structural foundations", BuiltInCategory.OST_StructuralFoundation },

        // Stairs
        { "stair", BuiltInCategory.OST_Stairs },
        { "stairs", BuiltInCategory.OST_Stairs },

        // Railings
        { "railing", BuiltInCategory.OST_Railings },
        { "railings", BuiltInCategory.OST_Railings },

        // Ramps
        { "ramp", BuiltInCategory.OST_Ramps },
        { "ramps", BuiltInCategory.OST_Ramps },

        // Furniture
        { "furniture", BuiltInCategory.OST_Furniture },
        { "casework", BuiltInCategory.OST_Casework },

        // Generic Models
        { "generic model", BuiltInCategory.OST_GenericModel },
        { "generic models", BuiltInCategory.OST_GenericModel },

        // Plumbing
        { "plumbing fixture", BuiltInCategory.OST_PlumbingFixtures },
        { "plumbing fixtures", BuiltInCategory.OST_PlumbingFixtures },
        { "plumbing", BuiltInCategory.OST_PlumbingFixtures },
        { "pipe", BuiltInCategory.OST_PipeCurves },
        { "pipes", BuiltInCategory.OST_PipeCurves },
        { "pipe fitting", BuiltInCategory.OST_PipeFitting },
        { "pipe fittings", BuiltInCategory.OST_PipeFitting },

        // Mechanical/HVAC
        { "duct", BuiltInCategory.OST_DuctCurves },
        { "ducts", BuiltInCategory.OST_DuctCurves },
        { "duct fitting", BuiltInCategory.OST_DuctFitting },
        { "duct fittings", BuiltInCategory.OST_DuctFitting },
        { "mechanical equipment", BuiltInCategory.OST_MechanicalEquipment },
        { "air terminal", BuiltInCategory.OST_DuctTerminal },
        { "air terminals", BuiltInCategory.OST_DuctTerminal },

        // Electrical
        { "electrical equipment", BuiltInCategory.OST_ElectricalEquipment },
        { "electrical fixture", BuiltInCategory.OST_ElectricalFixtures },
        { "electrical fixtures", BuiltInCategory.OST_ElectricalFixtures },
        { "lighting fixture", BuiltInCategory.OST_LightingFixtures },
        { "lighting fixtures", BuiltInCategory.OST_LightingFixtures },
        { "light", BuiltInCategory.OST_LightingFixtures },
        { "lights", BuiltInCategory.OST_LightingFixtures },
        { "cable tray", BuiltInCategory.OST_CableTray },
        { "cable trays", BuiltInCategory.OST_CableTray },
        { "conduit", BuiltInCategory.OST_Conduit },
        { "conduits", BuiltInCategory.OST_Conduit },

        // Fire Protection
        { "sprinkler", BuiltInCategory.OST_Sprinklers },
        { "sprinklers", BuiltInCategory.OST_Sprinklers },
        { "fire alarm device", BuiltInCategory.OST_FireAlarmDevices },
        { "fire alarm devices", BuiltInCategory.OST_FireAlarmDevices },

        // Curtain Walls
        { "curtain wall", BuiltInCategory.OST_CurtainWallPanels },
        { "curtain wall panel", BuiltInCategory.OST_CurtainWallPanels },
        { "curtain wall panels", BuiltInCategory.OST_CurtainWallPanels },
        { "curtain wall mullion", BuiltInCategory.OST_CurtainWallMullions },
        { "curtain wall mullions", BuiltInCategory.OST_CurtainWallMullions },

        // Parking
        { "parking", BuiltInCategory.OST_Parking },

        // Site
        { "planting", BuiltInCategory.OST_Planting },
        { "plants", BuiltInCategory.OST_Planting },
        { "topography", BuiltInCategory.OST_Topography },
        { "site", BuiltInCategory.OST_Site },

        // Specialty Equipment
        { "specialty equipment", BuiltInCategory.OST_SpecialityEquipment },

        // Views and Sheets
        { "view", BuiltInCategory.OST_Views },
        { "views", BuiltInCategory.OST_Views },
        { "sheet", BuiltInCategory.OST_Sheets },
        { "sheets", BuiltInCategory.OST_Sheets },

        // Schedules
        { "schedule", BuiltInCategory.OST_Schedules },
        { "schedules", BuiltInCategory.OST_Schedules },

        // Levels and Grids
        { "level", BuiltInCategory.OST_Levels },
        { "levels", BuiltInCategory.OST_Levels },
        { "grid", BuiltInCategory.OST_Grids },
        { "grids", BuiltInCategory.OST_Grids },
    };

    /// <summary>
    /// List of common category names for error messages.
    /// </summary>
    public static readonly string[] CommonCategoryNames =
    {
        "Walls", "Doors", "Windows", "Floors", "Ceilings", "Roofs", "Rooms",
        "Columns", "Structural Columns", "Beams", "Framing", "Foundations",
        "Stairs", "Railings", "Ramps", "Furniture", "Casework", "Generic Models",
        "Plumbing Fixtures", "Pipes", "Ducts", "Mechanical Equipment",
        "Electrical Equipment", "Lighting Fixtures", "Sprinklers",
        "Curtain Wall Panels", "Views", "Sheets", "Levels", "Grids"
    };

    /// <summary>
    /// Tries to get a BuiltInCategory from a user-friendly name.
    /// </summary>
    /// <param name="categoryName">The user-friendly category name.</param>
    /// <param name="category">The resulting BuiltInCategory if found.</param>
    /// <returns>True if the category was found, false otherwise.</returns>
    public static bool TryGetCategory(string categoryName, out BuiltInCategory category)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            category = default;
            return false;
        }

        return CategoryMap.TryGetValue(categoryName.Trim(), out category);
    }

    /// <summary>
    /// Gets a BuiltInCategory from a user-friendly name, or null if not found.
    /// </summary>
    /// <param name="categoryName">The user-friendly category name.</param>
    /// <returns>The BuiltInCategory or null.</returns>
    public static BuiltInCategory? GetCategory(string categoryName)
    {
        if (TryGetCategory(categoryName, out var category))
            return category;
        return null;
    }

    /// <summary>
    /// Gets the display name for a BuiltInCategory.
    /// </summary>
    /// <param name="category">The built-in category.</param>
    /// <returns>A user-friendly display name.</returns>
    public static string GetDisplayName(BuiltInCategory category)
    {
        return category switch
        {
            BuiltInCategory.OST_Walls => "Walls",
            BuiltInCategory.OST_Doors => "Doors",
            BuiltInCategory.OST_Windows => "Windows",
            BuiltInCategory.OST_Floors => "Floors",
            BuiltInCategory.OST_Ceilings => "Ceilings",
            BuiltInCategory.OST_Roofs => "Roofs",
            BuiltInCategory.OST_Rooms => "Rooms",
            BuiltInCategory.OST_Columns => "Columns",
            BuiltInCategory.OST_StructuralColumns => "Structural Columns",
            BuiltInCategory.OST_StructuralFraming => "Structural Framing",
            BuiltInCategory.OST_StructuralFoundation => "Structural Foundations",
            BuiltInCategory.OST_Stairs => "Stairs",
            BuiltInCategory.OST_Railings => "Railings",
            BuiltInCategory.OST_Ramps => "Ramps",
            BuiltInCategory.OST_Furniture => "Furniture",
            BuiltInCategory.OST_Casework => "Casework",
            BuiltInCategory.OST_GenericModel => "Generic Models",
            BuiltInCategory.OST_PlumbingFixtures => "Plumbing Fixtures",
            BuiltInCategory.OST_PipeCurves => "Pipes",
            BuiltInCategory.OST_PipeFitting => "Pipe Fittings",
            BuiltInCategory.OST_DuctCurves => "Ducts",
            BuiltInCategory.OST_DuctFitting => "Duct Fittings",
            BuiltInCategory.OST_DuctTerminal => "Air Terminals",
            BuiltInCategory.OST_MechanicalEquipment => "Mechanical Equipment",
            BuiltInCategory.OST_ElectricalEquipment => "Electrical Equipment",
            BuiltInCategory.OST_ElectricalFixtures => "Electrical Fixtures",
            BuiltInCategory.OST_LightingFixtures => "Lighting Fixtures",
            BuiltInCategory.OST_CableTray => "Cable Trays",
            BuiltInCategory.OST_Conduit => "Conduits",
            BuiltInCategory.OST_Sprinklers => "Sprinklers",
            BuiltInCategory.OST_FireAlarmDevices => "Fire Alarm Devices",
            BuiltInCategory.OST_CurtainWallPanels => "Curtain Wall Panels",
            BuiltInCategory.OST_CurtainWallMullions => "Curtain Wall Mullions",
            BuiltInCategory.OST_Parking => "Parking",
            BuiltInCategory.OST_Planting => "Planting",
            BuiltInCategory.OST_Topography => "Topography",
            BuiltInCategory.OST_Site => "Site",
            BuiltInCategory.OST_SpecialityEquipment => "Specialty Equipment",
            BuiltInCategory.OST_Views => "Views",
            BuiltInCategory.OST_Sheets => "Sheets",
            BuiltInCategory.OST_Schedules => "Schedules",
            BuiltInCategory.OST_Levels => "Levels",
            BuiltInCategory.OST_Grids => "Grids",
            _ => category.ToString().Replace("OST_", "")
        };
    }

    /// <summary>
    /// Gets an error message listing valid category names.
    /// </summary>
    /// <param name="invalidName">The invalid category name that was provided.</param>
    /// <returns>An error message with suggestions.</returns>
    public static string GetInvalidCategoryError(string invalidName)
    {
        return $"Unknown category: '{invalidName}'. Valid categories include: {string.Join(", ", CommonCategoryNames)}.";
    }
}
