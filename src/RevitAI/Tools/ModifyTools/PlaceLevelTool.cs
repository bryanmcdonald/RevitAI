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
using RevitAI.Tools.ModifyTools.Helpers;

namespace RevitAI.Tools.ModifyTools;

/// <summary>
/// Tool that creates a new level at a specified elevation.
/// </summary>
public sealed class PlaceLevelTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceLevelTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the new level (e.g., 'Level 3')."
                    },
                    "elevation": {
                        "type": "number",
                        "description": "Elevation in feet above the project origin."
                    }
                },
                "required": ["name", "elevation"],
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

    public string Name => "place_level";

    public string Description => "Creates a new level at a specified elevation. Use get_levels to see existing levels.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var name = input.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "unknown" : "unknown";
        var elevation = input.TryGetProperty("elevation", out var elevElem) ? elevElem.GetDouble() : 0;
        return $"Would create level '{name}' at elevation {elevation:F2}'.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("name", out var nameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: name"));

        if (!input.TryGetProperty("elevation", out var elevationElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: elevation"));

        try
        {
            var levelName = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(levelName))
                return Task.FromResult(ToolResult.Error("name cannot be empty."));

            var elevation = elevationElement.GetDouble();

            // Check for existing level with same name
            var existing = ElementLookupHelper.FindLevelByName(doc, levelName);
            if (existing != null)
            {
                return Task.FromResult(ToolResult.Error(
                    $"A level named '{levelName}' already exists at elevation {existing.Elevation:F2}'. " +
                    $"Existing levels: {ElementLookupHelper.GetAvailableLevelNames(doc)}"));
            }

            // Create the level
            var level = Level.Create(doc, elevation);
            try
            {
                level.Name = levelName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                doc.Delete(level.Id);
                return Task.FromResult(ToolResult.Error(
                    $"Could not name level '{levelName}' — the name may be in use by another element. " +
                    $"Existing levels: {ElementLookupHelper.GetAvailableLevelNames(doc)}"));
            }

            var result = new PlaceLevelResult
            {
                LevelId = level.Id.Value,
                Name = level.Name,
                Elevation = Math.Round(elevation, 4),
                Message = $"Created level '{level.Name}' at elevation {elevation:F2}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error(
                $"Failed to create level: {ex.Message}. " +
                $"Existing levels: {ElementLookupHelper.GetAvailableLevelNames(doc)}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceLevelResult
    {
        public long LevelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Elevation { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
