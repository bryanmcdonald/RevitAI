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

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that creates an Assembly from selected elements.
/// </summary>
public sealed class CreateAssemblyTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateAssemblyTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to include in the assembly."
                    },
                    "name": {
                        "type": "string",
                        "description": "Optional name for the assembly type. If omitted, Revit assigns a default name."
                    },
                    "naming_category": {
                        "type": "string",
                        "description": "Optional category name to use for the assembly naming (e.g. 'Structural Columns', 'Structural Framing', 'Walls'). If omitted, the most common category among the elements is used."
                    }
                },
                "required": ["element_ids"],
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

    public string Name => "create_assembly";

    public string Description => "Creates an Assembly from the specified elements. Optionally provide a name and naming_category (the category used for the assembly type name). If naming_category is omitted, the most common category among the elements is used.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        var hasName = input.TryGetProperty("name", out var nameElem) && nameElem.ValueKind == JsonValueKind.String;
        var nameText = hasName ? $" named '{nameElem.GetString()}'" : "";
        return $"Would create an assembly{nameText} from {count} element(s).";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        try
        {
            // Parse element IDs
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
                requestedIds.Add(idElement.GetInt64());

            if (requestedIds.Count == 0)
                return Task.FromResult(ToolResult.Error("element_ids array cannot be empty."));

            // Validate element IDs and collect categories
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();
            var categoryCounts = new Dictionary<ElementId, (int count, string name)>();

            foreach (var id in requestedIds)
            {
                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);
                if (element == null)
                {
                    invalidIds.Add(id);
                    continue;
                }

                validIds.Add(elementId);

                if (element.Category != null)
                {
                    var catId = element.Category.Id;
                    if (categoryCounts.TryGetValue(catId, out var existing))
                        categoryCounts[catId] = (existing.count + 1, existing.name);
                    else
                        categoryCounts[catId] = (1, element.Category.Name);
                }
            }

            if (validIds.Count == 0)
                return Task.FromResult(ToolResult.Error($"None of the specified element IDs are valid: {string.Join(", ", invalidIds)}"));

            // Determine naming category
            ElementId namingCategoryId;
            string namingCategoryName;

            if (input.TryGetProperty("naming_category", out var namingCatElement) && namingCatElement.ValueKind == JsonValueKind.String)
            {
                var requestedCatName = namingCatElement.GetString()!.Trim();

                // Find a matching category from the elements
                var matchingCat = categoryCounts
                    .FirstOrDefault(kvp => string.Equals(kvp.Value.name, requestedCatName, StringComparison.OrdinalIgnoreCase));

                if (matchingCat.Key == null || matchingCat.Key == ElementId.InvalidElementId)
                {
                    var availableCats = string.Join(", ", categoryCounts.Values.Select(v => v.name).Distinct());
                    return Task.FromResult(ToolResult.Error($"Naming category '{requestedCatName}' not found among the element categories. Available: {availableCats}"));
                }

                namingCategoryId = matchingCat.Key;
                namingCategoryName = matchingCat.Value.name;
            }
            else
            {
                // Use most common category
                if (categoryCounts.Count == 0)
                    return Task.FromResult(ToolResult.Error("None of the elements have a category. Cannot determine naming category."));

                var mostCommon = categoryCounts.OrderByDescending(kvp => kvp.Value.count).First();
                namingCategoryId = mostCommon.Key;
                namingCategoryName = mostCommon.Value.name;
            }

            // Pre-validate
            if (!AssemblyInstance.AreElementsValidForAssembly(doc, validIds, namingCategoryId))
                return Task.FromResult(ToolResult.Error($"The specified elements are not valid for creating an assembly with naming category '{namingCategoryName}'. Elements may need to be on the same level or meet other Revit assembly requirements."));

            // Create the assembly
            var assembly = AssemblyInstance.Create(doc, validIds, namingCategoryId);

            // Regenerate to finalize the assembly
            doc.Regenerate();

            // Rename if name provided
            if (input.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                var requestedName = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(requestedName))
                {
                    var assemblyType = doc.GetElement(assembly.GetTypeId());
                    if (assemblyType != null)
                    {
                        assemblyType.Name = requestedName;
                    }
                }
            }

            var assemblyTypeName = doc.GetElement(assembly.GetTypeId())?.Name ?? "Unknown";

            var result = new CreateAssemblyResult
            {
                AssemblyId = assembly.Id.Value,
                AssemblyName = assemblyTypeName,
                NamingCategory = namingCategoryName,
                MemberCount = validIds.Count,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                Message = $"Created assembly '{assemblyTypeName}' (ID: {assembly.Id.Value}) with {validIds.Count} member(s), naming category: {namingCategoryName}."
            };

            return Task.FromResult(ToolResult.OkWithElements(JsonSerializer.Serialize(result, _jsonOptions), new[] { assembly.Id.Value }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class CreateAssemblyResult
    {
        public long AssemblyId { get; set; }
        public string AssemblyName { get; set; } = string.Empty;
        public string NamingCategory { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public List<long>? InvalidIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
