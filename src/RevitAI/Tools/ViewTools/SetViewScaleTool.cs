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

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that sets the scale of a view.
/// </summary>
public sealed class SetViewScaleTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static SetViewScaleTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The element ID of the view to modify. Optional - uses active view if not specified."
                    },
                    "scale": {
                        "type": "integer",
                        "description": "The scale denominator (e.g. 48 for 1:48, 96 for 1:96). Must be a positive integer."
                    }
                },
                "required": ["scale"],
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

    public string Name => "set_view_scale";

    public string Description =>
        "Sets the scale of a view (e.g. 48 for 1/4\" = 1'-0\", 96 for 1/8\" = 1'-0\"). " +
        "Uses the active view if no view_id is specified.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var scale = input.TryGetProperty("scale", out var s) ? s.GetInt32() : 0;
        return $"Would set view scale to 1:{scale}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get required scale parameter
        if (!input.TryGetProperty("scale", out var scaleElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: scale"));

        var scale = scaleElement.GetInt32();
        if (scale <= 0)
            return Task.FromResult(ToolResult.Error("Parameter 'scale' must be a positive integer."));

        try
        {
            // Resolve view using general-purpose resolver
            var (view, viewError) = DraftingHelper.ResolveView(doc, input);
            if (viewError != null) return Task.FromResult(viewError);

            // Reject view types that don't support scale
            if (view!.ViewType == ViewType.Schedule || view.ViewType == ViewType.ColumnSchedule ||
                view.ViewType == ViewType.PanelSchedule)
                return Task.FromResult(ToolResult.Error("Schedule views do not have a settable scale."));

            if (view.ViewType == ViewType.DrawingSheet)
                return Task.FromResult(ToolResult.Error("Sheet views do not have a settable scale."));

            var oldScale = view.Scale;

            if (oldScale == scale)
            {
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(new SetViewScaleResult
                {
                    ViewId = view.Id.Value,
                    ViewName = view.Name,
                    OldScale = oldScale,
                    NewScale = scale,
                    Message = $"View '{view.Name}' is already at scale 1:{scale}."
                }, _jsonOptions)));
            }

            view.Scale = scale;

            var result = new SetViewScaleResult
            {
                ViewId = view.Id.Value,
                ViewName = view.Name,
                OldScale = oldScale,
                NewScale = scale,
                Message = $"Set view '{view.Name}' scale from 1:{oldScale} to 1:{scale}."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to set view scale: {ex.Message}"));
        }
    }

    private sealed class SetViewScaleResult
    {
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public int OldScale { get; set; }
        public int NewScale { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
