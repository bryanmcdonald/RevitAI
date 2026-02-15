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
using RevitAI.Tools.DraftingTools.Helpers;
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that places up to 100 detail components from the same family in a single call.
/// Supports per-component type names with cached lookups and partial-success tracking.
/// </summary>
public sealed class BatchPlaceDetailComponentsTool : IRevitTool
{
    private const int MaxComponents = 100;
    private const int MaxErrors = 5;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static BatchPlaceDetailComponentsTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place components in. Optional - uses active view if not specified."
                    },
                    "family_name": {
                        "type": "string",
                        "description": "Family name of the detail component (shared across all items)."
                    },
                    "components": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "type_name": {
                                    "type": "string",
                                    "description": "Type name within the family for this component."
                                },
                                "location": {
                                    "type": "array",
                                    "items": { "type": "number" },
                                    "minItems": 2,
                                    "maxItems": 3,
                                    "description": "Placement location [x, y] or [x, y, z] in feet."
                                },
                                "rotation": {
                                    "type": "number",
                                    "description": "Rotation angle in degrees (counter-clockwise). Optional - defaults to 0."
                                }
                            },
                            "required": ["type_name", "location"]
                        },
                        "minItems": 1,
                        "maxItems": 100,
                        "description": "Array of components to place. Each has type_name, location, and optional rotation."
                    }
                },
                "required": ["family_name", "components"],
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

    public string Name => "batch_place_detail_components";

    public string Description => "Places up to 100 detail components from the same family in a single call. Each component specifies a type_name, location, and optional rotation. The family is resolved once upfront. Much more efficient than calling place_detail_component repeatedly. Coordinates are in feet.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var familyName = input.TryGetProperty("family_name", out var f) ? f.GetString() : "unknown";
        var count = input.TryGetProperty("components", out var c) ? c.GetArrayLength() : 0;
        return $"Would place {count} '{familyName}' detail component(s).";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Resolve view
            var (view, viewError) = DraftingHelper.ResolveDetailView(doc, input);
            if (viewError != null) return Task.FromResult(viewError);

            // Validate family_name
            if (!input.TryGetProperty("family_name", out var familyNameElement) ||
                string.IsNullOrWhiteSpace(familyNameElement.GetString()))
                return Task.FromResult(ToolResult.Error("Missing required parameter: family_name"));
            var familyName = familyNameElement.GetString()!.Trim();

            // Validate components array
            if (!input.TryGetProperty("components", out var componentsArray) ||
                componentsArray.ValueKind != JsonValueKind.Array)
                return Task.FromResult(ToolResult.Error("Missing required parameter: components"));

            var componentCount = componentsArray.GetArrayLength();
            if (componentCount == 0)
                return Task.FromResult(ToolResult.Error("Parameter 'components' must contain at least 1 item."));
            if (componentCount > MaxComponents)
                return Task.FromResult(ToolResult.Error(
                    $"Too many components ({componentCount}). Maximum is {MaxComponents} per call."));

            // Verify the family exists by trying to find any symbol with this family name
            var (testSymbol, _, _) = ElementLookupHelper.FindFamilySymbolInCategoryFuzzy(
                doc, BuiltInCategory.OST_DetailComponents, familyName);
            if (testSymbol == null)
            {
                var available = ElementLookupHelper.GetAvailableTypeNames(doc, BuiltInCategory.OST_DetailComponents);
                return Task.FromResult(ToolResult.Error(
                    $"Detail component family '{familyName}' not found. Available detail components: {available}"));
            }

            // Pre-cache type symbols: resolve each unique type_name once
            var symbolCache = new Dictionary<string, FamilySymbol?>(StringComparer.OrdinalIgnoreCase);
            foreach (var compItem in componentsArray.EnumerateArray())
            {
                if (compItem.TryGetProperty("type_name", out var typeElem))
                {
                    var typeName = typeElem.GetString();
                    if (!string.IsNullOrWhiteSpace(typeName) && !symbolCache.ContainsKey(typeName))
                    {
                        var fullName = $"{familyName}: {typeName}";
                        var (symbol, _, _) = ElementLookupHelper.FindFamilySymbolInCategoryFuzzy(
                            doc, BuiltInCategory.OST_DetailComponents, fullName);
                        if (symbol != null && !symbol.IsActive)
                            symbol.Activate();
                        symbolCache[typeName] = symbol;
                    }
                }
            }

            // Place components
            var succeeded = 0;
            var failed = 0;
            var elementIds = new List<long>();
            var errors = new List<string>();

            var index = 0;
            foreach (var compItem in componentsArray.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Get type_name
                    if (!compItem.TryGetProperty("type_name", out var typeElem) ||
                        string.IsNullOrWhiteSpace(typeElem.GetString()))
                    {
                        failed++;
                        if (errors.Count < MaxErrors)
                            errors.Add($"Component {index}: missing 'type_name'.");
                        index++;
                        continue;
                    }
                    var typeName = typeElem.GetString()!.Trim();

                    // Look up cached symbol
                    if (!symbolCache.TryGetValue(typeName, out var symbol) || symbol == null)
                    {
                        failed++;
                        if (errors.Count < MaxErrors)
                            errors.Add($"Component {index}: type '{familyName}: {typeName}' not found.");
                        index++;
                        continue;
                    }

                    // Parse location
                    var (location, locErr) = ParsePointFromItem(compItem, "location", index);
                    if (locErr != null)
                    {
                        failed++;
                        if (errors.Count < MaxErrors) errors.Add(locErr);
                        index++;
                        continue;
                    }

                    // Place the instance
                    var instance = doc.Create.NewFamilyInstance(location!, symbol, view!);

                    // Apply rotation if specified
                    if (compItem.TryGetProperty("rotation", out var rotationElem))
                    {
                        var rotation = rotationElem.GetDouble();
                        if (Math.Abs(rotation) > 0.001)
                        {
                            var radians = DraftingHelper.DegreesToRadians(rotation);
                            var axis = Line.CreateBound(location!, location! + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, instance.Id, axis, radians);
                        }
                    }

                    elementIds.Add(instance.Id.Value);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (errors.Count < MaxErrors)
                        errors.Add($"Component {index}: {ex.Message}");
                }

                index++;
            }

            // Build result
            if (succeeded == 0)
            {
                return Task.FromResult(ToolResult.Error(
                    $"All {componentCount} components failed. Errors: {string.Join("; ", errors)}"));
            }

            var result = new BatchResult
            {
                Succeeded = succeeded,
                Failed = failed,
                Total = componentCount,
                Errors = errors.Count > 0 ? errors : null,
                ElementIds = elementIds.ToArray(),
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                FamilyName = familyName,
                Message = failed == 0
                    ? $"Placed {succeeded} '{familyName}' detail component(s) in '{view.Name}'."
                    : $"Placed {succeeded} of {componentCount} '{familyName}' detail component(s) in '{view.Name}'. {failed} failed."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), elementIds));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to place detail components: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    /// <summary>
    /// Parses a point from a component item's property.
    /// Returns the point or an error message string.
    /// </summary>
    private static (XYZ? Point, string? Error) ParsePointFromItem(JsonElement item, string paramName, int compIndex)
    {
        if (!item.TryGetProperty(paramName, out var element))
            return (null, $"Component {compIndex}: missing '{paramName}'.");

        if (element.ValueKind != JsonValueKind.Array)
            return (null, $"Component {compIndex}: '{paramName}' must be an array [x, y].");

        var length = element.GetArrayLength();
        if (length < 2 || length > 3)
            return (null, $"Component {compIndex}: '{paramName}' must have 2 or 3 numbers.");

        var x = element[0].GetDouble();
        var y = element[1].GetDouble();
        var z = length == 3 ? element[2].GetDouble() : 0;

        return (new XYZ(x, y, z), null);
    }

    private sealed class BatchResult
    {
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public List<string>? Errors { get; set; }
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
