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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that lists available line styles in the document.
/// Returns structured data with name and ID for each style.
/// </summary>
public sealed class GetLineStylesTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetLineStylesTool()
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

    public string Name => "get_line_styles";

    public string Description => "Lists available line styles in the document with their IDs. " +
        "Use this before placing detail lines or arcs to discover available line style names. " +
        "Pass a style name to the 'line_style' parameter on drawing tools.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            var lineCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lineCategory == null)
                return Task.FromResult(ToolResult.Error("Line styles category not found in document."));

            var styles = new List<LineStyleData>();
            foreach (Category subCat in lineCategory.SubCategories)
            {
                var graphicsStyle = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (graphicsStyle != null)
                {
                    styles.Add(new LineStyleData
                    {
                        Id = graphicsStyle.Id.Value,
                        Name = subCat.Name
                    });
                }
            }

            styles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var result = new GetLineStylesResult
            {
                LineStyles = styles,
                Count = styles.Count
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get line styles: {ex.Message}"));
        }
    }

    private sealed class LineStyleData
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class GetLineStylesResult
    {
        public List<LineStyleData> LineStyles { get; set; } = new();
        public int Count { get; set; }
    }
}
