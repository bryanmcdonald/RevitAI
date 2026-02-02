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
/// Tool that switches the active view to a specified view by ID.
/// </summary>
public sealed class SwitchViewTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static SwitchViewTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The element ID of the view to switch to"
                    }
                },
                "required": ["view_id"],
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

    public string Name => "switch_view";

    public string Description =>
        "Switches the active view to the specified view by ID. " +
        "Use get_view_list first to find available view IDs.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;

        // Get view_id parameter
        if (!input.TryGetProperty("view_id", out var viewIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: view_id"));

        var viewId = new ElementId(viewIdElement.GetInt64());

        try
        {
            var view = doc.GetElement(viewId) as View;
            if (view == null)
                return Task.FromResult(ToolResult.Error($"View with ID {viewId.Value} not found."));

            if (view.IsTemplate)
                return Task.FromResult(ToolResult.Error("Cannot switch to a view template. Please specify a regular view."));

            // Check if the view can be displayed (some views like schedules may have restrictions)
            if (!view.CanBePrinted)
            {
                // This is a soft check - view might still be switchable
            }

            uiDoc.ActiveView = view;

            var result = new SwitchViewResult
            {
                SwitchedTo = view.Name,
                ViewType = view.ViewType.ToString(),
                ViewId = view.Id.Value
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Cannot switch to view: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to switch view: {ex.Message}"));
        }
    }

    private sealed class SwitchViewResult
    {
        public string SwitchedTo { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public long ViewId { get; set; }
    }
}
