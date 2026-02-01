using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Tool that returns model warnings from the Revit document.
/// </summary>
public sealed class GetWarningsTool : IRevitTool
{
    private const int MaxWarnings = 100;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetWarningsTool()
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

    public string Name => "get_warnings";

    public string Description => "Returns model warnings from the Revit document with descriptions and affected element IDs. Maximum 100 warnings returned. Use this to identify model quality issues.";

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
            var warnings = doc.GetWarnings();
            var totalCount = warnings.Count;

            if (totalCount == 0)
            {
                var emptyResult = new GetWarningsResult
                {
                    Warnings = new List<WarningData>(),
                    Count = 0,
                    Truncated = false,
                    Message = "No warnings found in the document."
                };
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(emptyResult, _jsonOptions)));
            }

            var warningList = new List<WarningData>();
            var truncated = totalCount > MaxWarnings;
            var processCount = Math.Min(totalCount, MaxWarnings);

            foreach (var warning in warnings.Take(processCount))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var data = new WarningData
                    {
                        Description = warning.GetDescriptionText(),
                        Severity = warning.GetSeverity().ToString(),
                        FailingElements = warning.GetFailingElements()
                            .Select(id => id.Value)
                            .ToList(),
                        AdditionalElements = warning.GetAdditionalElements()
                            .Select(id => id.Value)
                            .ToList()
                    };

                    // Get element info for failing elements (limited to first 5)
                    if (data.FailingElements.Count > 0)
                    {
                        data.FailingElementInfo = new List<ElementBriefInfo>();
                        foreach (var elemId in data.FailingElements.Take(5))
                        {
                            var elem = doc.GetElement(new ElementId(elemId));
                            if (elem != null)
                            {
                                data.FailingElementInfo.Add(new ElementBriefInfo
                                {
                                    Id = elemId,
                                    Category = elem.Category?.Name ?? "Unknown",
                                    Name = elem.Name
                                });
                            }
                        }
                    }

                    warningList.Add(data);
                }
                catch
                {
                    // Skip warnings that throw exceptions
                }
            }

            // Group warnings by description for summary
            var groupedWarnings = warningList
                .GroupBy(w => w.Description)
                .Select(g => new WarningGroupSummary
                {
                    Description = g.Key ?? "Unknown",
                    Count = g.Count()
                })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToList();

            var result = new GetWarningsResult
            {
                Warnings = warningList,
                Count = totalCount,
                Truncated = truncated,
                TruncatedMessage = truncated ? $"Showing {MaxWarnings} of {totalCount} warnings." : null,
                Summary = groupedWarnings
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get warnings: {ex.Message}"));
        }
    }

    private sealed class ElementBriefInfo
    {
        public long Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Name { get; set; }
    }

    private sealed class WarningData
    {
        public string? Description { get; set; }
        public string Severity { get; set; } = string.Empty;
        public List<long> FailingElements { get; set; } = new();
        public List<long> AdditionalElements { get; set; } = new();
        public List<ElementBriefInfo>? FailingElementInfo { get; set; }
    }

    private sealed class WarningGroupSummary
    {
        public string Description { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class GetWarningsResult
    {
        public List<WarningData> Warnings { get; set; } = new();
        public int Count { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
        public string? Message { get; set; }
        public List<WarningGroupSummary>? Summary { get; set; }
    }
}
