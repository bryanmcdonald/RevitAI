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
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that places a tag on an element in the active view.
/// </summary>
public sealed class PlaceTagTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceTagTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_id": {
                        "type": "integer",
                        "description": "The ID of the element to tag."
                    },
                    "tag_type": {
                        "type": "string",
                        "description": "Tag type name (e.g., 'Wall Tag'). Optional - uses default tag for the element's category."
                    },
                    "location": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Tag location [x, y] in feet. Optional - defaults to element bounding box center."
                    },
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the tag in. Optional - uses active view if not specified."
                    },
                    "has_leader": {
                        "type": "boolean",
                        "description": "Whether the tag has a leader line. Default is false."
                    },
                    "orientation": {
                        "type": "string",
                        "enum": ["horizontal", "vertical"],
                        "description": "Tag orientation. Default is horizontal."
                    }
                },
                "required": ["element_id"],
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

    public string Name => "place_tag";

    public string Description => "Places a tag on an element in a view. Automatically determines the appropriate tag type based on the element's category (walls, doors, windows, etc.). Use get_selected_elements or get_elements_by_category to find element IDs.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var elementId = input.TryGetProperty("element_id", out var idElem) ? idElem.GetInt64().ToString() : "unknown";
        var tagType = input.TryGetProperty("tag_type", out var typeElem) ? typeElem.GetString() : null;

        return tagType != null
            ? $"Would place a '{tagType}' tag on element {elementId}."
            : $"Would place a tag on element {elementId}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_id", out var elementIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_id"));

        try
        {
            // Resolve element
            var elementId = new ElementId(elementIdElement.GetInt64());
            var element = doc.GetElement(elementId);
            if (element == null)
                return Task.FromResult(ToolResult.Error($"Element with ID {elementIdElement.GetInt64()} not found."));

            // Resolve view
            View? view = null;
            if (input.TryGetProperty("view_id", out var viewIdElement))
            {
                var viewId = new ElementId(viewIdElement.GetInt64());
                view = doc.GetElement(viewId) as View;
                if (view == null)
                    return Task.FromResult(ToolResult.Error($"View with ID {viewIdElement.GetInt64()} not found."));
            }
            else
            {
                view = doc.ActiveView;
            }

            if (view == null)
                return Task.FromResult(ToolResult.Error("No active view available."));

            // Tags cannot be placed in 3D views
            if (view.ViewType == ViewType.ThreeD)
                return Task.FromResult(ToolResult.Error("Tags cannot be placed in 3D views. Switch to a plan, section, elevation, or drafting view."));

            // Find tag type
            string? tagTypeName = null;
            if (input.TryGetProperty("tag_type", out var tagTypeElement))
                tagTypeName = tagTypeElement.GetString();

            var tagType = ElementLookupHelper.FindTagTypeForElement(doc, element, tagTypeName);
            if (tagType == null)
            {
                var categoryName = element.Category?.Name ?? "unknown";
                if (!string.IsNullOrWhiteSpace(tagTypeName))
                {
                    var available = ElementLookupHelper.GetAvailableTagTypesForCategory(doc, element);
                    return Task.FromResult(ToolResult.Error(
                        $"Tag type '{tagTypeName}' not found for {categoryName}. Available: {available}"));
                }
                return Task.FromResult(ToolResult.Error(
                    $"No tag types available for category '{categoryName}'. " +
                    "Load a tag family for this category first."));
            }

            // Activate tag type if needed
            if (!tagType.IsActive)
                tagType.Activate();

            // Determine tag location
            XYZ tagLocation;
            if (input.TryGetProperty("location", out var locElement))
            {
                var locArray = locElement.EnumerateArray().ToList();
                if (locArray.Count != 2)
                    return Task.FromResult(ToolResult.Error("location must be an array of exactly 2 numbers [x, y]."));
                tagLocation = new XYZ(locArray[0].GetDouble(), locArray[1].GetDouble(), 0);
            }
            else
            {
                // Default to element bounding box center
                var bbox = element.get_BoundingBox(view);
                if (bbox != null)
                {
                    tagLocation = (bbox.Min + bbox.Max) / 2.0;
                }
                else
                {
                    // Fall back to element location if no bounding box
                    var loc = element.Location;
                    if (loc is LocationPoint lp)
                        tagLocation = lp.Point;
                    else if (loc is LocationCurve lc)
                        tagLocation = lc.Curve.Evaluate(0.5, true);
                    else
                        return Task.FromResult(ToolResult.Error(
                            "Cannot determine element location for tag placement. Please specify the 'location' parameter."));
                }
            }

            // Determine orientation
            var orientation = TagOrientation.Horizontal;
            if (input.TryGetProperty("orientation", out var orientElement))
            {
                var orientStr = orientElement.GetString()?.ToLowerInvariant();
                if (orientStr == "vertical")
                    orientation = TagOrientation.Vertical;
            }

            // Determine leader
            var hasLeader = false;
            if (input.TryGetProperty("has_leader", out var leaderElement))
                hasLeader = leaderElement.GetBoolean();

            // Create the tag
            var reference = new Reference(element);
            var tag = IndependentTag.Create(
                doc,
                view.Id,
                reference,
                hasLeader,
                TagMode.TM_ADDBY_CATEGORY,
                orientation,
                tagLocation);

            // Set tag type if different from default
            if (tag.GetTypeId() != tagType.Id)
                tag.ChangeTypeId(tagType.Id);

            var result = new PlaceTagResult
            {
                TagId = tag.Id.Value,
                ElementId = elementIdElement.GetInt64(),
                TagType = tagType.Name,
                ViewId = view.Id.Value,
                ViewName = view.Name,
                Location = new[] { tagLocation.X, tagLocation.Y },
                HasLeader = hasLeader,
                Message = $"Tagged {element.Category?.Name ?? "element"} (ID: {elementIdElement.GetInt64()}) " +
                          $"with '{tagType.Name}' in '{view.Name}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error(
                $"Failed to place tag: {ex.Message}. " +
                "Ensure the element is visible in the target view."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceTagResult
    {
        public long TagId { get; set; }
        public long ElementId { get; set; }
        public string TagType { get; set; } = string.Empty;
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public double[] Location { get; set; } = Array.Empty<double>();
        public bool HasLeader { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
