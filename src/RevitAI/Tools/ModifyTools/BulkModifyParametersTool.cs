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
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Tools.ReadTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that modifies a parameter value across multiple elements of a category.
/// Supports sequential numbering via {index} and {index:N} placeholders.
/// </summary>
public sealed partial class BulkModifyParametersTool : IRevitTool
{
    private const int MaxElements = 1000;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static BulkModifyParametersTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "description": "The category of elements to modify (e.g., 'Structural Columns', 'Walls', 'Doors')."
                    },
                    "filter": {
                        "type": "object",
                        "properties": {
                            "parameter": {
                                "type": "string",
                                "description": "Parameter name to filter by."
                            },
                            "value": {
                                "type": "string",
                                "description": "Value to match (case-insensitive display value comparison)."
                            }
                        },
                        "required": ["parameter", "value"],
                        "description": "Optional filter to narrow elements by parameter value."
                    },
                    "level": {
                        "type": "string",
                        "description": "Optional level name to filter elements."
                    },
                    "modify": {
                        "type": "object",
                        "properties": {
                            "parameter": {
                                "type": "string",
                                "description": "Parameter name to set."
                            },
                            "value": {
                                "type": "string",
                                "description": "Value to set. Use {index} for sequential numbering (1,2,3...) or {index:N} for zero-padded numbering ({index:3} -> 001,002,003)."
                            }
                        },
                        "required": ["parameter", "value"],
                        "description": "The parameter and value to set on matched elements."
                    }
                },
                "required": ["category", "modify"],
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

    public string Name => "bulk_modify_parameters";

    public string Description => "Modifies a parameter value across multiple elements in a category. Supports level and parameter value filtering. Use {index} for sequential numbering or {index:3} for zero-padded (001, 002...). Maximum 1000 elements.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var category = input.TryGetProperty("category", out var catElem) ? catElem.GetString() ?? "unknown" : "unknown";
        var modifyParam = string.Empty;
        var modifyValue = string.Empty;
        if (input.TryGetProperty("modify", out var modifyElem))
        {
            modifyParam = modifyElem.TryGetProperty("parameter", out var p) ? p.GetString() ?? "unknown" : "unknown";
            modifyValue = modifyElem.TryGetProperty("value", out var v) ? v.GetString() ?? "unknown" : "unknown";
        }

        var filters = new List<string>();
        if (input.TryGetProperty("level", out var levelElem))
        {
            var level = levelElem.GetString();
            if (!string.IsNullOrWhiteSpace(level))
                filters.Add($"on level '{level}'");
        }
        if (input.TryGetProperty("filter", out var filterElem))
        {
            var filterParam = filterElem.TryGetProperty("parameter", out var fp) ? fp.GetString() : null;
            var filterVal = filterElem.TryGetProperty("value", out var fv) ? fv.GetString() : null;
            if (!string.IsNullOrWhiteSpace(filterParam))
                filters.Add($"where {filterParam} = '{filterVal}'");
        }

        var filterStr = filters.Count > 0 ? $" ({string.Join(", ", filters)})" : "";
        return $"Would set '{modifyParam}' to '{modifyValue}' on {category}{filterStr}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get category
        if (!input.TryGetProperty("category", out var categoryElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: category"));

        var categoryName = categoryElement.GetString();
        if (string.IsNullOrWhiteSpace(categoryName))
            return Task.FromResult(ToolResult.Error("Parameter 'category' cannot be empty."));

        if (!CategoryHelper.TryGetCategory(categoryName, out var builtInCategory))
            return Task.FromResult(ToolResult.Error(CategoryHelper.GetInvalidCategoryError(categoryName)));

        // Get modify parameters
        if (!input.TryGetProperty("modify", out var modifyElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: modify"));

        if (!modifyElement.TryGetProperty("parameter", out var modifyParamElement))
            return Task.FromResult(ToolResult.Error("Missing required field: modify.parameter"));

        if (!modifyElement.TryGetProperty("value", out var modifyValueElement))
            return Task.FromResult(ToolResult.Error("Missing required field: modify.value"));

        var modifyParamName = modifyParamElement.GetString();
        var modifyValueTemplate = modifyValueElement.GetString();

        if (string.IsNullOrWhiteSpace(modifyParamName))
            return Task.FromResult(ToolResult.Error("Field 'modify.parameter' cannot be empty."));
        if (modifyValueTemplate == null)
            return Task.FromResult(ToolResult.Error("Field 'modify.value' cannot be null."));

        try
        {
            // Collect elements by category
            var collector = new FilteredElementCollector(doc)
                .OfCategory(builtInCategory)
                .WhereElementIsNotElementType();

            var elements = collector.ToElements().ToList();

            // Apply level filter
            if (input.TryGetProperty("level", out var levelElement))
            {
                var levelName = levelElement.GetString();
                if (!string.IsNullOrWhiteSpace(levelName))
                {
                    var filterLevel = FindLevelByName(doc, levelName);
                    if (filterLevel == null)
                    {
                        var availableLevels = GetAvailableLevelNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Level '{levelName}' not found. Available levels: {string.Join(", ", availableLevels)}"));
                    }
                    elements = elements.Where(e => IsElementOnLevel(e, filterLevel, doc)).ToList();
                }
            }

            // Apply parameter value filter
            if (input.TryGetProperty("filter", out var filterElement))
            {
                var filterParamName = filterElement.TryGetProperty("parameter", out var fp)
                    ? fp.GetString() : null;
                var filterValue = filterElement.TryGetProperty("value", out var fv)
                    ? fv.GetString() : null;

                if (!string.IsNullOrWhiteSpace(filterParamName) && filterValue != null)
                {
                    elements = elements
                        .Where(e => MatchesFilter(e, filterParamName, filterValue, doc))
                        .ToList();
                }
            }

            // Check element count cap
            if (elements.Count > MaxElements)
            {
                return Task.FromResult(ToolResult.Error(
                    $"Too many elements ({elements.Count}). Maximum is {MaxElements}. " +
                    "Use level or filter parameters to narrow the selection."));
            }

            if (elements.Count == 0)
            {
                return Task.FromResult(ToolResult.Error(
                    $"No elements found matching the specified criteria in category '{CategoryHelper.GetDisplayName(builtInCategory)}'."));
            }

            // Validate the modify parameter exists on the first element
            var firstElement = elements[0];
            var testParam = firstElement.LookupParameter(modifyParamName);
            if (testParam == null)
            {
                var availableParams = GetWritableParameterNames(firstElement);
                return Task.FromResult(ToolResult.Error(
                    $"Parameter '{modifyParamName}' not found on elements. " +
                    $"Available writable parameters: {string.Join(", ", availableParams.Take(20))}" +
                    (availableParams.Count > 20 ? ", ..." : "")));
            }

            if (testParam.IsReadOnly)
            {
                return Task.FromResult(ToolResult.Error(
                    $"Parameter '{modifyParamName}' is read-only and cannot be modified."));
            }

            // Modify elements
            int modified = 0;
            int skipped = 0;
            int failed = 0;
            var modifiedIds = new List<long>();
            var errors = new List<string>();

            for (int i = 0; i < elements.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elem = elements[i];
                var param = elem.LookupParameter(modifyParamName);

                if (param == null || param.IsReadOnly)
                {
                    skipped++;
                    continue;
                }

                // Resolve {index} placeholders
                var resolvedValue = ResolveIndexPlaceholders(modifyValueTemplate, i + 1);

                var success = SetParameterStringValue(param, resolvedValue);
                if (success)
                {
                    modified++;
                    modifiedIds.Add(elem.Id.Value);
                }
                else
                {
                    failed++;
                    if (errors.Count < 5)
                        errors.Add($"Element {elem.Id.Value}: failed to set value '{resolvedValue}'");
                }
            }

            var result = new BulkModifyResult
            {
                Category = CategoryHelper.GetDisplayName(builtInCategory),
                Parameter = modifyParamName,
                ValueTemplate = modifyValueTemplate,
                Matched = elements.Count,
                Modified = modified,
                Skipped = skipped,
                Failed = failed,
                Errors = errors.Count > 0 ? errors : null,
                Message = $"Modified '{modifyParamName}' on {modified} of {elements.Count} {CategoryHelper.GetDisplayName(builtInCategory)}."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions),
                modifiedIds));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to bulk modify parameters: {ex.Message}"));
        }
    }

    /// <summary>
    /// Resolves {index} and {index:N} placeholders in the value template.
    /// {index} -> sequential number (1, 2, 3...)
    /// {index:N} -> zero-padded number ({index:3} -> 001, 002, 003)
    /// </summary>
    private static string ResolveIndexPlaceholders(string template, int index)
    {
        return IndexPlaceholderRegex().Replace(template, match =>
        {
            var paddingGroup = match.Groups[1];
            if (paddingGroup.Success && int.TryParse(paddingGroup.Value, out var padding))
            {
                return index.ToString().PadLeft(padding, '0');
            }
            return index.ToString();
        });
    }

    [GeneratedRegex(@"\{index(?::(\d+))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex IndexPlaceholderRegex();

    private static bool MatchesFilter(Element elem, string paramName, string value, Document doc)
    {
        // Special handling for "Level" filter
        if (paramName.Equals("Level", StringComparison.OrdinalIgnoreCase))
        {
            var levelName = GetLevelName(elem, doc);
            return levelName.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        // Look up the parameter and compare display value
        var param = elem.LookupParameter(paramName);
        if (param == null) return false;

        var displayValue = GetParameterDisplayValue(param, doc);
        return displayValue.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetParameterDisplayValue(Parameter param, Document doc)
    {
        var valueString = param.AsValueString();
        if (!string.IsNullOrEmpty(valueString))
            return valueString;

        return param.StorageType switch
        {
            StorageType.String => param.AsString() ?? string.Empty,
            StorageType.Integer => param.AsInteger().ToString(),
            StorageType.Double => param.AsDouble().ToString("F4"),
            StorageType.ElementId => GetElementIdValueString(param.AsElementId(), doc),
            _ => string.Empty
        };
    }

    private static string GetElementIdValueString(ElementId elemId, Document doc)
    {
        if (elemId == ElementId.InvalidElementId)
            return string.Empty;
        var elem = doc.GetElement(elemId);
        return elem?.Name ?? $"Element {elemId.Value}";
    }

    private static string GetLevelName(Element elem, Document doc)
    {
        if (elem.LevelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(elem.LevelId) as Level;
            if (level != null) return level.Name;
        }

        BuiltInParameter[] levelParams =
        {
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
            BuiltInParameter.LEVEL_PARAM
        };

        foreach (var bip in levelParams)
        {
            var param = elem.get_Parameter(bip);
            if (param != null && param.HasValue)
            {
                var level = doc.GetElement(param.AsElementId()) as Level;
                if (level != null) return level.Name;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Sets a string value on a parameter, converting as needed for the storage type.
    /// </summary>
    private static bool SetParameterStringValue(Parameter param, string value)
    {
        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(value);
                    return true;

                case StorageType.Integer:
                    if (int.TryParse(value, out var intVal))
                    {
                        param.Set(intVal);
                        return true;
                    }
                    return false;

                case StorageType.Double:
                    if (double.TryParse(value, out var doubleVal))
                    {
                        param.Set(doubleVal);
                        return true;
                    }
                    return false;

                case StorageType.ElementId:
                    if (long.TryParse(value, out var idVal))
                    {
                        param.Set(new ElementId(idVal));
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static List<string> GetWritableParameterNames(Element element)
    {
        var names = new List<string>();
        foreach (Parameter param in element.Parameters)
        {
            if (!param.IsReadOnly && param.Definition != null)
            {
                names.Add(param.Definition.Name);
            }
        }
        return names.Distinct().OrderBy(n => n).ToList();
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
    /// </summary>
    private static bool IsElementOnLevel(Element elem, Level targetLevel, Document doc)
    {
        var targetLevelId = targetLevel.Id;

        if (elem.LevelId == targetLevelId)
            return true;

        var refLevelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
        if (refLevelParam != null && refLevelParam.HasValue && refLevelParam.AsElementId() == targetLevelId)
            return true;

        var scheduleLevelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
        if (scheduleLevelParam != null && scheduleLevelParam.HasValue && scheduleLevelParam.AsElementId() == targetLevelId)
            return true;

        var familyLevelParam = elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (familyLevelParam != null && familyLevelParam.HasValue && familyLevelParam.AsElementId() == targetLevelId)
            return true;

        var baseLevelParam = elem.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
        if (baseLevelParam != null && baseLevelParam.HasValue && baseLevelParam.AsElementId() == targetLevelId)
            return true;

        var levelParam = elem.get_Parameter(BuiltInParameter.LEVEL_PARAM);
        if (levelParam != null && levelParam.HasValue && levelParam.AsElementId() == targetLevelId)
            return true;

        return false;
    }

    private sealed class BulkModifyResult
    {
        public string Category { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;
        public string ValueTemplate { get; set; } = string.Empty;
        public int Matched { get; set; }
        public int Modified { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string>? Errors { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
