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
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a structural column at a specified location.
/// </summary>
public sealed class PlaceColumnTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceColumnTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "location": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Column location [x, y] in feet."
                    },
                    "column_type": {
                        "type": "string",
                        "description": "Column type name in 'Family: Type' format (e.g., 'W-Wide Flange-Column: W10x49')."
                    },
                    "base_level": {
                        "type": "string",
                        "description": "Name of the base level (e.g., 'Level 1')."
                    },
                    "top_level": {
                        "type": "string",
                        "description": "Name of the top level. Optional - uses unconnected height if not specified."
                    },
                    "base_offset": {
                        "type": "number",
                        "description": "Offset from the base level in feet. Positive is up. Default is 0."
                    },
                    "top_offset": {
                        "type": "number",
                        "description": "Offset from the top level in feet. Positive is up. Default is 0. Only used if top_level is specified."
                    }
                },
                "required": ["location", "column_type", "base_level"],
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

    public string Name => "place_column";

    public string Description => "Places a structural column at a location. Use get_levels to see available levels and get_available_types with 'Structural Columns' to see column types.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var columnType = input.TryGetProperty("column_type", out var typeElem) ? typeElem.GetString() ?? "unknown" : "unknown";
        var baseLevel = input.TryGetProperty("base_level", out var levelElem) ? levelElem.GetString() ?? "unknown" : "unknown";
        if (input.TryGetProperty("location", out var locElem))
        {
            var coords = locElem.EnumerateArray().ToList();
            if (coords.Count == 2)
            {
                return $"Would place a '{columnType}' column at ({coords[0].GetDouble():F2}, {coords[1].GetDouble():F2}) on {baseLevel}.";
            }
        }
        return $"Would place a '{columnType}' column on {baseLevel}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get required parameters
        if (!input.TryGetProperty("location", out var locationElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: location"));

        if (!input.TryGetProperty("column_type", out var columnTypeElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: column_type"));

        if (!input.TryGetProperty("base_level", out var baseLevelElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: base_level"));

        try
        {
            // Parse location
            var locationArray = locationElement.EnumerateArray().ToList();
            if (locationArray.Count != 2)
                return Task.FromResult(ToolResult.Error("location must be an array of exactly 2 numbers [x, y]."));
            var x = locationArray[0].GetDouble();
            var y = locationArray[1].GetDouble();

            // Find base level
            var baseLevelName = baseLevelElement.GetString();
            if (string.IsNullOrWhiteSpace(baseLevelName))
                return Task.FromResult(ToolResult.Error("base_level cannot be empty."));

            var baseLevel = ElementLookupHelper.FindLevelByName(doc, baseLevelName);
            if (baseLevel == null)
            {
                var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                return Task.FromResult(ToolResult.Error(
                    $"Level '{baseLevelName}' not found. Available levels: {availableLevels}"));
            }

            // Find column type
            var columnTypeName = columnTypeElement.GetString();
            if (string.IsNullOrWhiteSpace(columnTypeName))
                return Task.FromResult(ToolResult.Error("column_type cannot be empty."));

            var columnSymbol = ElementLookupHelper.FindFamilySymbolInCategory(
                doc, BuiltInCategory.OST_StructuralColumns, columnTypeName);

            if (columnSymbol == null)
            {
                var availableTypes = ElementLookupHelper.GetAvailableTypeNames(doc, BuiltInCategory.OST_StructuralColumns);
                return Task.FromResult(ToolResult.Error(
                    $"Column type '{columnTypeName}' not found. Available types: {availableTypes}"));
            }

            // Activate symbol if not already active
            if (!columnSymbol.IsActive)
            {
                columnSymbol.Activate();
                doc.Regenerate();
            }

            // Find optional top level
            Level? topLevel = null;
            if (input.TryGetProperty("top_level", out var topLevelElement))
            {
                var topLevelName = topLevelElement.GetString();
                if (!string.IsNullOrWhiteSpace(topLevelName))
                {
                    topLevel = ElementLookupHelper.FindLevelByName(doc, topLevelName);
                    if (topLevel == null)
                    {
                        var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Top level '{topLevelName}' not found. Available levels: {availableLevels}"));
                    }

                    // Validate top level is above base level
                    if (topLevel.Elevation <= baseLevel.Elevation)
                    {
                        return Task.FromResult(ToolResult.Error(
                            $"Top level '{topLevelName}' must be above base level '{baseLevelName}'."));
                    }
                }
            }

            // Get optional base offset
            double baseOffset = 0.0;
            if (input.TryGetProperty("base_offset", out var baseOffsetElement))
            {
                baseOffset = baseOffsetElement.GetDouble();
            }

            // Get optional top offset
            double topOffset = 0.0;
            if (input.TryGetProperty("top_offset", out var topOffsetElement))
            {
                topOffset = topOffsetElement.GetDouble();
            }

            // Create column location point at base level elevation
            var location = new XYZ(x, y, baseLevel.Elevation);

            // Create the column
            FamilyInstance column;
            if (topLevel != null)
            {
                // Create column spanning between two levels
                column = doc.Create.NewFamilyInstance(
                    location,
                    columnSymbol,
                    baseLevel,
                    StructuralType.Column);

                // Set the top level constraint
                var topLevelParam = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                topLevelParam?.Set(topLevel.Id);

                // Set base offset
                var baseOffsetParam = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                baseOffsetParam?.Set(baseOffset);

                // Set top offset
                var topOffsetParam = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                topOffsetParam?.Set(topOffset);
            }
            else
            {
                // Create unconnected column
                column = doc.Create.NewFamilyInstance(
                    location,
                    columnSymbol,
                    baseLevel,
                    StructuralType.Column);

                // Set base offset even for unconnected columns
                if (baseOffset != 0)
                {
                    var baseOffsetParam = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                    baseOffsetParam?.Set(baseOffset);
                }
            }

            var result = new PlaceColumnResult
            {
                ColumnId = column.Id.Value,
                ColumnType = $"{columnSymbol.Family?.Name}: {columnSymbol.Name}",
                BaseLevel = baseLevel.Name,
                TopLevel = topLevel?.Name,
                BaseOffset = baseOffset,
                TopOffset = topLevel != null ? topOffset : null,
                Location = new[] { x, y },
                Message = topLevel != null
                    ? $"Created column from {baseLevel.Name} to {topLevel.Name} at ({x:F2}, {y:F2})."
                    : $"Created column on {baseLevel.Name} at ({x:F2}, {y:F2})."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceColumnResult
    {
        public long ColumnId { get; set; }
        public string ColumnType { get; set; } = string.Empty;
        public string BaseLevel { get; set; } = string.Empty;
        public string? TopLevel { get; set; }
        public double BaseOffset { get; set; }
        public double? TopOffset { get; set; }
        public double[] Location { get; set; } = Array.Empty<double>();
        public string Message { get; set; } = string.Empty;
    }
}
