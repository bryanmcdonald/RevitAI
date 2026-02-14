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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that creates a callout (detail or section) in a parent view.
/// Supports both new callouts and reference callouts to existing views.
/// </summary>
public sealed class PlaceCalloutTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceCalloutTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "parent_view_id": {
                        "type": "integer",
                        "description": "The element ID of the parent view to place the callout in."
                    },
                    "callout_type": {
                        "type": "string",
                        "enum": ["detail", "section"],
                        "description": "The type of callout to create: 'detail' for a detail callout, 'section' for a section callout."
                    },
                    "min_point": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Bottom-left corner [x, y] of the callout rectangle in feet."
                    },
                    "max_point": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Top-right corner [x, y] of the callout rectangle in feet."
                    },
                    "reference_view_id": {
                        "type": "integer",
                        "description": "Optional. If provided, creates a reference callout pointing to this existing view instead of creating a new callout view."
                    }
                },
                "required": ["parent_view_id", "callout_type", "min_point", "max_point"],
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

    public string Name => "place_callout";

    public string Description =>
        "Creates a callout in a parent view. Supports detail callouts and section callouts. " +
        "A callout creates a new cropped view of the specified area. " +
        "Optionally, provide reference_view_id to create a reference callout that points to an existing view " +
        "instead of creating a new one. The parent view must be a plan, section, elevation, or detail view.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var calloutType = input.TryGetProperty("callout_type", out var ct) ? ct.GetString() : "detail";
        var isReference = input.TryGetProperty("reference_view_id", out _);
        var prefix = isReference ? "reference " : "";
        return $"Would create {prefix}{calloutType} callout.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Resolve parent view
            if (!input.TryGetProperty("parent_view_id", out var parentViewIdElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: parent_view_id"));

            var parentViewId = new ElementId(parentViewIdElement.GetInt64());
            var parentView = doc.GetElement(parentViewId) as View;
            if (parentView == null)
                return Task.FromResult(ToolResult.Error($"View with ID {parentViewIdElement.GetInt64()} not found."));

            // Validate parent view type
            var validParentTypes = new[]
            {
                ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.AreaPlan, ViewType.EngineeringPlan,
                ViewType.Section, ViewType.Elevation, ViewType.Detail
            };

            if (!validParentTypes.Contains(parentView.ViewType))
                return Task.FromResult(ToolResult.Error(
                    $"Callouts cannot be placed in {parentView.ViewType} views. " +
                    "Use a plan, section, elevation, or detail view as the parent."));

            // Get callout type
            if (!input.TryGetProperty("callout_type", out var calloutTypeElement))
                return Task.FromResult(ToolResult.Error("Missing required parameter: callout_type"));

            var calloutTypeStr = calloutTypeElement.GetString();
            var targetFamily = calloutTypeStr switch
            {
                "detail" => ViewFamily.Detail,
                "section" => ViewFamily.Section,
                _ => (ViewFamily?)null
            };

            if (targetFamily == null)
                return Task.FromResult(ToolResult.Error(
                    $"Invalid callout_type '{calloutTypeStr}'. Must be 'detail' or 'section'."));

            // Parse min/max points
            var (minPoint, minError) = DraftingHelper.ParsePoint(input, "min_point");
            if (minError != null) return Task.FromResult(minError);

            var (maxPoint, maxError) = DraftingHelper.ParsePoint(input, "max_point");
            if (maxError != null) return Task.FromResult(maxError);

            // Validate min != max
            if (minPoint!.DistanceTo(maxPoint!) < 0.01)
                return Task.FromResult(ToolResult.Error(
                    "min_point and max_point are too close together. They must define a rectangle with non-zero area."));

            // Check if this is a reference callout
            var isReference = input.TryGetProperty("reference_view_id", out var refViewIdElement);

            if (isReference)
            {
                // Reference callout — points to an existing view
                var refViewId = new ElementId(refViewIdElement.GetInt64());
                var refView = doc.GetElement(refViewId) as View;
                if (refView == null)
                    return Task.FromResult(ToolResult.Error($"Reference view with ID {refViewIdElement.GetInt64()} not found."));

                ViewSection.CreateReferenceCallout(doc, parentViewId, refViewId, minPoint, maxPoint);

                var refResult = new PlaceCalloutResult
                {
                    ParentViewId = parentViewId.Value,
                    ParentViewName = parentView.Name,
                    CalloutType = calloutTypeStr ?? "detail",
                    IsReferenceCallout = true,
                    ReferenceViewId = refViewId.Value,
                    ReferenceViewName = refView.Name,
                    Message = $"Created reference {calloutTypeStr} callout in '{parentView.Name}' pointing to '{refView.Name}'."
                };

                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(refResult, _jsonOptions)));
            }
            else
            {
                // New callout — find the ViewFamilyType
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == targetFamily.Value);

                if (vft == null)
                    return Task.FromResult(ToolResult.Error(
                        $"No {calloutTypeStr} view family type found in the document."));

                var calloutView = ViewSection.CreateCallout(doc, parentViewId, vft.Id, minPoint, maxPoint);

                var result = new PlaceCalloutResult
                {
                    CalloutViewId = calloutView.Id.Value,
                    CalloutViewName = calloutView.Name,
                    ParentViewId = parentViewId.Value,
                    ParentViewName = parentView.Name,
                    CalloutType = calloutTypeStr ?? "detail",
                    IsReferenceCallout = false,
                    Message = $"Created {calloutTypeStr} callout '{calloutView.Name}' in '{parentView.Name}'."
                };

                return Task.FromResult(ToolResult.OkWithElements(
                    JsonSerializer.Serialize(result, _jsonOptions), new[] { calloutView.Id.Value }));
            }
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create callout: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceCalloutResult
    {
        public long? CalloutViewId { get; set; }
        public string? CalloutViewName { get; set; }
        public long ParentViewId { get; set; }
        public string ParentViewName { get; set; } = string.Empty;
        public string CalloutType { get; set; } = string.Empty;
        public bool IsReferenceCallout { get; set; }
        public long? ReferenceViewId { get; set; }
        public string? ReferenceViewName { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
