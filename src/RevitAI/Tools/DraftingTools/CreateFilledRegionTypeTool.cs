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
/// Tool that creates a new FilledRegionType by duplicating an existing type
/// and setting its foreground pattern and color.
/// </summary>
public sealed class CreateFilledRegionTypeTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateFilledRegionTypeTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the new filled region type."
                    },
                    "fill_pattern_name": {
                        "type": "string",
                        "description": "Name of the fill pattern to use (e.g., 'Diagonal Crosshatch', 'Solid fill'). Use get_fill_patterns to discover available patterns."
                    },
                    "color": {
                        "type": "array",
                        "items": { "type": "integer", "minimum": 0, "maximum": 255 },
                        "minItems": 3,
                        "maxItems": 3,
                        "description": "Foreground color as [r, g, b] values (0-255)."
                    },
                    "base_type_name": {
                        "type": "string",
                        "description": "Name of the existing FilledRegionType to duplicate from. Optional - uses first available non-masking type if not specified."
                    }
                },
                "required": ["name", "fill_pattern_name", "color"],
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

    public string Name => "create_filled_region_type";

    public string Description => "Creates a new filled region type with a specified fill pattern and color. Duplicates from an existing type. Use get_fill_patterns to discover available pattern names.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var name = input.TryGetProperty("name", out var n) ? n.GetString() : "unnamed";
        var pattern = input.TryGetProperty("fill_pattern_name", out var p) ? p.GetString() : "unknown";
        return $"Would create filled region type '{name}' with pattern '{pattern}'.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Parse required parameters
            if (!input.TryGetProperty("name", out var nameElement) || string.IsNullOrWhiteSpace(nameElement.GetString()))
                return Task.FromResult(ToolResult.Error("Missing required parameter: name"));
            var typeName = nameElement.GetString()!.Trim();

            if (!input.TryGetProperty("fill_pattern_name", out var patternElement) || string.IsNullOrWhiteSpace(patternElement.GetString()))
                return Task.FromResult(ToolResult.Error("Missing required parameter: fill_pattern_name"));
            var patternName = patternElement.GetString()!.Trim();

            if (!input.TryGetProperty("color", out var colorElement) || colorElement.ValueKind != JsonValueKind.Array)
                return Task.FromResult(ToolResult.Error("Missing required parameter: color (must be [r, g, b])"));

            var colorValues = colorElement.EnumerateArray().ToList();
            if (colorValues.Count != 3)
                return Task.FromResult(ToolResult.Error("Color must be exactly 3 values [r, g, b]."));

            var r = (byte)Math.Clamp((int)colorValues[0].GetDouble(), 0, 255);
            var g = (byte)Math.Clamp((int)colorValues[1].GetDouble(), 0, 255);
            var b = (byte)Math.Clamp((int)colorValues[2].GetDouble(), 0, 255);

            // Check for duplicate name
            var existingType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(frt => string.Equals(frt.Name, typeName, StringComparison.OrdinalIgnoreCase));

            if (existingType != null)
                return Task.FromResult(ToolResult.Error($"A filled region type named '{typeName}' already exists (ID: {existingType.Id.Value})."));

            // Find fill pattern element by name
            var fillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => string.Equals(fp.Name, patternName, StringComparison.OrdinalIgnoreCase));

            if (fillPattern == null)
            {
                var available = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .OrderBy(fp => fp.Name)
                    .Take(30)
                    .Select(fp => fp.Name)
                    .ToList();

                return Task.FromResult(ToolResult.Error(
                    $"Fill pattern '{patternName}' not found. Available patterns: {string.Join(", ", available)}"));
            }

            // Find base type to duplicate
            FilledRegionType? baseType = null;
            if (input.TryGetProperty("base_type_name", out var baseNameElement) && !string.IsNullOrWhiteSpace(baseNameElement.GetString()))
            {
                var baseName = baseNameElement.GetString()!.Trim();
                baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType))
                    .Cast<FilledRegionType>()
                    .FirstOrDefault(frt => string.Equals(frt.Name, baseName, StringComparison.OrdinalIgnoreCase));

                if (baseType == null)
                {
                    var available = ModifyTools.Helpers.ElementLookupHelper.GetAvailableFilledRegionTypeNames(doc);
                    return Task.FromResult(ToolResult.Error(
                        $"Base filled region type '{baseName}' not found. Available types: {available}"));
                }
            }
            else
            {
                baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType))
                    .Cast<FilledRegionType>()
                    .FirstOrDefault(frt => !frt.IsMasking);
            }

            if (baseType == null)
                return Task.FromResult(ToolResult.Error("No non-masking filled region types found in the document to duplicate from."));

            // Duplicate and configure
            var newType = baseType.Duplicate(typeName) as FilledRegionType;
            if (newType == null)
                return Task.FromResult(ToolResult.Error("Failed to duplicate the base filled region type."));

            newType.ForegroundPatternId = fillPattern.Id;
            newType.ForegroundPatternColor = new Color(r, g, b);

            var result = new CreateFilledRegionTypeResult
            {
                TypeId = newType.Id.Value,
                TypeName = typeName,
                FillPatternName = fillPattern.Name,
                Color = new[] { (int)r, (int)g, (int)b },
                BasedOn = baseType.Name,
                Message = $"Created filled region type '{typeName}' with pattern '{fillPattern.Name}' and color [{r},{g},{b}]."
            };

            // Type elements are not selectable in the viewport, so Ok() is used instead of OkWithElements()
            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create filled region type: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class CreateFilledRegionTypeResult
    {
        public long TypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public string FillPatternName { get; set; } = string.Empty;
        public int[] Color { get; set; } = Array.Empty<int>();
        public string BasedOn { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
