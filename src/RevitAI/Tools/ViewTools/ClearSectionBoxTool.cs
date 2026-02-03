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
/// Tool that clears (disables) the section box on the active 3D view.
/// </summary>
public sealed class ClearSectionBoxTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ClearSectionBoxTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {},
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

    public string Name => "clear_section_box";

    public string Description =>
        "Clears (disables) the section box on the active 3D view, showing the full model extent.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var activeView = uiDoc.ActiveView;

        // Verify view is a 3D view
        if (activeView is not View3D view3D)
        {
            return Task.FromResult(ToolResult.Error(
                $"Section box can only be cleared on 3D views. Current view '{activeView.Name}' is a {activeView.ViewType}."));
        }

        // Check if view is a locked template
        if (view3D.IsTemplate)
        {
            return Task.FromResult(ToolResult.Error("Cannot modify section box on a view template."));
        }

        try
        {
            var wasActive = view3D.IsSectionBoxActive;

            if (!wasActive)
            {
                var result = new ClearSectionBoxResult
                {
                    WasActive = false,
                    ViewName = view3D.Name,
                    Message = $"Section box was not active on view '{view3D.Name}'."
                };
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
            }

            // Disable section box (transaction is handled by the framework)
            view3D.IsSectionBoxActive = false;

            var successResult = new ClearSectionBoxResult
            {
                WasActive = true,
                ViewName = view3D.Name,
                Message = $"Section box cleared on view '{view3D.Name}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(successResult, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class ClearSectionBoxResult
    {
        public bool WasActive { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
