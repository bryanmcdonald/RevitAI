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
/// Tool that reads data from a Revit schedule view.
/// </summary>
public sealed class ReadScheduleDataTool : IRevitTool
{
    private const int DefaultMaxRows = 200;
    private const int AbsoluteMaxRows = 200;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ReadScheduleDataTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "schedule_name": {
                        "type": "string",
                        "description": "The name of the schedule to read (case-insensitive, partial match supported)."
                    },
                    "max_rows": {
                        "type": "integer",
                        "description": "Maximum number of rows to return (default 200, max 200)."
                    }
                },
                "required": ["schedule_name"],
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

    public string Name => "read_schedule_data";

    public string Description => "Reads data from a Revit schedule view. Returns headers and rows inline — always present the full data to the user in a readable table. Supports case-insensitive partial name matching. Maximum 200 rows.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        // Get schedule_name parameter
        if (!input.TryGetProperty("schedule_name", out var scheduleNameElement))
            return Task.FromResult(ToolResult.Error("Missing required parameter: schedule_name"));

        var scheduleName = scheduleNameElement.GetString();
        if (string.IsNullOrWhiteSpace(scheduleName))
            return Task.FromResult(ToolResult.Error("Parameter 'schedule_name' cannot be empty."));

        // Get optional max_rows
        var maxRows = DefaultMaxRows;
        if (input.TryGetProperty("max_rows", out var maxRowsElement)
            && maxRowsElement.TryGetInt32(out var requestedMaxRows))
        {
            maxRows = Math.Clamp(requestedMaxRows, 1, AbsoluteMaxRows);
        }

        try
        {
            // Find schedule by name (case-insensitive, partial match)
            var schedule = FindSchedule(doc, scheduleName);
            if (schedule == null)
            {
                var availableSchedules = GetAvailableScheduleNames(doc);
                return Task.FromResult(ToolResult.Error(
                    $"Schedule '{scheduleName}' not found. Available schedules: {string.Join(", ", availableSchedules)}"));
            }

            // Build visible column map from field definitions
            // This ensures headers and body columns stay aligned even when fields are hidden
            var (headers, visibleColumnIndexes) = ExtractVisibleColumns(schedule);

            // Extract body rows using only visible column indexes
            var tableData = schedule.GetTableData();
            var bodySection = tableData.GetSectionData(SectionType.Body);
            var totalRows = bodySection.NumberOfRows;
            var rowsToRead = Math.Min(totalRows, maxRows);
            var truncated = totalRows > maxRows;

            var rows = new List<List<string>>();
            for (int row = 0; row < rowsToRead; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rowData = new List<string>();
                foreach (var col in visibleColumnIndexes)
                {
                    rowData.Add(schedule.GetCellText(SectionType.Body, row, col));
                }
                rows.Add(rowData);
            }

            var result = new ReadScheduleResult
            {
                ScheduleName = schedule.Name,
                Headers = headers,
                Rows = rows,
                TotalRows = totalRows,
                ReturnedRows = rowsToRead,
                Truncated = truncated,
                TruncatedMessage = truncated ? $"Showing {rowsToRead} of {totalRows} rows. Use max_rows to adjust." : null
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to read schedule data: {ex.Message}"));
        }
    }

    private static ViewSchedule? FindSchedule(Document doc, string name)
    {
        var schedules = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.Name.StartsWith("<")) // Skip internal schedules
            .ToList();

        // Try exact match first (case-insensitive)
        var exact = schedules.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Try partial match (case-insensitive)
        var partial = schedules.FirstOrDefault(s =>
            s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        return partial;
    }

    /// <summary>
    /// Extracts visible column headers and their corresponding column indexes.
    /// Uses field definitions to correctly handle hidden columns.
    /// </summary>
    private static (List<string> Headers, List<int> ColumnIndexes) ExtractVisibleColumns(ViewSchedule schedule)
    {
        var headers = new List<string>();
        var columnIndexes = new List<int>();
        var fieldOrder = schedule.Definition.GetFieldOrder();

        for (int i = 0; i < fieldOrder.Count; i++)
        {
            var field = schedule.Definition.GetField(fieldOrder[i]);
            if (!field.IsHidden)
            {
                headers.Add(field.GetName());
                columnIndexes.Add(i);
            }
        }

        return (headers, columnIndexes);
    }

    private static List<string> GetAvailableScheduleNames(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.Name.StartsWith("<"))
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToList();
    }

    private sealed class ReadScheduleResult
    {
        public string ScheduleName { get; set; } = string.Empty;
        public List<string> Headers { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public int TotalRows { get; set; }
        public int ReturnedRows { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
    }
}
