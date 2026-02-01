using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Tool that returns information about the currently active view.
/// </summary>
public sealed class GetViewInfoTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetViewInfoTool()
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

    public string Name => "get_view_info";

    public string Description => "Returns information about the currently active view including name, type, scale, associated level, detail level, and phase. Use this to understand what the user is currently looking at.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        var doc = uiDoc?.Document;

        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var view = uiDoc.ActiveView;
        if (view == null)
            return Task.FromResult(ToolResult.Error("No active view."));

        try
        {
            var result = new ViewInfoResult
            {
                Id = view.Id.Value,
                Name = view.Name,
                ViewType = view.ViewType.ToString(),
                Scale = view.Scale,
                ScaleFormatted = FormatScale(view.Scale),
                DetailLevel = view.DetailLevel.ToString()
            };

            // Get associated level for plan views
            if (view is ViewPlan viewPlan && viewPlan.GenLevel != null)
            {
                result.AssociatedLevel = viewPlan.GenLevel.Name;
                result.AssociatedLevelId = viewPlan.GenLevel.Id.Value;
            }

            // Get phase
            var phaseParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
            if (phaseParam != null && phaseParam.HasValue)
            {
                var phaseId = phaseParam.AsElementId();
                if (phaseId != ElementId.InvalidElementId)
                {
                    var phase = doc.GetElement(phaseId) as Phase;
                    if (phase != null)
                    {
                        result.Phase = phase.Name;
                        result.PhaseId = phase.Id.Value;
                    }
                }
            }

            // Get view family type name
            var viewTypeId = view.GetTypeId();
            if (viewTypeId != ElementId.InvalidElementId)
            {
                var viewFamilyType = doc.GetElement(viewTypeId);
                if (viewFamilyType != null)
                {
                    result.ViewFamilyType = viewFamilyType.Name;
                }
            }

            // Additional view properties
            result.IsTemplate = view.IsTemplate;
            result.CanBePrinted = view.CanBePrinted;

            // Get discipline if available
            var disciplineParam = view.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
            if (disciplineParam != null && disciplineParam.HasValue)
            {
                result.Discipline = disciplineParam.AsValueString();
            }

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get view info: {ex.Message}"));
        }
    }

    private static string FormatScale(int scale)
    {
        return scale switch
        {
            1 => "1:1 (Full)",
            12 => "1\" = 1'-0\"",
            24 => "1/2\" = 1'-0\"",
            48 => "1/4\" = 1'-0\"",
            96 => "1/8\" = 1'-0\"",
            192 => "1/16\" = 1'-0\"",
            _ => $"1:{scale}"
        };
    }

    private sealed class ViewInfoResult
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public int Scale { get; set; }
        public string ScaleFormatted { get; set; } = string.Empty;
        public string? AssociatedLevel { get; set; }
        public long? AssociatedLevelId { get; set; }
        public string DetailLevel { get; set; } = string.Empty;
        public string? Phase { get; set; }
        public long? PhaseId { get; set; }
        public string? ViewFamilyType { get; set; }
        public bool IsTemplate { get; set; }
        public bool CanBePrinted { get; set; }
        public string? Discipline { get; set; }
    }
}
