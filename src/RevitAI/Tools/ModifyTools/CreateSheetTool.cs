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
/// Tool that creates a new sheet with an optional title block and placed views.
/// </summary>
public sealed class CreateSheetTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateSheetTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "number": {
                        "type": "string",
                        "description": "Sheet number (e.g., 'A101')."
                    },
                    "name": {
                        "type": "string",
                        "description": "Sheet name (e.g., 'Floor Plan - Level 1')."
                    },
                    "title_block": {
                        "type": "string",
                        "description": "Title block family name or 'Family: Type' name. Use get_available_types with 'TitleBlocks' to see options."
                    },
                    "views_to_place": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "view_id": {
                                    "type": "integer",
                                    "description": "The ID of the view to place on the sheet."
                                },
                                "location": {
                                    "type": "array",
                                    "items": { "type": "number" },
                                    "minItems": 2,
                                    "maxItems": 2,
                                    "description": "Location [x, y] on the sheet in feet from the sheet origin."
                                }
                            },
                            "required": ["view_id"]
                        },
                        "description": "Optional array of views to place on the sheet, each with a view_id and optional location."
                    }
                },
                "required": ["number", "name", "title_block"],
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

    public string Name => "create_sheet";

    public string Description => "Creates a new sheet with a title block and optionally places views on it. Use get_available_types with 'TitleBlocks' to see title block options, and get_view_list to find view IDs.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var number = input.TryGetProperty("number", out var numElem) ? numElem.GetString() ?? "" : "";
        var name = input.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "";
        var viewCount = input.TryGetProperty("views_to_place", out var viewsElem) ? viewsElem.GetArrayLength() : 0;

        if (viewCount > 0)
            return $"Would create sheet {number} '{name}' with {viewCount} view(s).";
        return $"Would create sheet {number} '{name}'.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("number", out var numberElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: number"));

        if (!input.TryGetProperty("name", out var nameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: name"));

        if (!input.TryGetProperty("title_block", out var titleBlockElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: title_block"));

        try
        {
            var sheetNumber = numberElement.GetString();
            if (string.IsNullOrWhiteSpace(sheetNumber))
                return Task.FromResult(ToolResult.Error("number cannot be empty."));

            var sheetName = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(sheetName))
                return Task.FromResult(ToolResult.Error("name cannot be empty."));

            var titleBlockName = titleBlockElement.GetString();
            if (string.IsNullOrWhiteSpace(titleBlockName))
                return Task.FromResult(ToolResult.Error("title_block cannot be empty."));

            // Find title block type
            var titleBlock = ElementLookupHelper.FindTitleBlockType(doc, titleBlockName);
            if (titleBlock == null)
            {
                var available = ElementLookupHelper.GetAvailableTitleBlockNames(doc);
                return Task.FromResult(ToolResult.Error(
                    $"Title block '{titleBlockName}' not found. Available title blocks: {available}"));
            }

            // Activate the title block if needed
            if (!titleBlock.IsActive)
                titleBlock.Activate();

            // Create the sheet
            var sheet = ViewSheet.Create(doc, titleBlock.Id);

            // Set sheet number — may throw if duplicate
            try
            {
                sheet.SheetNumber = sheetNumber;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                doc.Delete(sheet.Id);
                return Task.FromResult(ToolResult.Error(
                    $"Sheet number '{sheetNumber}' is already in use. Please choose a different number."));
            }

            sheet.Name = sheetName;

            // Place views if specified
            var viewportsPlaced = new List<ViewportPlacedInfo>();
            if (input.TryGetProperty("views_to_place", out var viewsElement))
            {
                foreach (var viewEntry in viewsElement.EnumerateArray())
                {
                    if (!viewEntry.TryGetProperty("view_id", out var viewIdElem))
                        continue;

                    var viewId = new ElementId(viewIdElem.GetInt64());
                    var viewElem = doc.GetElement(viewId) as View;
                    if (viewElem == null)
                    {
                        viewportsPlaced.Add(new ViewportPlacedInfo
                        {
                            ViewId = viewIdElem.GetInt64(),
                            Success = false,
                            Error = $"View with ID {viewIdElem.GetInt64()} not found."
                        });
                        continue;
                    }

                    // Check if view can be added to sheet
                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
                    {
                        viewportsPlaced.Add(new ViewportPlacedInfo
                        {
                            ViewId = viewIdElem.GetInt64(),
                            ViewName = viewElem.Name,
                            Success = false,
                            Error = $"View '{viewElem.Name}' cannot be added (may already be on another sheet)."
                        });
                        continue;
                    }

                    // Determine location
                    XYZ location;
                    if (viewEntry.TryGetProperty("location", out var locElem))
                    {
                        var locArray = locElem.EnumerateArray().ToList();
                        if (locArray.Count == 2)
                        {
                            location = new XYZ(locArray[0].GetDouble(), locArray[1].GetDouble(), 0);
                        }
                        else
                        {
                            location = new XYZ(1.0, 1.0, 0); // Default center-ish
                        }
                    }
                    else
                    {
                        location = new XYZ(1.0, 1.0, 0); // Default center-ish
                    }

                    var viewport = Viewport.Create(doc, sheet.Id, viewId, location);
                    viewportsPlaced.Add(new ViewportPlacedInfo
                    {
                        ViewId = viewIdElem.GetInt64(),
                        ViewName = viewElem.Name,
                        ViewportId = viewport.Id.Value,
                        Success = true
                    });
                }
            }

            var result = new CreateSheetResult
            {
                SheetId = sheet.Id.Value,
                Number = sheet.SheetNumber,
                Name = sheet.Name,
                TitleBlock = titleBlockName,
                ViewportsPlaced = viewportsPlaced.Count > 0 ? viewportsPlaced : null,
                Message = viewportsPlaced.Count > 0
                    ? $"Created sheet {sheet.SheetNumber} '{sheet.Name}' with {viewportsPlaced.Count(v => v.Success)} view(s) placed."
                    : $"Created sheet {sheet.SheetNumber} '{sheet.Name}'."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create sheet: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class CreateSheetResult
    {
        public long SheetId { get; set; }
        public string Number { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TitleBlock { get; set; } = string.Empty;
        public List<ViewportPlacedInfo>? ViewportsPlaced { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class ViewportPlacedInfo
    {
        public long ViewId { get; set; }
        public string? ViewName { get; set; }
        public long? ViewportId { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
