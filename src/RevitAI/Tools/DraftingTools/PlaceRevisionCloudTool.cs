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
using RevitAI.Tools.DraftingTools.Helpers;

namespace RevitAI.Tools.DraftingTools;

/// <summary>
/// Tool that places a revision cloud in a view.
/// </summary>
public sealed class PlaceRevisionCloudTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static PlaceRevisionCloudTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "view_id": {
                        "type": "integer",
                        "description": "The view ID to place the revision cloud in. Optional - uses active view if not specified."
                    },
                    "points": {
                        "type": "array",
                        "items": {
                            "type": "array",
                            "items": { "type": "number" },
                            "minItems": 2,
                            "maxItems": 2
                        },
                        "minItems": 3,
                        "description": "Array of [x, y] points in feet defining the revision cloud boundary. Minimum 3 points. Auto-closes if first != last."
                    },
                    "revision_id": {
                        "type": "integer",
                        "description": "The element ID of the revision to associate with. Optional - defaults to the latest revision in the project."
                    }
                },
                "required": ["points"],
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

    public string Name => "place_revision_cloud";

    public string Description =>
        "Places a revision cloud in a view. Revision clouds mark areas that have been revised. " +
        "Requires at least 3 points to define the cloud boundary. Associates with the latest " +
        "revision by default, or specify a revision_id. Use get_revision_list to see available revisions.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true;

    public bool RequiresConfirmation => true;

    public string GetDryRunDescription(JsonElement input)
    {
        var pointCount = 0;
        if (input.TryGetProperty("points", out var pts))
            pointCount = pts.GetArrayLength();
        return $"Would place revision cloud with {pointCount} points.";
    }

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Resolve view using general-purpose resolver
            var (view, viewError) = DraftingHelper.ResolveView(doc, input);
            if (viewError != null) return Task.FromResult(viewError);

            // Reject views that can't host revision clouds
            if (view!.ViewType == ViewType.Schedule || view.ViewType == ViewType.ColumnSchedule ||
                view.ViewType == ViewType.PanelSchedule)
                return Task.FromResult(ToolResult.Error("Revision clouds cannot be placed in schedule views."));

            if (view.ViewType == ViewType.DrawingSheet)
                return Task.FromResult(ToolResult.Error(
                    "Revision clouds cannot be placed directly on sheets. Place them in the views on the sheet instead."));

            // Parse points
            var (points, pointsError) = DraftingHelper.ParsePointArray(input, "points", minPoints: 3);
            if (pointsError != null) return Task.FromResult(pointsError);

            // Build closed curve loop
            var (curveLoop, loopError) = DraftingHelper.BuildClosedCurveLoop(points!);
            if (loopError != null) return Task.FromResult(loopError);

            // Resolve revision
            ElementId revisionId;
            string revisionDescription;

            if (input.TryGetProperty("revision_id", out var revIdElement))
            {
                revisionId = new ElementId(revIdElement.GetInt64());
                var revision = doc.GetElement(revisionId) as Revision;
                if (revision == null)
                    return Task.FromResult(ToolResult.Error($"Revision with ID {revIdElement.GetInt64()} not found."));
                revisionDescription = revision.Description ?? $"Revision #{revision.SequenceNumber}";
            }
            else
            {
                // Default to latest revision
                var allRevisionIds = Revision.GetAllRevisionIds(doc);
                if (allRevisionIds == null || allRevisionIds.Count == 0)
                    return Task.FromResult(ToolResult.Error(
                        "No revisions exist in the project. Create a revision first (Manage > Additional Settings > Sheet Issues/Revisions)."));

                revisionId = allRevisionIds.Last();
                var revision = doc.GetElement(revisionId) as Revision;
                revisionDescription = revision?.Description ?? "Latest revision";
            }

            // Convert CurveLoop to IList<Curve> for the RevisionCloud API
            var curves = curveLoop!.Cast<Curve>().ToList();

            // Create the revision cloud
            var cloud = RevisionCloud.Create(doc, view!, revisionId, curves);

            var result = new PlaceRevisionCloudResult
            {
                ElementIds = new[] { cloud.Id.Value },
                ViewId = view.Id.Value,
                ViewName = view.Name,
                RevisionId = revisionId.Value,
                RevisionDescription = revisionDescription,
                PointCount = points!.Count,
                Message = $"Placed revision cloud with {points.Count} points in '{view.Name}' (revision: {revisionDescription})."
            };

            return Task.FromResult(ToolResult.OkWithElements(
                JsonSerializer.Serialize(result, _jsonOptions), new[] { cloud.Id.Value }));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to place revision cloud: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class PlaceRevisionCloudResult
    {
        public long[] ElementIds { get; set; } = Array.Empty<long>();
        public long ViewId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public long RevisionId { get; set; }
        public string RevisionDescription { get; set; } = string.Empty;
        public int PointCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
