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

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that creates an elevation view at a specified location.
/// </summary>
public sealed class CreateElevationViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateElevationViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the new elevation view"
                    },
                    "origin": {
                        "type": "object",
                        "description": "Location for the elevation marker (in feet)",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" },
                            "z": { "type": "number" }
                        },
                        "required": ["x", "y", "z"],
                        "additionalProperties": false
                    },
                    "direction": {
                        "type": "string",
                        "description": "Direction the elevation looks (North=+Y, South=-Y, East=+X, West=-X)",
                        "enum": ["North", "South", "East", "West"],
                        "default": "North"
                    },
                    "scale": {
                        "type": "integer",
                        "description": "View scale (e.g., 48 for 1/4\" = 1'-0\")",
                        "default": 48
                    },
                    "plan_view_id": {
                        "type": "integer",
                        "description": "Optional: ID of the plan view to host the elevation marker. If not specified, uses a floor plan from the document."
                    },
                    "view_family_type_id": {
                        "type": "integer",
                        "description": "Optional: specific ViewFamilyType ID to use"
                    }
                },
                "required": ["name", "origin"],
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

    public string Name => "create_elevation_view";

    public string Description =>
        "Creates an elevation view at the specified location, looking in the specified direction. " +
        "Directions: North (+Y), South (-Y), East (+X), West (-X). An elevation marker will be placed on a plan view.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;

        // Get required parameters
        if (!input.TryGetProperty("name", out var nameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: name"));

        if (!input.TryGetProperty("origin", out var originElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: origin"));

        var viewName = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(viewName))
            return Task.FromResult(ToolResult.Error("Parameter 'name' cannot be empty."));

        // Validate name doesn't contain invalid characters
        if (viewName.Contains(':'))
            return Task.FromResult(ToolResult.Error(
                "View names cannot contain colons (:). Please remove the colon from the name."));

        // Parse origin
        var origin = new XYZ(
            originElement.GetProperty("x").GetDouble(),
            originElement.GetProperty("y").GetDouble(),
            originElement.GetProperty("z").GetDouble());

        // Get direction (default North)
        var direction = "North";
        if (input.TryGetProperty("direction", out var dirProp))
        {
            var dirStr = dirProp.GetString();
            if (!string.IsNullOrEmpty(dirStr))
                direction = dirStr;
        }

        // Get scale (default 48)
        var scale = 48;
        if (input.TryGetProperty("scale", out var scaleProp))
        {
            scale = scaleProp.GetInt32();
        }

        try
        {
            // Get elevation ViewFamilyType
            ViewFamilyType? vft = null;
            if (input.TryGetProperty("view_family_type_id", out var vftIdProp))
            {
                vft = doc.GetElement(new ElementId(vftIdProp.GetInt64())) as ViewFamilyType;
                if (vft == null)
                    return Task.FromResult(ToolResult.Error("Invalid view_family_type_id."));
                if (vft.ViewFamily != ViewFamily.Elevation)
                    return Task.FromResult(ToolResult.Error($"ViewFamilyType {vft.Name} is not an Elevation type."));
            }
            else
            {
                vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);
            }

            if (vft == null)
                return Task.FromResult(ToolResult.Error("No elevation view family type found in the document."));

            // Get or find plan view for the marker
            ViewPlan? planView = null;
            if (input.TryGetProperty("plan_view_id", out var planIdProp))
            {
                planView = doc.GetElement(new ElementId(planIdProp.GetInt64())) as ViewPlan;
                if (planView == null)
                    return Task.FromResult(ToolResult.Error("Invalid plan_view_id. Must be a floor plan or ceiling plan."));
            }
            else
            {
                // Try to find a suitable floor plan
                planView = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                    .FirstOrDefault();
            }

            if (planView == null)
                return Task.FromResult(ToolResult.Error("No floor plan view found. Please specify plan_view_id or create a floor plan first."));

            // Create elevation marker at the origin
            var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, scale);

            // Get direction index (0=North/+Y, 1=East/+X, 2=South/-Y, 3=West/-X)
            var directionIndex = GetDirectionIndex(direction);

            // Create elevation view from marker
            var view = marker.CreateElevation(doc, planView.Id, directionIndex);

            // Try to set the name
            try
            {
                view.Name = viewName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A view named '{viewName}' already exists. Please choose a different name."));
            }

            // Switch to the newly created view
            uiDoc.ActiveView = view;

            var result = new CreateElevationResult
            {
                CreatedViewId = view.Id.Value,
                Name = view.Name,
                ViewType = "Elevation",
                Direction = direction,
                MarkerId = marker.Id.Value,
                HostPlanView = planView.Name
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create elevation view: {ex.Message}"));
        }
    }

    /// <summary>
    /// Maps direction name to elevation marker index.
    /// Based on observed behavior, marker indices are:
    /// 0=West (-X), 1=North (+Y), 2=East (+X), 3=South (-Y)
    /// </summary>
    private static int GetDirectionIndex(string direction)
    {
        return direction switch
        {
            "North" => 1,
            "East" => 2,
            "South" => 3,
            "West" => 0,
            _ => 1 // Default to North
        };
    }

    private sealed class CreateElevationResult
    {
        public long CreatedViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public long MarkerId { get; set; }
        public string HostPlanView { get; set; } = string.Empty;
    }
}
