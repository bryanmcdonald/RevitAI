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

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Tool that returns all grids in the Revit project with their names and geometry.
/// </summary>
public sealed class GetGridsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetGridsTool()
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

    public string Name => "get_grids";

    public string Description => "Returns all grids in the Revit project with their names, start/end points (in feet), orientation (Horizontal/Vertical/Diagonal), angle in degrees (0=East-West, 90=North-South), and whether they are curved. Use this to understand the structural grid layout.";

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
            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .OrderBy(g => g.Name)
                .Select(g => ExtractGridData(g))
                .ToList();

            var result = new GetGridsResult
            {
                Grids = grids,
                Count = grids.Count
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get grids: {ex.Message}"));
        }
    }

    private static GridData ExtractGridData(Grid grid)
    {
        var data = new GridData
        {
            Id = grid.Id.Value,
            Name = grid.Name,
            IsCurved = false
        };

        var curve = grid.Curve;
        if (curve != null)
        {
            data.IsCurved = !(curve is Line);

            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);

            data.Start = new PointData
            {
                X = Math.Round(start.X, 4),
                Y = Math.Round(start.Y, 4),
                Z = Math.Round(start.Z, 4)
            };

            data.End = new PointData
            {
                X = Math.Round(end.X, 4),
                Y = Math.Round(end.Y, 4),
                Z = Math.Round(end.Z, 4)
            };

            if (curve is Line line)
            {
                data.Length = Math.Round(line.Length, 4);

                // Calculate orientation based on the grid direction
                var direction = line.Direction;
                var angleRad = Math.Atan2(direction.Y, direction.X);
                var angleDeg = angleRad * 180.0 / Math.PI;

                // Normalize angle to 0-180 range (grids are bidirectional)
                if (angleDeg < 0) angleDeg += 180;
                if (angleDeg >= 180) angleDeg -= 180;

                data.AngleDegrees = Math.Round(angleDeg, 2);

                // Determine orientation: 0° = East-West (horizontal), 90° = North-South (vertical)
                // Use 10-degree tolerance for classification
                if (angleDeg <= 10 || angleDeg >= 170)
                    data.Orientation = "Horizontal (East-West)";
                else if (angleDeg >= 80 && angleDeg <= 100)
                    data.Orientation = "Vertical (North-South)";
                else
                    data.Orientation = "Diagonal";
            }
        }

        return data;
    }

    private sealed class PointData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    private sealed class GridData
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public PointData? Start { get; set; }
        public PointData? End { get; set; }
        public bool IsCurved { get; set; }
        public double? Length { get; set; }
        public double? AngleDegrees { get; set; }
        public string? Orientation { get; set; }
    }

    private sealed class GetGridsResult
    {
        public List<GridData> Grids { get; set; } = new();
        public int Count { get; set; }
    }
}
