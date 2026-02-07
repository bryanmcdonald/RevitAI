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
/// Tool that creates a Model Group from selected elements.
/// </summary>
public sealed class CreateGroupTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static CreateGroupTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "element_ids": {
                        "type": "array",
                        "items": { "type": "integer" },
                        "description": "Array of element IDs to group (minimum 2)."
                    },
                    "name": {
                        "type": "string",
                        "description": "Optional name for the group type. If omitted, Revit assigns a default name."
                    }
                },
                "required": ["element_ids"],
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

    public string Name => "create_group";

    public string Description => "Creates a Model Group from the specified elements (minimum 2). Optionally provide a name for the group type.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var count = input.TryGetProperty("element_ids", out var ids) ? ids.GetArrayLength() : 0;
        var hasName = input.TryGetProperty("name", out var nameElem) && nameElem.ValueKind == JsonValueKind.String;
        var nameText = hasName ? $" named '{nameElem.GetString()}'" : "";
        return $"Would create a group{nameText} from {count} element(s).";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        if (!input.TryGetProperty("element_ids", out var elementIdsElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: element_ids"));

        try
        {
            // Parse element IDs
            var requestedIds = new List<long>();
            foreach (var idElement in elementIdsElement.EnumerateArray())
                requestedIds.Add(idElement.GetInt64());

            if (requestedIds.Count < 2)
                return Task.FromResult(ToolResult.Error("At least 2 elements are required to create a group."));

            // Validate element IDs
            var validIds = new List<ElementId>();
            var invalidIds = new List<long>();

            foreach (var id in requestedIds)
            {
                var elementId = new ElementId(id);
                var element = doc.GetElement(elementId);
                if (element != null)
                    validIds.Add(elementId);
                else
                    invalidIds.Add(id);
            }

            if (validIds.Count < 2)
                return Task.FromResult(ToolResult.Error($"Need at least 2 valid elements. Found {validIds.Count} valid, {invalidIds.Count} invalid: {string.Join(", ", invalidIds)}"));

            // Create the group
            var group = doc.Create.NewGroup(validIds);

            // Rename if name provided
            string? requestedName = null;
            if (input.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                requestedName = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(requestedName))
                {
                    var groupType = doc.GetElement(group.GroupType.Id) as GroupType;
                    if (groupType != null)
                    {
                        groupType.Name = requestedName;
                    }
                }
            }

            var result = new CreateGroupResult
            {
                GroupId = group.Id.Value,
                GroupTypeId = group.GroupType.Id.Value,
                GroupName = group.GroupType.Name,
                MemberCount = validIds.Count,
                InvalidIds = invalidIds.Count > 0 ? invalidIds : null,
                Message = $"Created group '{group.GroupType.Name}' (ID: {group.Id.Value}) with {validIds.Count} member(s)."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class CreateGroupResult
    {
        public long GroupId { get; set; }
        public long GroupTypeId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public List<long>? InvalidIds { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
