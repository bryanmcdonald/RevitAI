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
/// Tool that changes the display style (visual style) of the current view.
/// </summary>
public sealed class SetDisplayStyleTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;
    private static readonly Dictionary<string, DisplayStyle> _styleMap;

    static SetDisplayStyleTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "style": {
                        "type": "string",
                        "enum": ["Wireframe", "HiddenLine", "Shaded", "ShadedWithEdges", "Realistic", "Consistent", "Rendered"],
                        "description": "The display style to apply. Wireframe shows edges only. HiddenLine removes back faces. Shaded adds surface colors. ShadedWithEdges combines shading with edge lines. Realistic uses material appearances. Consistent (FlatColors) uses flat colors. Rendered applies full rendering."
                    }
                },
                "required": ["style"],
                "additionalProperties": false
            }
            """;
        _inputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Map friendly names to Revit DisplayStyle enum
        _styleMap = new Dictionary<string, DisplayStyle>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wireframe"] = DisplayStyle.Wireframe,
            ["HiddenLine"] = DisplayStyle.HLR,
            ["Shaded"] = DisplayStyle.Shading,
            ["ShadedWithEdges"] = DisplayStyle.ShadingWithEdges,
            ["Realistic"] = DisplayStyle.Realistic,
            ["Consistent"] = DisplayStyle.FlatColors,
            ["Rendered"] = DisplayStyle.Rendering
        };
    }

    public string Name => "set_display_style";

    public string Description =>
        "Changes the display style (visual style) of the current view. " +
        "Options: Wireframe, HiddenLine, Shaded, ShadedWithEdges, Realistic, Consistent, Rendered.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var activeView = uiDoc.ActiveView;

        // Get style parameter
        if (!input.TryGetProperty("style", out var styleElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: style"));

        var styleName = styleElement.GetString();
        if (string.IsNullOrEmpty(styleName))
            return Task.FromResult(ToolResult.Error("Style parameter cannot be empty."));

        if (!_styleMap.TryGetValue(styleName, out var displayStyle))
        {
            var validStyles = string.Join(", ", _styleMap.Keys);
            return Task.FromResult(ToolResult.Error($"Invalid style '{styleName}'. Valid options: {validStyles}"));
        }

        try
        {
            // Get previous style before attempting change
            var previousStyle = activeView.DisplayStyle;

            // Verify this is a view type that supports display style changes
            // Schedule views, sheet views, and legends don't support display style
            if (activeView.ViewType == ViewType.Schedule ||
                activeView.ViewType == ViewType.DrawingSheet ||
                activeView.ViewType == ViewType.Legend)
            {
                return Task.FromResult(ToolResult.Error(
                    $"The current view '{activeView.Name}' ({activeView.ViewType}) does not support display style changes."));
            }

            // Apply the display style (transaction is handled by the framework)
            activeView.DisplayStyle = displayStyle;

            var result = new SetDisplayStyleResult
            {
                Style = styleName,
                PreviousStyle = GetFriendlyStyleName(previousStyle),
                ViewName = activeView.Name,
                Message = $"Changed display style from '{GetFriendlyStyleName(previousStyle)}' to '{styleName}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static string GetFriendlyStyleName(DisplayStyle style)
    {
        return style switch
        {
            DisplayStyle.Wireframe => "Wireframe",
            DisplayStyle.HLR => "HiddenLine",
            DisplayStyle.Shading => "Shaded",
            DisplayStyle.ShadingWithEdges => "ShadedWithEdges",
            DisplayStyle.Realistic => "Realistic",
            DisplayStyle.FlatColors => "Consistent",
            DisplayStyle.Rendering => "Rendered",
            _ => style.ToString()
        };
    }

    private sealed class SetDisplayStyleResult
    {
        public string Style { get; set; } = string.Empty;
        public string PreviousStyle { get; set; } = string.Empty;
        public string ViewName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
