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
/// Tool that returns information about currently selected elements.
/// </summary>
public sealed class GetSelectedElementsTool : IRevitTool
{
    private const int MaxElements = 50;
    private const int MaxParametersPerElement = 10;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetSelectedElementsTool()
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

    public string Name => "get_selected_elements";

    public string Description => "Returns information about currently selected elements including ID, category, family, type, level, and key parameters. Maximum 50 elements returned.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        var doc = uiDoc?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            var selectedIds = uiDoc.Selection.GetElementIds();
            var totalCount = selectedIds.Count;

            if (totalCount == 0)
            {
                var emptyResult = new GetSelectedElementsResult
                {
                    Elements = new List<SelectedElementData>(),
                    Count = 0,
                    Truncated = false
                };
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(emptyResult, _jsonOptions)));
            }

            var elements = new List<SelectedElementData>();
            var truncated = totalCount > MaxElements;
            var processCount = Math.Min(totalCount, MaxElements);

            foreach (var id in selectedIds.Take(processCount))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var elem = doc.GetElement(id);
                    if (elem == null)
                        continue;

                    var data = ExtractElementData(elem, doc);
                    if (data != null)
                        elements.Add(data);
                }
                catch
                {
                    // Skip elements that throw exceptions
                }
            }

            var result = new GetSelectedElementsResult
            {
                Elements = elements,
                Count = totalCount,
                Truncated = truncated,
                TruncatedMessage = truncated ? $"Showing {MaxElements} of {totalCount} selected elements." : null
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get selected elements: {ex.Message}"));
        }
    }

    private static SelectedElementData? ExtractElementData(Element elem, Document doc)
    {
        var data = new SelectedElementData
        {
            Id = elem.Id.Value,
            Category = elem.Category?.Name ?? "Unknown"
        };

        // Get family and type names
        if (elem is FamilyInstance familyInstance)
        {
            data.Family = familyInstance.Symbol?.Family?.Name;
            data.Type = familyInstance.Symbol?.Name;
        }
        else if (elem.GetTypeId() != ElementId.InvalidElementId)
        {
            var elemType = doc.GetElement(elem.GetTypeId());
            if (elemType != null)
            {
                data.Type = elemType.Name;
                var familyNameParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                data.Family = familyNameParam?.AsString();
            }
        }

        // Full type name
        data.FullTypeName = string.IsNullOrEmpty(data.Family)
            ? data.Type ?? elem.Name ?? "Unknown"
            : $"{data.Family}: {data.Type}";

        // Get level
        var levelId = elem.LevelId;
        if (levelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(levelId) as Level;
            data.Level = level?.Name;
            data.LevelId = level?.Id.Value;
        }
        else
        {
            // Try to get level from parameter
            var levelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)
                          ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                          ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (levelParam != null && levelParam.HasValue)
            {
                var level = doc.GetElement(levelParam.AsElementId()) as Level;
                data.Level = level?.Name;
                data.LevelId = level?.Id.Value;
            }
        }

        // Get location
        data.Location = ExtractLocation(elem);

        // Get key parameters
        data.KeyParameters = ExtractKeyParameters(elem, doc);

        return data;
    }

    private static LocationData? ExtractLocation(Element elem)
    {
        var location = elem.Location;

        if (location is LocationPoint locationPoint)
        {
            var pt = locationPoint.Point;
            return new LocationData
            {
                Type = "point",
                X = Math.Round(pt.X, 4),
                Y = Math.Round(pt.Y, 4),
                Z = Math.Round(pt.Z, 4)
            };
        }
        else if (location is LocationCurve locationCurve)
        {
            var curve = locationCurve.Curve;
            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);
            return new LocationData
            {
                Type = "curve",
                StartX = Math.Round(start.X, 4),
                StartY = Math.Round(start.Y, 4),
                StartZ = Math.Round(start.Z, 4),
                EndX = Math.Round(end.X, 4),
                EndY = Math.Round(end.Y, 4),
                EndZ = Math.Round(end.Z, 4)
            };
        }

        return null;
    }

    private static Dictionary<string, string>? ExtractKeyParameters(Element elem, Document doc)
    {
        var priorityParams = new[]
        {
            BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
            BuiltInParameter.ALL_MODEL_MARK,
            BuiltInParameter.CURVE_ELEM_LENGTH,
            BuiltInParameter.WALL_USER_HEIGHT_PARAM,
            BuiltInParameter.INSTANCE_LENGTH_PARAM,
            BuiltInParameter.FAMILY_WIDTH_PARAM,
            BuiltInParameter.FAMILY_HEIGHT_PARAM,
            BuiltInParameter.HOST_AREA_COMPUTED,
            BuiltInParameter.HOST_VOLUME_COMPUTED,
            BuiltInParameter.DOOR_WIDTH,
            BuiltInParameter.DOOR_HEIGHT
        };

        var parameters = new Dictionary<string, string>();

        foreach (var builtIn in priorityParams)
        {
            if (parameters.Count >= MaxParametersPerElement)
                break;

            var param = elem.get_Parameter(builtIn);
            if (param != null && param.HasValue)
            {
                var value = GetParameterValue(param, doc);
                if (!string.IsNullOrEmpty(value))
                {
                    parameters[param.Definition.Name] = value;
                }
            }
        }

        return parameters.Count > 0 ? parameters : null;
    }

    private static string GetParameterValue(Parameter param, Document doc)
    {
        // Use value string if available (includes units)
        var valueString = param.AsValueString();
        if (!string.IsNullOrEmpty(valueString))
            return valueString;

        return param.StorageType switch
        {
            StorageType.String => param.AsString() ?? string.Empty,
            StorageType.Integer => param.AsInteger().ToString(),
            StorageType.Double => param.AsDouble().ToString("F3"),
            StorageType.ElementId => GetElementIdValue(param.AsElementId(), doc),
            _ => string.Empty
        };
    }

    private static string GetElementIdValue(ElementId elemId, Document doc)
    {
        if (elemId == ElementId.InvalidElementId)
            return string.Empty;

        var elem = doc.GetElement(elemId);
        return elem?.Name ?? $"Element {elemId.Value}";
    }

    private sealed class LocationData
    {
        public string Type { get; set; } = string.Empty;
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public double? StartX { get; set; }
        public double? StartY { get; set; }
        public double? StartZ { get; set; }
        public double? EndX { get; set; }
        public double? EndY { get; set; }
        public double? EndZ { get; set; }
    }

    private sealed class SelectedElementData
    {
        public long Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Family { get; set; }
        public string? Type { get; set; }
        public string FullTypeName { get; set; } = string.Empty;
        public string? Level { get; set; }
        public long? LevelId { get; set; }
        public LocationData? Location { get; set; }
        public Dictionary<string, string>? KeyParameters { get; set; }
    }

    private sealed class GetSelectedElementsResult
    {
        public List<SelectedElementData> Elements { get; set; } = new();
        public int Count { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
    }
}
