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
using RevitAI.Services;

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Read-only tool that resolves the intersection point of two named grids.
/// Useful for getting coordinates for floor boundaries and multi-step planning.
/// </summary>
public sealed class ResolveGridIntersectionTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ResolveGridIntersectionTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "grid1": {
                        "type": "string",
                        "description": "Name of the first grid."
                    },
                    "grid2": {
                        "type": "string",
                        "description": "Name of the second grid."
                    }
                },
                "required": ["grid1", "grid2"],
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

    public string Name => "resolve_grid_intersection";

    public string Description => "Returns the [x, y] coordinates (in feet) where two grids intersect. Useful for getting coordinates for floor boundaries or when you need explicit coordinate values.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("grid1", out var grid1Elem))
            return Task.FromResult(ToolResult.Error("Missing required parameter: grid1"));

        if (!input.TryGetProperty("grid2", out var grid2Elem))
            return Task.FromResult(ToolResult.Error("Missing required parameter: grid2"));

        var grid1Name = grid1Elem.GetString();
        var grid2Name = grid2Elem.GetString();

        if (string.IsNullOrWhiteSpace(grid1Name) || string.IsNullOrWhiteSpace(grid2Name))
            return Task.FromResult(ToolResult.Error("grid1 and grid2 cannot be empty."));

        try
        {
            var (point, error) = GeometryResolver.ResolveGridIntersection(doc, grid1Name, grid2Name);

            if (point == null)
                return Task.FromResult(ToolResult.Error(error!));

            var result = new GridIntersectionResult
            {
                X = Math.Round(point.X, 4),
                Y = Math.Round(point.Y, 4),
                Grid1 = grid1Name,
                Grid2 = grid2Name,
                Message = $"Grid {grid1Name}/{grid2Name} intersection at ({point.X:F2}, {point.Y:F2})."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to resolve grid intersection: {ex.Message}"));
        }
    }

    private sealed class GridIntersectionResult
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Grid1 { get; set; } = string.Empty;
        public string Grid2 { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
