using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Tool that returns detailed properties for a specific element.
/// </summary>
public sealed class GetElementPropertiesTool : IRevitTool
{
    private const int MaxParameters = 50;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    private static readonly HashSet<string> InternalParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ELEM_FAMILY_PARAM",
        "ELEM_TYPE_PARAM",
        "SYMBOL_ID_PARAM",
        "ELEM_FAMILY_AND_TYPE_PARAM"
    };

    static GetElementPropertiesTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_id": {
                        "type": "integer",
                        "description": "The element ID to get properties for"
                    },
                    "parameter_names": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "Optional list of specific parameter names to retrieve. If not specified, returns all parameters."
                    }
                },
                "required": ["element_id"],
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

    public string Name => "get_element_properties";

    public string Description => "Returns detailed properties for a specific element by ID, including instance and type parameters. Optionally filter to specific parameter names.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get element_id parameter
        if (!input.TryGetProperty("element_id", out var elementIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_id"));

        long elementIdValue;
        if (elementIdElement.ValueKind == JsonValueKind.Number)
        {
            elementIdValue = elementIdElement.GetInt64();
        }
        else if (elementIdElement.ValueKind == JsonValueKind.String && long.TryParse(elementIdElement.GetString(), out var parsed))
        {
            elementIdValue = parsed;
        }
        else
        {
            return Task.FromResult(ToolResult.Error("Parameter 'element_id' must be a valid integer"));
        }

        // Validate element ID is positive (negative IDs are invalid, 0 is reserved)
        if (elementIdValue <= 0)
        {
            return Task.FromResult(ToolResult.Error($"Invalid element ID: {elementIdValue}. Element IDs must be positive integers."));
        }

        // Get optional parameter_names filter
        HashSet<string>? parameterFilter = null;
        if (input.TryGetProperty("parameter_names", out var paramNamesElement) && paramNamesElement.ValueKind == JsonValueKind.Array)
        {
            parameterFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in paramNamesElement.EnumerateArray())
            {
                var name = item.GetString();
                if (!string.IsNullOrEmpty(name))
                    parameterFilter.Add(name);
            }
        }

        try
        {
            var elementId = new ElementId(elementIdValue);
            var element = doc.GetElement(elementId);

            if (element == null)
                return Task.FromResult(ToolResult.Error($"Element with ID {elementIdValue} not found."));

            var result = ExtractElementProperties(element, doc, parameterFilter);
            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get element properties: {ex.Message}"));
        }
    }

    private static ElementPropertiesResult ExtractElementProperties(Element element, Document doc, HashSet<string>? parameterFilter)
    {
        var result = new ElementPropertiesResult
        {
            ElementId = element.Id.Value,
            Category = element.Category?.Name ?? "Unknown"
        };

        // Get family and type info
        if (element is FamilyInstance familyInstance)
        {
            result.Family = familyInstance.Symbol?.Family?.Name;
            result.Type = familyInstance.Symbol?.Name;
            result.IsFamilyInstance = true;
        }
        else if (element.GetTypeId() != ElementId.InvalidElementId)
        {
            var elemType = doc.GetElement(element.GetTypeId());
            if (elemType != null)
            {
                result.Type = elemType.Name;
                var familyParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                result.Family = familyParam?.AsString();
            }
        }

        // Get level
        var levelId = element.LevelId;
        if (levelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(levelId) as Level;
            result.Level = level?.Name;
            result.LevelId = level?.Id.Value;
        }

        // Get location
        result.Location = ExtractLocation(element);

        // Get bounding box info
        var bbox = element.get_BoundingBox(null);
        if (bbox != null)
        {
            result.BoundingBox = new BoundingBoxData
            {
                MinX = Math.Round(bbox.Min.X, 4),
                MinY = Math.Round(bbox.Min.Y, 4),
                MinZ = Math.Round(bbox.Min.Z, 4),
                MaxX = Math.Round(bbox.Max.X, 4),
                MaxY = Math.Round(bbox.Max.Y, 4),
                MaxZ = Math.Round(bbox.Max.Z, 4)
            };
        }

        // Extract instance parameters
        result.InstanceParameters = ExtractParameters(element, doc, parameterFilter, true);

        // Extract type parameters
        var typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var elemType = doc.GetElement(typeId);
            if (elemType != null)
            {
                result.TypeId = typeId.Value;
                result.TypeParameters = ExtractParameters(elemType, doc, parameterFilter, false);
            }
        }

        return result;
    }

    private static List<ParameterData> ExtractParameters(Element element, Document doc, HashSet<string>? filter, bool isInstance)
    {
        var parameters = new List<ParameterData>();

        foreach (Parameter param in element.Parameters)
        {
            if (parameters.Count >= MaxParameters)
                break;

            if (!param.HasValue)
                continue;

            var name = param.Definition.Name;

            // Skip internal parameters
            if (InternalParameterNames.Contains(name))
                continue;

            // Apply filter if specified
            if (filter != null && !filter.Contains(name))
                continue;

            var data = new ParameterData
            {
                Name = name,
                Value = GetParameterValue(param, doc),
                StorageType = param.StorageType.ToString(),
                IsReadOnly = param.IsReadOnly,
                IsShared = param.IsShared
            };

            // Get parameter group
            try
            {
                data.Group = param.Definition.GetGroupTypeId()?.TypeId ?? "Unknown";
            }
            catch
            {
                data.Group = "Unknown";
            }

            parameters.Add(data);
        }

        // Sort parameters by name
        return parameters.OrderBy(p => p.Name).ToList();
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
            StorageType.Double => param.AsDouble().ToString("F4"),
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
                Z = Math.Round(pt.Z, 4),
                Rotation = locationPoint.Rotation
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
                EndZ = Math.Round(end.Z, 4),
                Length = curve.Length
            };
        }

        return null;
    }

    private sealed class LocationData
    {
        public string Type { get; set; } = string.Empty;
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public double? Rotation { get; set; }
        public double? StartX { get; set; }
        public double? StartY { get; set; }
        public double? StartZ { get; set; }
        public double? EndX { get; set; }
        public double? EndY { get; set; }
        public double? EndZ { get; set; }
        public double? Length { get; set; }
    }

    private sealed class BoundingBoxData
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
    }

    private sealed class ParameterData
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string StorageType { get; set; } = string.Empty;
        public bool IsReadOnly { get; set; }
        public bool IsShared { get; set; }
        public string? Group { get; set; }
    }

    private sealed class ElementPropertiesResult
    {
        public long ElementId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Family { get; set; }
        public string? Type { get; set; }
        public bool IsFamilyInstance { get; set; }
        public string? Level { get; set; }
        public long? LevelId { get; set; }
        public long? TypeId { get; set; }
        public LocationData? Location { get; set; }
        public BoundingBoxData? BoundingBox { get; set; }
        public List<ParameterData> InstanceParameters { get; set; } = new();
        public List<ParameterData>? TypeParameters { get; set; }
    }
}
