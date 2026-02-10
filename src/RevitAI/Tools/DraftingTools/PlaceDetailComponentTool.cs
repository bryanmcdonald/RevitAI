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
/// Tool that places a detail component family instance in a view.
/// Uses fuzzy matching for family/type name resolution.
/// </summary>
public sealed class PlaceDetailComponentTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceDetailComponentTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the component in. Optional - uses active view if not specified."
                    },
                    "family_name": {
                        "type": "string",
                        "description": "Family name of the detail component. Can be just the family name, or 'Family: Type' format."
                    },
                    "type_name": {
                        "type": "string",
                        "description": "Type name within the family. Optional - if provided, combined with family_name as 'family_name: type_name'."
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
                "required": ["family_name", "location"],
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

    public string Name => "place_detail_component";

    public string Description => "Places a detail component (2D family instance) in a view. Supports fuzzy name matching. Use get_detail_components to discover available families. Coordinates are in feet.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var familyName = input.TryGetProperty("family_name", out var f) ? f.GetString() : "unknown";
        var typeName = input.TryGetProperty("type_name", out var t) ? t.GetString() : null;
        var rotation = input.TryGetProperty("rotation", out var r) ? r.GetDouble() : 0;

        var displayName = typeName != null ? $"{familyName}: {typeName}" : familyName;
        if (rotation != 0)
            return $"Would place detail component '{displayName}' rotated {rotation:F1} degrees.";
        return $"Would place detail component '{displayName}'.";
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

            // Parse family name
            if (!input.TryGetProperty("family_name", out var familyNameElement) || string.IsNullOrWhiteSpace(familyNameElement.GetString()))
                return Task.FromResult(ToolResult.Error("Missing required parameter: family_name"));
            var familyName = familyNameElement.GetString()!.Trim();

            // Build full name: "Family: Type" if type_name is provided
            var fullName = familyName;
            if (input.TryGetProperty("type_name", out var typeNameElement) && !string.IsNullOrWhiteSpace(typeNameElement.GetString()))
            {
                fullName = $"{familyName}: {typeNameElement.GetString()!.Trim()}";
            }

            // Parse location
            var (location, locationError) = DraftingHelper.ParsePoint(input, "location");
            if (locationError != null) return Task.FromResult(locationError);

            // Find family symbol using fuzzy matching
            var (symbol, isFuzzy, matchedName) = ElementLookupHelper.FindFamilySymbolInCategoryFuzzy(
                doc, BuiltInCategory.OST_DetailComponents, fullName);

            if (symbol == null)
            {
                var available = ElementLookupHelper.GetAvailableTypeNames(doc, BuiltInCategory.OST_DetailComponents);
                return Task.FromResult(ToolResult.Error(
                    $"Detail component '{fullName}' not found. Available detail components: {available}"));
            }

            // Activate if needed
            if (!symbol.IsActive)
                symbol.Activate();

            // Place the instance
            var instance = doc.Create.NewFamilyInstance(location!, symbol, view!);

            // Apply rotation if specified
            var rotation = 0.0;
            if (input.TryGetProperty("rotation", out var rotationElement))
            {
                rotation = rotationElement.GetDouble();
                if (Math.Abs(rotation) > 0.001)
                {
                    var radians = DraftingHelper.DegreesToRadians(rotation);
                    var axis = Line.CreateBound(location!, location! + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, radians);
                }
            }

            var result = new PlaceDetailComponentResult
            {
                ElementIds = new[] { instance.Id.Value },
                ViewId = view!.Id.Value,
                ViewName = view.Name,
                FamilyName = symbol.Family?.Name ?? "",
                TypeName = symbol.Name,
                MatchedName = matchedName,
                IsFuzzyMatch = isFuzzy,
                Location = new[] { location!.X, location.Y },
                Rotation = Math.Abs(rotation) > 0.001 ? Math.Round(rotation, 2) : null,
                Message = isFuzzy
                    ? $"Placed detail component '{matchedName}' (fuzzy match for '{fullName}') in '{view.Name}'."
                    : $"Placed detail component '{matchedName}' in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { instance.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to place detail component: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceDetailComponentResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string? MatchedName { get; set; }
        public bool IsFuzzyMatch { get; set; }
        public double[] Location { get; set; } = Array.Empty<double>();
        public double? Rotation { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
