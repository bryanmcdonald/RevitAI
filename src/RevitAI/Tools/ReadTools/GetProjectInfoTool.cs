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
/// Tool that returns project information and metadata.
/// </summary>
public sealed class GetProjectInfoTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetProjectInfoTool()
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

    public string Name => "get_project_info";

    public string Description => "Returns project information including name, number, client, address, file path, and whether it is workshared. Use this to understand the project context.";

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
            var projectInfo = doc.ProjectInformation;
            var result = new ProjectInfoResult
            {
                FilePath = doc.PathName,
                IsWorkshared = doc.IsWorkshared
            };

            if (projectInfo != null)
            {
                result.Name = projectInfo.Name;
                result.Number = projectInfo.Number;
                result.Client = projectInfo.ClientName;
                result.Address = projectInfo.Address;
                result.Status = projectInfo.Status;
                result.IssueDate = projectInfo.IssueDate;
                result.Author = projectInfo.Author;
                result.BuildingName = projectInfo.BuildingName;
                result.OrganizationName = projectInfo.OrganizationName;
                result.OrganizationDescription = projectInfo.OrganizationDescription;
            }

            // Add some useful document stats
            result.Title = doc.Title;

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get project info: {ex.Message}"));
        }
    }

    private sealed class ProjectInfoResult
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Client { get; set; }
        public string? Address { get; set; }
        public string? Status { get; set; }
        public string? IssueDate { get; set; }
        public string? Author { get; set; }
        public string? BuildingName { get; set; }
        public string? OrganizationName { get; set; }
        public string? OrganizationDescription { get; set; }
        public string? Title { get; set; }
        public string? FilePath { get; set; }
        public bool IsWorkshared { get; set; }
    }
}
