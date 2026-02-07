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
/// Tool that places a text note in a view.
/// </summary>
public sealed class PlaceTextNoteTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceTextNoteTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the text note in. Optional - uses active view if not specified."
                    },
                    "location": {
                        "type": "array",
                        "items": { "type": "number" },
                        "minItems": 2,
                        "maxItems": 3,
                        "description": "Location [x, y] or [x, y, z] in feet."
                    },
                    "text": {
                        "type": "string",
                        "description": "The text content of the note."
                    },
                    "text_note_type": {
                        "type": "string",
                        "description": "Name of the text note type. Optional - uses default if not specified."
                    },
                    "font_size": {
                        "type": "number",
                        "description": "Font size in points (e.g., 10, 12, 24). Optional - if specified, a custom text type is created with this size."
                    },
                    "horizontal_alignment": {
                        "type": "string",
                        "enum": ["left", "center", "right"],
                        "description": "Text horizontal alignment. Optional - defaults to left."
                    }
                },
                "required": ["location", "text"],
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

    public string Name => "place_text_note";

    public string Description => "Places a text note in a view. Coordinates are in feet. Text notes are view-specific annotation elements.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var text = input.TryGetProperty("text", out var textElem) ? textElem.GetString() ?? "" : "";
        var preview = text.Length > 40 ? text[..40] + "..." : text;
        return $"Would place text note \"{preview}\".";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("location", out var locationElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: location"));

        if (!input.TryGetProperty("text", out var textElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: text"));

        try
        {
            var text = textElement.GetString();
            if (string.IsNullOrEmpty(text))
                return Task.FromResult(ToolResult.Error("text cannot be empty."));

            // Parse location
            var locArray = locationElement.EnumerateArray().ToList();
            if (locArray.Count < 2 || locArray.Count > 3)
                return Task.FromResult(ToolResult.Error("location must be an array of 2 or 3 numbers [x, y] or [x, y, z]."));
            var x = locArray[0].GetDouble();
            var y = locArray[1].GetDouble();
            var z = locArray.Count == 3 ? locArray[2].GetDouble() : 0;
            var position = new XYZ(x, y, z);

            // Resolve view
            View? view = null;
            if (input.TryGetProperty("view_id", out var viewIdElement))
            {
                var viewId = new ElementId(viewIdElement.GetInt64());
                view = doc.GetElement(viewId) as View;
                if (view == null)
                    return Task.FromResult(ToolResult.Error($"View with ID {viewIdElement.GetInt64()} not found."));
            }
            else
            {
                view = doc.ActiveView;
            }

            if (view == null)
                return Task.FromResult(ToolResult.Error("No active view available."));

            // Text notes cannot be placed in 3D views
            if (view.ViewType == ViewType.ThreeD)
                return Task.FromResult(ToolResult.Error("Text notes cannot be placed in 3D views. Switch to a plan, section, elevation, or drafting view."));

            // Find or create text note type
            TextNoteType? textNoteType = null;
            string? typeName = null;

            if (input.TryGetProperty("text_note_type", out var typeElement))
            {
                typeName = typeElement.GetString();
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    textNoteType = ElementLookupHelper.FindTextNoteType(doc, typeName);
                    if (textNoteType == null)
                    {
                        var available = ElementLookupHelper.GetAvailableTextNoteTypeNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Text note type '{typeName}' not found. Available types: {available}"));
                    }
                }
            }

            // If no type specified, get default
            textNoteType ??= GetDefaultTextNoteType(doc);
            if (textNoteType == null)
                return Task.FromResult(ToolResult.Error("No text note types available in the document."));

            // Handle custom font size — duplicate type with new size
            if (input.TryGetProperty("font_size", out var fontSizeElement))
            {
                var fontSizePoints = fontSizeElement.GetDouble();
                if (fontSizePoints <= 0)
                    return Task.FromResult(ToolResult.Error("font_size must be greater than 0."));

                // Convert points to fractional inches (1 point = 1/72 inch, but Revit TEXT_SIZE is in fractional feet)
                // Actually Revit TEXT_SIZE parameter stores the value in feet (internal units)
                var fontSizeFeet = fontSizePoints / 72.0 / 12.0; // points -> inches -> feet

                // Check if a type with this size already exists from a previous call
                var customTypeName = $"{textNoteType.Name} ({fontSizePoints:F0}pt)";
                var existingCustom = ElementLookupHelper.FindTextNoteType(doc, customTypeName);
                if (existingCustom != null)
                {
                    textNoteType = existingCustom;
                }
                else
                {
                    // Duplicate the base type and set the size
                    var newType = textNoteType.Duplicate(customTypeName) as TextNoteType;
                    if (newType != null)
                    {
                        var sizeParam = newType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        sizeParam?.Set(fontSizeFeet);
                        textNoteType = newType;
                    }
                }
            }

            // Create the text note
            var textNote = TextNote.Create(doc, view.Id, position, text, textNoteType.Id);

            // Set horizontal alignment if specified
            if (input.TryGetProperty("horizontal_alignment", out var alignElement))
            {
                var alignment = alignElement.GetString()?.ToLowerInvariant();
                if (alignment != null)
                {
                    var hAlign = alignment switch
                    {
                        "center" => HorizontalTextAlignment.Center,
                        "right" => HorizontalTextAlignment.Right,
                        _ => HorizontalTextAlignment.Left
                    };
                    textNote.HorizontalAlignment = hAlign;
                }
            }

            var result = new PlaceTextNoteResult
            {
                TextNoteId = textNote.Id.Value,
                ViewId = view.Id.Value,
                ViewName = view.Name,
                Text = text,
                TypeName = textNoteType.Name,
                Location = new[] { x, y },
                Message = $"Created text note in '{view.Name}' using type '{textNoteType.Name}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create text note: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private static TextNoteType? GetDefaultTextNoteType(Document doc)
    {
        // Try to get the document's default text note type first
        var defaultTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (defaultTypeId != ElementId.InvalidElementId)
        {
            var defaultType = doc.GetElement(defaultTypeId) as TextNoteType;
            if (defaultType != null)
                return defaultType;
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault();
    }

    private sealed class PlaceTextNoteResult
    {
        public long TextNoteId { get; set; }
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public double[] Location { get; set; } = Array.Empty<double>();
        public string Message { get; set; } = string.Empty;
    }
}
