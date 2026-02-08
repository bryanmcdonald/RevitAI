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

namespace RevitAI.Models;

/// <summary>
/// Root container for Revit context data gathered before each Claude API call.
/// </summary>
public sealed class RevitContext
{
    /// <summary>
    /// Gets or sets whether an active document is open.
    /// </summary>
    public bool HasActiveDocument { get; set; }

    /// <summary>
    /// Gets or sets any error message encountered during context gathering.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the active view information.
    /// </summary>
    public ViewInfo? ActiveView { get; set; }

    /// <summary>
    /// Gets or sets the active level information.
    /// </summary>
    public LevelInfo? ActiveLevel { get; set; }

    /// <summary>
    /// Gets or sets the list of selected elements.
    /// </summary>
    public List<ElementInfo> SelectedElements { get; set; } = new();

    /// <summary>
    /// Gets or sets the project information (verbosity 2 only).
    /// </summary>
    public RevitProjectInfo? Project { get; set; }

    /// <summary>
    /// Gets or sets the available family types by category (verbosity 2 only).
    /// </summary>
    public Dictionary<string, List<TypeInfo>> AvailableTypes { get; set; } = new();

    /// <summary>
    /// Gets or sets the grid layout summary (verbosity 2 only).
    /// </summary>
    public GridSummary? GridInfo { get; set; }
}

/// <summary>
/// Information about the active Revit view.
/// </summary>
public sealed class ViewInfo
{
    /// <summary>
    /// Gets or sets the view name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the view type (e.g., FloorPlan, ThreeD, Section).
    /// </summary>
    public string ViewType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the view scale (e.g., 96 for 1/8" = 1'-0").
    /// </summary>
    public int Scale { get; set; }

    /// <summary>
    /// Gets or sets the formatted scale string (e.g., "1/8\" = 1'-0\"").
    /// </summary>
    public string? ScaleFormatted { get; set; }

    /// <summary>
    /// Gets or sets the associated level name (for plan views).
    /// </summary>
    public string? AssociatedLevel { get; set; }
}

/// <summary>
/// Information about a Revit level.
/// </summary>
public sealed class LevelInfo
{
    /// <summary>
    /// Gets or sets the level's element ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the level name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the elevation in feet.
    /// </summary>
    public double Elevation { get; set; }

    /// <summary>
    /// Gets or sets the formatted elevation string (e.g., "10'-0\"").
    /// </summary>
    public string ElevationFormatted { get; set; } = string.Empty;
}

/// <summary>
/// Information about a selected Revit element.
/// </summary>
public sealed class ElementInfo
{
    /// <summary>
    /// Gets or sets the element's ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the category name (e.g., "Walls", "Doors").
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the family name.
    /// </summary>
    public string? FamilyName { get; set; }

    /// <summary>
    /// Gets or sets the type name.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Gets or sets the full family:type name.
    /// </summary>
    public string FullTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the level name the element is on.
    /// </summary>
    public string? LevelName { get; set; }

    /// <summary>
    /// Gets or sets the element's location description (e.g., "(10.5, 20.3, 0.0)").
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Gets or sets the element's instance parameters.
    /// </summary>
    public List<ParameterInfo> Parameters { get; set; } = new();
}

/// <summary>
/// Information about an element parameter.
/// </summary>
public sealed class ParameterInfo
{
    /// <summary>
    /// Gets or sets the parameter name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parameter value as a string.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the storage type (String, Integer, Double, ElementId).
    /// </summary>
    public string StorageType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the parameter is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Gets or sets whether this is an instance parameter (vs type parameter).
    /// </summary>
    public bool IsInstance { get; set; }
}

/// <summary>
/// Information about an available family type.
/// </summary>
public sealed class TypeInfo
{
    /// <summary>
    /// Gets or sets the type's element ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the family name.
    /// </summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type name.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full name (Family: Type).
    /// </summary>
    public string FullName { get; set; } = string.Empty;
}

/// <summary>
/// Information about the Revit project.
/// </summary>
public sealed class RevitProjectInfo
{
    /// <summary>
    /// Gets or sets the project name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the project number.
    /// </summary>
    public string? Number { get; set; }

    /// <summary>
    /// Gets or sets the client name.
    /// </summary>
    public string? Client { get; set; }

    /// <summary>
    /// Gets or sets the project address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the file path of the project.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets all levels in the project.
    /// </summary>
    public List<LevelInfo> Levels { get; set; } = new();
}

/// <summary>
/// Summary of grids in the project, classified by orientation.
/// </summary>
public sealed class GridSummary
{
    /// <summary>
    /// Gets or sets the names of horizontal (east-west) grids.
    /// </summary>
    public List<string> HorizontalGrids { get; set; } = new();

    /// <summary>
    /// Gets or sets the names of vertical (north-south) grids.
    /// </summary>
    public List<string> VerticalGrids { get; set; } = new();

    /// <summary>
    /// Gets or sets the total number of grids in the project.
    /// </summary>
    public int TotalCount { get; set; }
}
