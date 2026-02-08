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
using RevitAI.Services;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a structural column at a specified location.
/// Supports grid intersections, relative positions, and raw coordinates.
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
                        "description": "Column location [x, y] in feet. Optional if grid_intersection or relative_to is provided."
                    },
                    "grid_intersection": {
                        "type": "object",
                        "properties": {
                            "grid1": { "type": "string", "description": "Name of the first grid." },
                            "grid2": { "type": "string", "description": "Name of the second grid." }
                        },
                        "required": ["grid1", "grid2"],
                        "additionalProperties": false,
                        "description": "Place column at the intersection of two grids."
                    },
                    "relative_to": {
                        "type": "object",
                        "properties": {
                            "element_id": { "type": "integer", "description": "Element ID of the reference element." },
                            "direction": { "type": "string", "description": "Horizontal direction: north, south, east, or west." },
                            "distance": { "type": "number", "description": "Distance in feet from the reference element." }
                        },
                        "required": ["element_id", "direction", "distance"],
                        "additionalProperties": false,
                        "description": "Place column relative to an existing element."
                    },
                    "column_type": {
                        "type": "string",
                        "description": "Column type name (e.g., 'W10x49' or 'W-Wide Flange-Column: W10x49'). Supports fuzzy matching."
                    },
                    "base_level": {
                        "type": "string",
                        "description": "Name of the base level. Optional - defaults to the active view's level."
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
                "required": ["column_type"],
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

    public string Description => "Places a structural column. Accepts grid intersections (e.g., grids A and 1), relative positions (e.g., 3' east of element), or raw [x,y] coordinates. Level defaults to active view. Type name supports fuzzy matching.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var columnType = input.TryGetProperty("column_type", out var typeElem) ? typeElem.GetString() ?? "unknown" : "unknown";
        var baseLevel = input.TryGetProperty("base_level", out var levelElem) ? levelElem.GetString() ?? "active view level" : "active view level";

        if (input.TryGetProperty("grid_intersection", out var gridElem))
        {
            var g1 = gridElem.TryGetProperty("grid1", out var g1E) ? g1E.GetString() : "?";
            var g2 = gridElem.TryGetProperty("grid2", out var g2E) ? g2E.GetString() : "?";
            return $"Would place a '{columnType}' column at grid {g1}/{g2} on {baseLevel}.";
        }

        if (input.TryGetProperty("relative_to", out var relElem))
        {
            var dir = relElem.TryGetProperty("direction", out var dirE) ? dirE.GetString() : "?";
            var dist = relElem.TryGetProperty("distance", out var distE) ? distE.GetDouble() : 0;
            var elemId = relElem.TryGetProperty("element_id", out var idE) ? idE.GetInt64() : 0;
            return $"Would place a '{columnType}' column {dist:F1}' {dir} of element {elemId} on {baseLevel}.";
        }

        if (input.TryGetProperty("location", out var locElem))
        {
            var coords = locElem.EnumerateArray().ToList();
            if (coords.Count == 2)
                return $"Would place a '{columnType}' column at ({coords[0].GetDouble():F2}, {coords[1].GetDouble():F2}) on {baseLevel}.";
        }

        return $"Would place a '{columnType}' column on {baseLevel}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("column_type", out var columnTypeElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: column_type"));

        try
        {
            // ── Resolve location ──────────────────────────────────────
            double x, y;
            string? resolvedFrom = null;

            if (input.TryGetProperty("grid_intersection", out var gridElem))
            {
                var g1 = gridElem.GetProperty("grid1").GetString()!;
                var g2 = gridElem.GetProperty("grid2").GetString()!;
                var (point, error) = GeometryResolver.ResolveGridIntersection(doc, g1, g2);
                if (point == null)
                    return Task.FromResult(ToolResult.Error(error!));
                x = point.X;
                y = point.Y;
                resolvedFrom = $"grid {g1}/{g2}";
            }
            else if (input.TryGetProperty("relative_to", out var relElem))
            {
                var elemId = relElem.GetProperty("element_id").GetInt64();
                var direction = relElem.GetProperty("direction").GetString()!;
                var distance = relElem.GetProperty("distance").GetDouble();
                var (point, error) = GeometryResolver.ResolveRelativePosition(doc, elemId, direction, distance);
                if (point == null)
                    return Task.FromResult(ToolResult.Error(error!));
                x = point.X;
                y = point.Y;
                resolvedFrom = $"{distance:F1}' {direction} of element {elemId}";
            }
            else if (input.TryGetProperty("location", out var locationElement))
            {
                var locationArray = locationElement.EnumerateArray().ToList();
                if (locationArray.Count != 2)
                    return Task.FromResult(ToolResult.Error("location must be an array of exactly 2 numbers [x, y]."));
                x = locationArray[0].GetDouble();
                y = locationArray[1].GetDouble();
            }
            else
            {
                return Task.FromResult(ToolResult.Error("Must provide one of: location, grid_intersection, or relative_to."));
            }

            // ── Resolve base level ───────────────────────────────────
            Level? baseLevel = null;
            if (input.TryGetProperty("base_level", out var baseLevelElement))
            {
                var baseLevelName = baseLevelElement.GetString();
                if (!string.IsNullOrWhiteSpace(baseLevelName))
                {
                    baseLevel = ElementLookupHelper.FindLevelByName(doc, baseLevelName);
                    if (baseLevel == null)
                    {
                        var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Level '{baseLevelName}' not found. Available levels: {availableLevels}"));
                    }
                }
            }

            // Default to active view's level
            if (baseLevel == null)
            {
                baseLevel = ElementLookupHelper.InferLevelFromActiveView(app);
                if (baseLevel == null)
                {
                    var availableLevels = ElementLookupHelper.GetAvailableLevelNames(doc);
                    return Task.FromResult(ToolResult.Error(
                        $"No base_level specified and cannot infer from active view. Available levels: {availableLevels}"));
                }
            }

            // ── Find column type (fuzzy) ─────────────────────────────
            var columnTypeName = columnTypeElement.GetString();
            if (string.IsNullOrWhiteSpace(columnTypeName))
                return Task.FromResult(ToolResult.Error("column_type cannot be empty."));

            var (columnSymbol, isFuzzy, matchedName) = ElementLookupHelper.FindFamilySymbolInCategoryFuzzy(
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

                    if (topLevel.Elevation <= baseLevel.Elevation)
                    {
                        return Task.FromResult(ToolResult.Error(
                            $"Top level '{topLevelName}' must be above base level '{baseLevel.Name}'."));
                    }
                }
            }

            // Get optional offsets
            double baseOffset = 0.0;
            if (input.TryGetProperty("base_offset", out var baseOffsetElement))
                baseOffset = baseOffsetElement.GetDouble();

            double topOffset = 0.0;
            if (input.TryGetProperty("top_offset", out var topOffsetElement))
                topOffset = topOffsetElement.GetDouble();

            // Create column location point at base level elevation
            var location = new XYZ(x, y, baseLevel.Elevation);

            // Create the column
            FamilyInstance column;
            if (topLevel != null)
            {
                column = doc.Create.NewFamilyInstance(
                    location, columnSymbol, baseLevel, StructuralType.Column);

                var topLevelParam = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                topLevelParam?.Set(topLevel.Id);

                var baseOffsetParam = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                baseOffsetParam?.Set(baseOffset);

                var topOffsetParam = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                topOffsetParam?.Set(topOffset);
            }
            else
            {
                column = doc.Create.NewFamilyInstance(
                    location, columnSymbol, baseLevel, StructuralType.Column);

                if (baseOffset != 0)
                {
                    var baseOffsetParam = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                    baseOffsetParam?.Set(baseOffset);
                }
            }

            // Build result message
            var fullTypeName = $"{columnSymbol.Family?.Name}: {columnSymbol.Name}";
            var msg = topLevel != null
                ? $"Created column from {baseLevel.Name} to {topLevel.Name} at ({x:F2}, {y:F2})."
                : $"Created column on {baseLevel.Name} at ({x:F2}, {y:F2}).";

            if (resolvedFrom != null)
                msg += $" Resolved from {resolvedFrom}.";
            if (isFuzzy)
                msg += $" Type fuzzy-matched to '{matchedName}'.";

            var result = new PlaceColumnResult
            {
                ColumnId = column.Id.Value,
                ColumnType = fullTypeName,
                BaseLevel = baseLevel.Name,
                TopLevel = topLevel?.Name,
                BaseOffset = baseOffset,
                TopOffset = topLevel != null ? topOffset : null,
                Location = new[] { x, y },
                ResolvedFrom = resolvedFrom,
                FuzzyMatched = isFuzzy ? matchedName : null,
                Message = msg
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
        public string? ResolvedFrom { get; set; }
        public string? FuzzyMatched { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
