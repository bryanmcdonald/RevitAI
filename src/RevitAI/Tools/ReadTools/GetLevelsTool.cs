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
/// Tool that returns all levels in the Revit project with their names and elevations.
/// </summary>
public sealed class GetLevelsTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetLevelsTool()
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

    public string Name => "get_levels";

    public string Description => "Returns all levels in the Revit project with their names, elevations, and IDs. Use this to understand the vertical organization of the building.";

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
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new LevelData
                {
                    Id = l.Id.Value,
                    Name = l.Name,
                    Elevation = l.Elevation,
                    ElevationFormatted = FormatElevation(l.Elevation)
                })
                .ToList();

            var result = new GetLevelsResult
            {
                Levels = levels,
                Count = levels.Count
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get levels: {ex.Message}"));
        }
    }

    private static string FormatElevation(double feet)
    {
        var totalInches = feet * 12;
        var wholeFeet = (int)Math.Floor(Math.Abs(feet));
        var inches = Math.Abs(totalInches) - (wholeFeet * 12);
        var sign = feet < 0 ? "-" : "";

        if (wholeFeet == 0)
            return $"{sign}{inches:F2}\"";
        else if (Math.Abs(inches) < 0.01)
            return $"{sign}{wholeFeet}'-0\"";
        else
            return $"{sign}{wholeFeet}'-{inches:F2}\"";
    }

    private sealed class LevelData
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Elevation { get; set; }
        public string ElevationFormatted { get; set; } = string.Empty;
    }

    private sealed class GetLevelsResult
    {
        public List<LevelData> Levels { get; set; } = new();
        public int Count { get; set; }
    }
}
