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

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that lists all revisions in the document with their properties.
/// Essential for revision cloud placement (which requires a revision ID).
/// </summary>
public sealed class GetRevisionListTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetRevisionListTool()
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

    public string Name => "get_revision_list";

    public string Description => "Lists all revisions in the document with sequence number, date, description, and issued status. " +
        "Use this before placing revision clouds to find the correct revision ID.";

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
            var revisionIds = Revision.GetAllRevisionIds(doc);

            if (revisionIds.Count == 0)
            {
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(new GetRevisionListResult
                {
                    Revisions = new List<RevisionData>(),
                    Count = 0,
                    Message = "No revisions found in this document. Create a revision in Revit before placing revision clouds."
                }, _jsonOptions)));
            }

            var revisions = revisionIds
                .Select(id => doc.GetElement(id) as Revision)
                .Where(r => r != null)
                .Select(r => new RevisionData
                {
                    Id = r!.Id.Value,
                    SequenceNumber = r.SequenceNumber,
                    Date = string.IsNullOrWhiteSpace(r.RevisionDate) ? null : r.RevisionDate,
                    Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description,
                    Issued = r.Issued,
                    Visibility = r.Visibility.ToString()
                })
                .OrderBy(r => r.SequenceNumber)
                .ToList();

            var result = new GetRevisionListResult
            {
                Revisions = revisions,
                Count = revisions.Count
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get revision list: {ex.Message}"));
        }
    }

    private sealed class RevisionData
    {
        public long Id { get; set; }
        public int SequenceNumber { get; set; }
        public string? Date { get; set; }
        public string? Description { get; set; }
        public bool Issued { get; set; }
        public string Visibility { get; set; } = string.Empty;
    }

    private sealed class GetRevisionListResult
    {
        public List<RevisionData> Revisions { get; set; } = new();
        public int Count { get; set; }
        public string? Message { get; set; }
    }
}
