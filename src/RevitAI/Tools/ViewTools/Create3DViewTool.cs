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
/// Tool that creates a new 3D view with optional orientation preset.
/// </summary>
public sealed class Create3DViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static Create3DViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the new 3D view"
                    },
                    "orientation": {
                        "type": "string",
                        "description": "View orientation preset",
                        "enum": ["Isometric", "Front", "Back", "Left", "Right", "Top", "Bottom"],
                        "default": "Isometric"
                    },
                    "view_family_type_id": {
                        "type": "integer",
                        "description": "Optional: specific ViewFamilyType ID to use"
                    }
                },
                "required": ["name"],
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

    public string Name => "create_3d_view";

    public string Description =>
        "Creates a new 3D view. Optionally specify an orientation preset: " +
        "Isometric (default), Front, Back, Left, Right, Top, Bottom.";

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

        var viewName = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(viewName))
            return Task.FromResult(ToolResult.Error("Parameter 'name' cannot be empty."));

        // Validate name doesn't contain invalid characters
        if (viewName.Contains(':'))
            return Task.FromResult(ToolResult.Error(
                "View names cannot contain colons (:). Please remove the colon from the name."));

        // Get optional orientation
        var orientation = "Isometric";
        if (input.TryGetProperty("orientation", out var orientProp))
        {
            var orientStr = orientProp.GetString();
            if (!string.IsNullOrEmpty(orientStr))
                orientation = orientStr;
        }

        try
        {
            // Get 3D view family type
            ViewFamilyType? vft = null;
            if (input.TryGetProperty("view_family_type_id", out var vftIdProp))
            {
                vft = doc.GetElement(new ElementId(vftIdProp.GetInt64())) as ViewFamilyType;
                if (vft == null)
                    return Task.FromResult(ToolResult.Error("Invalid view_family_type_id."));
                if (vft.ViewFamily != ViewFamily.ThreeDimensional)
                    return Task.FromResult(ToolResult.Error($"ViewFamilyType {vft.Name} is not a 3D type."));
            }
            else
            {
                vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            }

            if (vft == null)
                return Task.FromResult(ToolResult.Error("No 3D view family type found in the document."));

            // Create the 3D view
            var view = View3D.CreateIsometric(doc, vft.Id);

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

            // Apply orientation (skip for Isometric - Revit's CreateIsometric default is already good)
            if (orientation != "Isometric")
            {
                var viewOrientation = GetViewOrientation(orientation);
                if (viewOrientation != null)
                {
                    view.SetOrientation(viewOrientation);
                }
            }

            // Switch to the newly created view
            uiDoc.ActiveView = view;

            var result = new Create3DViewResult
            {
                CreatedViewId = view.Id.Value,
                Name = view.Name,
                Orientation = orientation,
                ViewType = "3D"
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create 3D view: {ex.Message}"));
        }
    }

    private static ViewOrientation3D? GetViewOrientation(string preset)
    {
        // Standard orientations looking at origin from distance
        // Note: Isometric is handled by Revit's default CreateIsometric, not here
        var eye = preset switch
        {
            "Front" => new XYZ(0, -100, 0),
            "Back" => new XYZ(0, 100, 0),
            "Left" => new XYZ(-100, 0, 0),
            "Right" => new XYZ(100, 0, 0),
            "Top" => new XYZ(0, 0, 100),
            "Bottom" => new XYZ(0, 0, -100),
            _ => null
        };

        if (eye == null) return null;

        // Determine up vector (for top/bottom views, use Y as up since Z is the view direction)
        var up = preset is "Top" or "Bottom" ? XYZ.BasisY : XYZ.BasisZ;

        // Calculate forward direction (from eye toward origin)
        var forward = (-eye).Normalize();

        return new ViewOrientation3D(eye, up, forward);
    }

    private sealed class Create3DViewResult
    {
        public long CreatedViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Orientation { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
    }
}
