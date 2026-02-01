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
/// Tool that modifies a parameter value on an element.
/// </summary>
public sealed class ModifyElementParameterTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ModifyElementParameterTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_id": {
                        "type": "integer",
                        "description": "The element ID of the element to modify."
                    },
                    "parameter_name": {
                        "type": "string",
                        "description": "The name of the parameter to modify."
                    },
                    "value": {
                        "description": "The new value for the parameter. Type depends on parameter: string, number, or integer."
                    }
                },
                "required": ["element_id", "parameter_name", "value"],
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

    public string Name => "modify_element_parameter";

    public string Description => "Modifies a parameter value on an element. Supports text, numeric, and integer parameters. For dimensions, provide values in feet.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var elementId = input.TryGetProperty("element_id", out var idElem) ? idElem.GetInt64().ToString() : "unknown";
        var paramName = input.TryGetProperty("parameter_name", out var paramElem) ? paramElem.GetString() ?? "unknown" : "unknown";
        var value = input.TryGetProperty("value", out var valElem) ? valElem.ToString() : "unknown";
        return $"Would set parameter '{paramName}' to '{value}' on element {elementId}.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get element_id parameter
        if (!input.TryGetProperty("element_id", out var elementIdElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_id"));

        // Get parameter_name
        if (!input.TryGetProperty("parameter_name", out var parameterNameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: parameter_name"));

        // Get value
        if (!input.TryGetProperty("value", out var valueElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: value"));

        try
        {
            var elementId = new ElementId(elementIdElement.GetInt64());
            var element = doc.GetElement(elementId);

            if (element == null)
                return Task.FromResult(ToolResult.Error($"Element with ID {elementId.Value} not found."));

            var parameterName = parameterNameElement.GetString();
            if (string.IsNullOrWhiteSpace(parameterName))
                return Task.FromResult(ToolResult.Error("parameter_name cannot be empty."));

            // Find the parameter
            var parameter = element.LookupParameter(parameterName);
            if (parameter == null)
            {
                // List available parameters
                var availableParams = GetWritableParameterNames(element);
                return Task.FromResult(ToolResult.Error(
                    $"Parameter '{parameterName}' not found on element {elementId.Value}. " +
                    $"Available writable parameters: {string.Join(", ", availableParams.Take(20))}" +
                    (availableParams.Count > 20 ? ", ..." : "")));
            }

            // Check if parameter is read-only
            if (parameter.IsReadOnly)
                return Task.FromResult(ToolResult.Error($"Parameter '{parameterName}' is read-only and cannot be modified."));

            // Get old value for result
            var oldValue = GetParameterValueString(parameter, doc);

            // Set the new value based on storage type
            var setResult = SetParameterValue(parameter, valueElement, doc);
            if (!setResult.Success)
                return Task.FromResult(ToolResult.Error(setResult.ErrorMessage!));

            // Get new value for result
            var newValue = GetParameterValueString(parameter, doc);

            var result = new ModifyParameterResult
            {
                ElementId = elementId.Value,
                Category = element.Category?.Name ?? "Unknown",
                ParameterName = parameterName,
                OldValue = oldValue,
                NewValue = newValue,
                Message = $"Changed '{parameterName}' from '{oldValue}' to '{newValue}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static List<string> GetWritableParameterNames(Element element)
    {
        var names = new List<string>();

        foreach (Parameter param in element.Parameters)
        {
            if (!param.IsReadOnly && param.Definition != null)
            {
                names.Add(param.Definition.Name);
            }
        }

        return names.Distinct().OrderBy(n => n).ToList();
    }

    private static string GetParameterValueString(Parameter param, Document doc)
    {
        // Try formatted value first
        var valueString = param.AsValueString();
        if (!string.IsNullOrEmpty(valueString))
            return valueString;

        return param.StorageType switch
        {
            StorageType.String => param.AsString() ?? string.Empty,
            StorageType.Integer => param.AsInteger().ToString(),
            StorageType.Double => param.AsDouble().ToString("F4"),
            StorageType.ElementId => GetElementIdValueString(param.AsElementId(), doc),
            _ => string.Empty
        };
    }

    private static string GetElementIdValueString(ElementId elemId, Document doc)
    {
        if (elemId == ElementId.InvalidElementId)
            return "(none)";

        var elem = doc.GetElement(elemId);
        return elem?.Name ?? $"Element {elemId.Value}";
    }

    private static (bool Success, string? ErrorMessage) SetParameterValue(Parameter param, JsonElement value, Document doc)
    {
        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    var stringValue = value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : value.ToString();
                    param.Set(stringValue);
                    return (true, null);

                case StorageType.Integer:
                    int intValue;
                    if (value.ValueKind == JsonValueKind.Number)
                    {
                        intValue = value.GetInt32();
                    }
                    else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                    {
                        intValue = parsed;
                    }
                    else
                    {
                        return (false, $"Cannot convert '{value}' to integer for parameter storage type.");
                    }
                    param.Set(intValue);
                    return (true, null);

                case StorageType.Double:
                    double doubleValue;
                    if (value.ValueKind == JsonValueKind.Number)
                    {
                        doubleValue = value.GetDouble();
                    }
                    else if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsedDouble))
                    {
                        doubleValue = parsedDouble;
                    }
                    else
                    {
                        return (false, $"Cannot convert '{value}' to number for parameter storage type.");
                    }
                    param.Set(doubleValue);
                    return (true, null);

                case StorageType.ElementId:
                    long elementIdValue;
                    if (value.ValueKind == JsonValueKind.Number)
                    {
                        elementIdValue = value.GetInt64();
                    }
                    else if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsedId))
                    {
                        elementIdValue = parsedId;
                    }
                    else
                    {
                        return (false, $"Cannot convert '{value}' to element ID for parameter storage type.");
                    }

                    var elemId = new ElementId(elementIdValue);
                    // Validate element exists if not InvalidElementId
                    if (elemId != ElementId.InvalidElementId && doc.GetElement(elemId) == null)
                    {
                        return (false, $"Element with ID {elementIdValue} not found.");
                    }
                    param.Set(elemId);
                    return (true, null);

                default:
                    return (false, $"Unsupported parameter storage type: {param.StorageType}");
            }
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return (false, $"Invalid value for parameter: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to set parameter value: {ex.Message}");
        }
    }

    private sealed class ModifyParameterResult
    {
        public long ElementId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string ParameterName { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
