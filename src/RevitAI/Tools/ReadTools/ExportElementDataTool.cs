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

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Tools.ReadTools.Helpers;

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Tool that exports element data by category to CSV or JSON format.
/// </summary>
public sealed class ExportElementDataTool : IRevitTool
{
    private const int MaxElements = 500;
    private static readonly string[] DefaultParameters = { "Family", "Type", "Level", "Mark" };

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ExportElementDataTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "description": "The category of elements to export (e.g., 'Walls', 'Doors', 'Structural Columns')."
                    },
                    "format": {
                        "type": "string",
                        "enum": ["csv", "json"],
                        "description": "Output format: 'csv' or 'json'."
                    },
                    "parameters": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "Parameter names to include. Defaults to Family, Type, Level, Mark. Special names: 'Family', 'Type', 'Level'."
                    },
                    "level": {
                        "type": "string",
                        "description": "Optional level name to filter elements."
                    }
                },
                "required": ["category", "format"],
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

    public string Name => "export_element_data";

    public string Description => "Exports element data by category to CSV or JSON format. Data is returned inline in the result — you MUST display the raw CSV or JSON content to the user so they can copy it. Include 'Id' in parameters to get element IDs. Supports optional level filter and custom parameter selection. Maximum 500 elements.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

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

        // Get format
        if (!input.TryGetProperty("format", out var formatElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: format"));

        var format = formatElement.GetString()?.ToLowerInvariant();
        if (format != "csv" && format != "json")
            return Task.FromResult(ToolResult.Error("Parameter 'format' must be 'csv' or 'json'."));

        // Get optional parameters list
        var parameters = DefaultParameters;
        if (input.TryGetProperty("parameters", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Array)
        {
            var customParams = paramsElement.EnumerateArray()
                .Select(p => p.GetString())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Cast<string>()
                .ToArray();
            if (customParams.Length > 0)
                parameters = customParams;
        }

        // Get optional level filter
        Level? filterLevel = null;
        if (input.TryGetProperty("level", out var levelElement))
        {
            var levelName = levelElement.GetString();
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                filterLevel = FindLevelByName(doc, levelName);
                if (filterLevel == null)
                {
                    var availableLevels = GetAvailableLevelNames(doc);
                    return Task.FromResult(ToolResult.Error(
                        $"Level '{levelName}' not found. Available levels: {string.Join(", ", availableLevels)}"));
                }
            }
        }

        try
        {
            // Collect elements
            var collector = new FilteredElementCollector(doc)
                .OfCategory(builtInCategory)
                .WhereElementIsNotElementType();

            var allElements = collector.ToElements();

            // Apply level filter
            if (filterLevel != null)
            {
                allElements = allElements
                    .Where(e => IsElementOnLevel(e, filterLevel, doc))
                    .ToList();
            }

            var totalCount = allElements.Count;
            var truncated = totalCount > MaxElements;
            var elements = allElements.Take(MaxElements).ToList();

            // Extract data
            var rows = new List<Dictionary<string, string>>();
            foreach (var elem in elements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = new Dictionary<string, string>();
                foreach (var paramName in parameters)
                {
                    row[paramName] = GetParameterValue(elem, paramName, doc);
                }
                rows.Add(row);
            }

            // Format output
            var result = new ExportResult
            {
                Category = CategoryHelper.GetDisplayName(builtInCategory),
                Format = format,
                Columns = parameters.ToList(),
                TotalElements = totalCount,
                ExportedElements = elements.Count,
                Truncated = truncated,
                TruncatedMessage = truncated ? $"Exported {elements.Count} of {totalCount} elements. Maximum is {MaxElements}." : null
            };

            if (format == "csv")
            {
                result.Data = FormatAsCsv(parameters, rows);
            }
            else
            {
                // Store rows directly to avoid double-serialization (JSON-in-JSON)
                result.JsonData = rows;
            }

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to export element data: {ex.Message}"));
        }
    }

    private static string GetParameterValue(Element elem, string paramName, Document doc)
    {
        // Handle special parameter names
        switch (paramName)
        {
            case "Family":
                return GetFamilyName(elem, doc);
            case "Type":
                return GetTypeName(elem, doc);
            case "Level":
                return GetLevelName(elem, doc);
            case "Id":
                return elem.Id.Value.ToString();
        }

        // Try instance parameter first
        var param = elem.LookupParameter(paramName);
        if (param != null)
            return GetParameterValueString(param, doc);

        // Try type parameter
        var typeId = elem.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElem = doc.GetElement(typeId);
            if (typeElem != null)
            {
                var typeParam = typeElem.LookupParameter(paramName);
                if (typeParam != null)
                    return GetParameterValueString(typeParam, doc);
            }
        }

        // Try well-known BuiltInParameter for Mark
        if (paramName.Equals("Mark", StringComparison.OrdinalIgnoreCase))
        {
            var markParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (markParam != null)
                return GetParameterValueString(markParam, doc);
        }

        return string.Empty;
    }

    private static string GetFamilyName(Element elem, Document doc)
    {
        if (elem is FamilyInstance fi)
            return fi.Symbol?.Family?.Name ?? string.Empty;

        // Try ALL_MODEL_FAMILY_NAME on type element
        var typeId = elem.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElem = doc.GetElement(typeId);
            var familyParam = typeElem?.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
            if (familyParam != null)
                return familyParam.AsString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string GetTypeName(Element elem, Document doc)
    {
        var typeId = elem.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElem = doc.GetElement(typeId);
            return typeElem?.Name ?? string.Empty;
        }

        return elem.Name ?? string.Empty;
    }

    private static string GetLevelName(Element elem, Document doc)
    {
        // Direct LevelId
        if (elem.LevelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(elem.LevelId) as Level;
            if (level != null) return level.Name;
        }

        // Try common level parameters
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

    private static string GetParameterValueString(Parameter param, Document doc)
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

    private static string FormatAsCsv(string[] headers, List<Dictionary<string, string>> rows)
    {
        var sb = new StringBuilder();

        // Header row
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));

        // Data rows
        foreach (var row in rows)
        {
            var values = headers.Select(h => row.TryGetValue(h, out var v) ? v : string.Empty);
            sb.AppendLine(string.Join(",", values.Select(EscapeCsvField)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a CSV field per RFC 4180.
    /// Fields containing commas, double quotes, or newlines are wrapped in double quotes.
    /// Internal double quotes are escaped by doubling.
    /// </summary>
    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
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

    private sealed class ExportResult
    {
        public string Category { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
        public string? Data { get; set; }
        public List<Dictionary<string, string>>? JsonData { get; set; }
        public int TotalElements { get; set; }
        public int ExportedElements { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
    }
}
