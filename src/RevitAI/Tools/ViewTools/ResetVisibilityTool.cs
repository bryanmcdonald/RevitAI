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

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that resets element visibility in the current view, clearing temporary or permanent overrides.
/// </summary>
public sealed class ResetVisibilityTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static ResetVisibilityTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "temporary_only": {
                        "type": "boolean",
                        "description": "If true (default), only resets temporary hide/isolate mode. If false, also clears permanent visibility overrides (requires undo to revert)."
                    }
                },
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

    public string Name => "reset_visibility";

    public string Description =>
        "Resets element visibility in the current view. " +
        "By default only clears temporary hide/isolate mode. " +
        "Set temporary_only=false to also clear permanent visibility overrides.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => true; // All visibility operations need transactions in Revit 2026

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        var doc = uiDoc.Document;
        var activeView = uiDoc.ActiveView;

        // Get optional temporary_only parameter (default true)
        var temporaryOnly = true;
        if (input.TryGetProperty("temporary_only", out var temporaryOnlyElement))
        {
            temporaryOnly = temporaryOnlyElement.GetBoolean();
        }

        try
        {
            var resetTemporary = false;
            var resetPermanent = false;
            var unhiddenCount = 0;

            // Reset temporary hide/isolate mode (transaction handled by framework)
            activeView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            resetTemporary = true;

            if (!temporaryOnly)
            {
                // Find all hidden elements by collecting from document (not view-filtered)
                // and checking IsHidden for each
                var collector = new FilteredElementCollector(doc);
                var allElements = collector
                    .WhereElementIsNotElementType()
                    .WhereElementIsViewIndependent()
                    .ToElements();

                var hiddenIds = new List<ElementId>();
                foreach (var element in allElements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (element.IsHidden(activeView) && element.CanBeHidden(activeView))
                        {
                            hiddenIds.Add(element.Id);
                        }
                    }
                    catch
                    {
                        // Some elements may throw exceptions for IsHidden check - skip them
                    }
                }

                // Also check view-dependent elements
                var viewDepCollector = new FilteredElementCollector(doc, activeView.Id);
                // This collector only returns visible elements, so we can't find hidden ones this way
                // Instead, we need to get all elements that COULD be in the view

                // Get all model elements and check if they're hidden
                var modelCollector = new FilteredElementCollector(doc);
                var modelElements = modelCollector
                    .WhereElementIsNotElementType()
                    .Where(e => !e.ViewSpecific)
                    .ToList();

                foreach (var element in modelElements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (!hiddenIds.Contains(element.Id) &&
                            element.IsHidden(activeView) &&
                            element.CanBeHidden(activeView))
                        {
                            hiddenIds.Add(element.Id);
                        }
                    }
                    catch
                    {
                        // Skip elements that throw exceptions
                    }
                }

                if (hiddenIds.Count > 0)
                {
                    activeView.UnhideElements(hiddenIds);
                    unhiddenCount = hiddenIds.Count;
                    resetPermanent = true;
                }
            }

            var result = new ResetVisibilityResult
            {
                ResetTemporary = resetTemporary,
                ResetPermanent = resetPermanent,
                UnhiddenCount = unhiddenCount,
                Message = temporaryOnly
                    ? "Reset temporary hide/isolate mode."
                    : resetPermanent
                        ? $"Reset temporary mode and unhid {unhiddenCount} permanently hidden element(s)."
                        : "Reset temporary mode. No permanently hidden elements found to unhide."
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromException(ex));
        }
    }

    private sealed class ResetVisibilityResult
    {
        public bool ResetTemporary { get; set; }
        public bool ResetPermanent { get; set; }
        public int UnhiddenCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
