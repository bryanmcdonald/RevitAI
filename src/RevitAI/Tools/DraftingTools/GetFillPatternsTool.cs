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
/// Tool that lists available fill patterns in the document.
/// Supports optional filtering by target (drafting or model).
/// </summary>
public sealed class GetFillPatternsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetFillPatternsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "target": {
                        "type": "string",
                        "enum": ["drafting", "model"],
                        "description": "Optional filter: 'drafting' for drafting patterns, 'model' for model patterns. Omit to list all."
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

    public string Name => "get_fill_patterns";

    public string Description => "Lists available fill patterns in the document. " +
        "Use this before placing filled regions to discover pattern names. " +
        "Optionally filter by 'drafting' or 'model' target.";

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
            // Parse optional target filter
            FillPatternTarget? targetFilter = null;
            if (input.TryGetProperty("target", out var targetElement))
            {
                var targetStr = targetElement.GetString();
                if (string.Equals(targetStr, "drafting", StringComparison.OrdinalIgnoreCase))
                    targetFilter = FillPatternTarget.Drafting;
                else if (string.Equals(targetStr, "model", StringComparison.OrdinalIgnoreCase))
                    targetFilter = FillPatternTarget.Model;
            }

            var elements = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .AsEnumerable();

            // Filter by target before projecting to DTOs
            if (targetFilter.HasValue)
                elements = elements.Where(fpe => fpe.GetFillPattern()?.Target == targetFilter.Value);

            var patterns = elements
                .Select(fpe =>
                {
                    var fp = fpe.GetFillPattern();
                    if (fp == null) return null;
                    return new PatternData
                    {
                        Id = fpe.Id.Value,
                        Name = fpe.Name,
                        Target = fp.Target == FillPatternTarget.Drafting ? "drafting" : "model",
                        IsSolidFill = fp.IsSolidFill
                    };
                })
                .Where(p => p != null)
                .OrderBy(p => p!.Target)
                .ThenBy(p => p!.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new GetFillPatternsResult
            {
                Patterns = patterns!,
                Count = patterns.Count,
                Filter = targetFilter.HasValue ? (targetFilter == FillPatternTarget.Drafting ? "drafting" : "model") : null
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get fill patterns: {ex.Message}"));
        }
    }

    private sealed class PatternData
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public bool IsSolidFill { get; set; }
    }

    private sealed class GetFillPatternsResult
    {
        public List<PatternData> Patterns { get; set; } = new();
        public int Count { get; set; }
        public string? Filter { get; set; }
    }
}
