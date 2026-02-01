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

    public string Description => "Returns all grids in the Revit project with their names, start/end points, and whether they are curved. Use this to understand the structural grid layout.";

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
    }

    private sealed class GetGridsResult
    {
        public List<GridData> Grids { get; set; } = new();
        public int Count { get; set; }
    }
}
