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
/// Tool that creates a section view at a specified location and direction.
/// </summary>
public sealed class CreateSectionViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateSectionViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the new section view"
                    },
                    "origin": {
                        "type": "object",
                        "description": "Origin point of the section (in feet)",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" },
                            "z": { "type": "number" }
                        },
                        "required": ["x", "y", "z"],
                        "additionalProperties": false
                    },
                    "direction": {
                        "type": "object",
                        "description": "View direction vector (section looks this direction)",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" },
                            "z": { "type": "number" }
                        },
                        "required": ["x", "y", "z"],
                        "additionalProperties": false
                    },
                    "width": {
                        "type": "number",
                        "description": "Section width in feet (default 20)",
                        "default": 20
                    },
                    "height": {
                        "type": "number",
                        "description": "Section height in feet (default 20)",
                        "default": 20
                    },
                    "depth": {
                        "type": "number",
                        "description": "Section depth/far clip in feet (default 10)",
                        "default": 10
                    },
                    "view_family_type_id": {
                        "type": "integer",
                        "description": "Optional: specific ViewFamilyType ID to use"
                    }
                },
                "required": ["name", "origin", "direction"],
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

    public string Name => "create_section_view";

    public string Description =>
        "Creates a section view at the specified origin point, looking in the specified direction. " +
        "The section plane is perpendicular to the direction vector. " +
        "IMPORTANT: Always call switch_view immediately after to open the new view.";

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

        if (!input.TryGetProperty("direction", out var directionElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: direction"));

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

        // Parse and normalize direction
        var direction = new XYZ(
            directionElement.GetProperty("x").GetDouble(),
            directionElement.GetProperty("y").GetDouble(),
            directionElement.GetProperty("z").GetDouble());

        if (direction.IsZeroLength())
            return Task.FromResult(ToolResult.Error("Direction vector cannot be zero."));

        direction = direction.Normalize();

        // Get optional dimensions
        var width = input.TryGetProperty("width", out var w) ? w.GetDouble() : 20.0;
        var height = input.TryGetProperty("height", out var h) ? h.GetDouble() : 20.0;
        var depth = input.TryGetProperty("depth", out var d) ? d.GetDouble() : 10.0;

        if (width <= 0 || height <= 0 || depth <= 0)
            return Task.FromResult(ToolResult.Error("Width, height, and depth must be positive values."));

        try
        {
            // Get section view family type
            ViewFamilyType? vft = null;
            if (input.TryGetProperty("view_family_type_id", out var vftIdProp))
            {
                vft = doc.GetElement(new ElementId(vftIdProp.GetInt64())) as ViewFamilyType;
                if (vft == null)
                    return Task.FromResult(ToolResult.Error("Invalid view_family_type_id."));
                if (vft.ViewFamily != ViewFamily.Section)
                    return Task.FromResult(ToolResult.Error($"ViewFamilyType {vft.Name} is not a Section type."));
            }
            else
            {
                vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);
            }

            if (vft == null)
                return Task.FromResult(ToolResult.Error("No section view family type found in the document."));

            // Create section box transform
            // BasisZ = view direction (what the section looks at)
            // BasisX = right direction
            // BasisY = up direction

            var right = direction.CrossProduct(XYZ.BasisZ).Normalize();
            if (right.IsZeroLength())
            {
                // Direction is vertical, use X axis as right
                right = XYZ.BasisX;
            }
            var up = right.CrossProduct(direction);

            var transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = direction;

            // Section box bounds (relative to transform)
            var sectionBox = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(-width / 2, -height / 2, 0),
                Max = new XYZ(width / 2, height / 2, depth)
            };

            var view = ViewSection.CreateSection(doc, vft.Id, sectionBox);

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

            var result = new CreateSectionResult
            {
                CreatedViewId = view.Id.Value,
                Name = view.Name,
                ViewType = "Section",
                Origin = new PointResult { X = origin.X, Y = origin.Y, Z = origin.Z },
                Direction = new PointResult { X = direction.X, Y = direction.Y, Z = direction.Z }
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create section view: {ex.Message}"));
        }
    }

    private sealed class PointResult
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    private sealed class CreateSectionResult
    {
        public long CreatedViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public PointResult? Origin { get; set; }
        public PointResult? Direction { get; set; }
    }
}
