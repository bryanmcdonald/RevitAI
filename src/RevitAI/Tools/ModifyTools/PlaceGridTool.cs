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

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that creates a grid line between two points.
/// </summary>
public sealed class PlaceGridTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceGridTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the grid line (e.g., 'A', '1')."
                    },
                    "start": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "Start point [x, y] in feet."
                    },
                    "end": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 2,
                        "description": "End point [x, y] in feet."
                    }
                },
                "required": ["name", "start", "end"],
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

    public string Name => "place_grid";

    public string Description => "Creates a grid line between two points. Coordinates are in feet. Use get_grids to see existing grids.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var name = input.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "unknown" : "unknown";

        double? length = null;
        if (input.TryGetProperty("start", out var startElem) && input.TryGetProperty("end", out var endElem))
        {
            var start = startElem.EnumerateArray().ToList();
            var end = endElem.EnumerateArray().ToList();
            if (start.Count == 2 && end.Count == 2)
            {
                var dx = end[0].GetDouble() - start[0].GetDouble();
                var dy = end[1].GetDouble() - start[1].GetDouble();
                length = Math.Sqrt(dx * dx + dy * dy);
            }
        }

        return length.HasValue
            ? $"Would create grid '{name}' ({length.Value:F2}' long)."
            : $"Would create grid '{name}'.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("name", out var nameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: name"));

        if (!input.TryGetProperty("start", out var startElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: start"));

        if (!input.TryGetProperty("end", out var endElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: end"));

        try
        {
            var gridName = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(gridName))
                return Task.FromResult(ToolResult.Error("name cannot be empty."));

            // Parse start point
            var startArray = startElement.EnumerateArray().ToList();
            if (startArray.Count != 2)
                return Task.FromResult(ToolResult.Error("start must be an array of exactly 2 numbers [x, y]."));
            var startX = startArray[0].GetDouble();
            var startY = startArray[1].GetDouble();

            // Parse end point
            var endArray = endElement.EnumerateArray().ToList();
            if (endArray.Count != 2)
                return Task.FromResult(ToolResult.Error("end must be an array of exactly 2 numbers [x, y]."));
            var endX = endArray[0].GetDouble();
            var endY = endArray[1].GetDouble();

            // Validate points are different
            if (Math.Abs(startX - endX) < 0.001 && Math.Abs(startY - endY) < 0.001)
                return Task.FromResult(ToolResult.Error("start and end points must be different."));

            // Create the grid line (grids are infinite in Z, placed at Z=0)
            var startPoint = new XYZ(startX, startY, 0);
            var endPoint = new XYZ(endX, endY, 0);
            var line = Line.CreateBound(startPoint, endPoint);

            var grid = Grid.Create(doc, line);
            try
            {
                grid.Name = gridName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                doc.Delete(grid.Id);
                return Task.FromResult(ToolResult.Error(
                    $"A grid named '{gridName}' already exists. Please choose a different name."));
            }

            var gridLength = line.Length;
            var result = new PlaceGridResult
            {
                GridId = grid.Id.Value,
                Name = grid.Name,
                Start = new[] { startX, startY },
                End = new[] { endX, endY },
                Length = Math.Round(gridLength, 4),
                Message = $"Created grid '{grid.Name}' ({gridLength:F2}' long)."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error(
                $"Failed to create grid: {ex.Message}. A grid with this name may already exist."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceGridResult
    {
        public long GridId { get; set; }
        public string Name { get; set; } = string.Empty;
        public double[] Start { get; set; } = Array.Empty<double>();
        public double[] End { get; set; } = Array.Empty<double>();
        public double Length { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
