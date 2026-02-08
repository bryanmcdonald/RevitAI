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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that lists loaded detail component families and types.
/// Groups results by family with types listed within each.
/// </summary>
public sealed class GetDetailComponentsTool : IRevitTool
{
    private const int MaxFamilies = 50;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetDetailComponentsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "family_name": {
                        "type": "string",
                        "description": "Optional filter: only show types for this family name (case-insensitive partial match)."
                    }
                },
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

    public string Name => "get_detail_components";

    public string Description => "Lists loaded detail component families and types. " +
        "Use this before placing detail components to discover available family/type names. " +
        "Optionally filter by family name.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Parse optional family name filter
            string? familyFilter = null;
            if (input.TryGetProperty("family_name", out var familyElement))
                familyFilter = familyElement.GetString()?.Trim();

            var symbols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            // Group by family
            var familyGroups = symbols
                .GroupBy(s => s.Family?.Name ?? "(Unknown)")
                .Where(g => string.IsNullOrEmpty(familyFilter) ||
                    g.Key.Contains(familyFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxFamilies)
                .Select(g => new FamilyGroup
                {
                    FamilyName = g.Key,
                    Types = g.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(s => new TypeData
                        {
                            Id = s.Id.Value,
                            Name = s.Name
                        })
                        .ToList()
                })
                .ToList();

            var totalTypes = familyGroups.Sum(g => g.Types.Count);

            var result = new GetDetailComponentsResult
            {
                Families = familyGroups,
                FamilyCount = familyGroups.Count,
                TotalTypeCount = totalTypes,
                Filter = familyFilter,
                Truncated = familyGroups.Count >= MaxFamilies
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get detail components: {ex.Message}"));
        }
    }

    private sealed class TypeData
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class FamilyGroup
    {
        public string FamilyName { get; set; } = string.Empty;
        public List<TypeData> Types { get; set; } = new();
    }

    private sealed class GetDetailComponentsResult
    {
        public List<FamilyGroup> Families { get; set; } = new();
        public int FamilyCount { get; set; }
        public int TotalTypeCount { get; set; }
        public string? Filter { get; set; }
        public bool Truncated { get; set; }
    }
}
