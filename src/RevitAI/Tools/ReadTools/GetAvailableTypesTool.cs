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
/// Tool that returns available family types for a given category.
/// </summary>
public sealed class GetAvailableTypesTool : IRevitTool
{
    private const int MaxTypes = 100;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetAvailableTypesTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "description": "The category to get types for (e.g., 'Walls', 'Doors', 'Windows', 'Floors', 'Columns')"
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

    public string Name => "get_available_types";

    public string Description => "Returns available family types for a given category (e.g., 'Walls', 'Doors', 'Windows'). Use this to see what types can be placed or to help users choose a type.";

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

        try
        {
            var types = new FilteredElementCollector(doc)
                .OfCategory(builtInCategory)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .OrderBy(t => GetFullTypeName(t))
                .Take(MaxTypes)
                .Select(t => new TypeData
                {
                    Id = t.Id.Value,
                    Family = GetFamilyName(t),
                    Type = t.Name,
                    FullName = GetFullTypeName(t)
                })
                .ToList();

            var result = new GetAvailableTypesResult
            {
                Category = CategoryHelper.GetDisplayName(builtInCategory),
                Types = types,
                Count = types.Count,
                Truncated = types.Count >= MaxTypes,
                TruncatedMessage = types.Count >= MaxTypes ? $"Showing first {MaxTypes} types. More may be available." : null
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get available types: {ex.Message}"));
        }
    }

    private static string? GetFamilyName(ElementType elemType)
    {
        var familyParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
        return familyParam?.AsString();
    }

    private static string GetFullTypeName(ElementType elemType)
    {
        var familyName = GetFamilyName(elemType);
        return string.IsNullOrEmpty(familyName)
            ? elemType.Name
            : $"{familyName}: {elemType.Name}";
    }

    private sealed class TypeData
    {
        public long Id { get; set; }
        public string? Family { get; set; }
        public string Type { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    private sealed class GetAvailableTypesResult
    {
        public string Category { get; set; } = string.Empty;
        public List<TypeData> Types { get; set; } = new();
        public int Count { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
    }
}
