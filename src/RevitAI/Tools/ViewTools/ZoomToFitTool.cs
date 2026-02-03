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
using Autodesk.Revit.UI;

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that zooms the view to fit all visible content.
/// </summary>
public sealed class ZoomToFitTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ZoomToFitTool()
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

    public string Name => "zoom_to_fit";

    public string Description =>
        "Zooms the current view to fit all visible content, showing the entire model extent. " +
        "Works in both 2D and 3D views.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            var uiView = GetActiveUIView(uiDoc);
            if (uiView == null)
                return Task.FromResult(ToolResult.Error("Cannot access the active view for zoom operations."));

            uiView.ZoomToFit();

            var result = new ZoomToFitResult
            {
                ViewName = uiDoc.ActiveView.Name,
                ViewType = uiDoc.ActiveView.ViewType.ToString(),
                Message = "View zoomed to fit all visible content."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static UIView? GetActiveUIView(UIDocument uiDoc)
    {
        var uiViews = uiDoc.GetOpenUIViews();
        return uiViews.FirstOrDefault(v => v.ViewId == uiDoc.ActiveView.Id);
    }

    private sealed class ZoomToFitResult
    {
        public string ViewName { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
