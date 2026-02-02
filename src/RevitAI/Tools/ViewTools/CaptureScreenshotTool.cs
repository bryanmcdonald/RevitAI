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
using Autodesk.Revit.UI;
using RevitAI.Models;
using RevitAI.Services;

namespace RevitAI.Tools.ViewTools;

/// <summary>
/// Tool that captures screenshots of the Revit window or active view for visual analysis.
/// Supports both full window capture (with UI) and view-only export (clean graphical views).
/// </summary>
public sealed class CaptureScreenshotTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;

    /// <summary>
    /// Estimated token counts for each resolution.
    /// </summary>
    private static readonly Dictionary<ScreenshotResolution, string> TokenEstimates = new()
    {
        { ScreenshotResolution.Low, "~500-700" },
        { ScreenshotResolution.Medium, "~800-1000" },
        { ScreenshotResolution.High, "~1500-2000" },
        { ScreenshotResolution.Max, "~2500-3500" }
    };

    static CaptureScreenshotTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "include_ui": {
                        "type": "boolean",
                        "description": "Include Revit UI (ribbon, browser, properties). Default true. Set false for cleaner graphical view capture.",
                        "default": true
                    },
                    "resolution": {
                        "type": "string",
                        "enum": ["low", "medium", "high", "max"],
                        "description": "Image resolution. Choose minimum needed: low (overview), medium (identification), high (text), max (fine detail). Default is 'medium'.",
                        "default": "medium"
                    }
                },
                "additionalProperties": false
            }
            """;
        _inputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone();
    }

    public string Name => "capture_screenshot";

    public string Description => """
        Captures a screenshot of the Revit window for visual analysis.

        RESOLUTION GUIDANCE: Choose the minimum resolution needed for your task:
        - 'low' (800px): Layout verification, general overview
        - 'medium' (1280px): Element identification, spatial relationships
        - 'high' (1920px): Reading text, checking details
        - 'max' (2560px): Fine detail analysis, dimension text

        CAPTURE MODES:
        - include_ui: true (default) - Captures full Revit window with ribbon, browser, properties
        - include_ui: false - Clean view-only export (works on floor plans, 3D, sections, elevations, detail views, legends)

        For schedules, sheets, and browser views, use include_ui: true (view-only export not supported).

        IMPORTANT: The tool result includes metadata (resolution, estimated tokens, capture count). You MUST report this information to the user when taking screenshots, e.g., "Screenshot captured at medium resolution (~800-1000 tokens)."
        """;

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public bool RequiresConfirmation => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configService = ConfigurationService.Instance;
        var captureService = ScreenCaptureService.Instance;

        // Check rate limit
        var currentCount = ScreenshotCounter.Instance.GetCount();
        var maxPerResponse = configService.MaxScreenshotsPerResponse;

        if (currentCount >= maxPerResponse)
        {
            return Task.FromResult(ToolResult.Error(
                $"Screenshot limit reached for this response ({currentCount}/{maxPerResponse}). " +
                "Send another message to take more screenshots."));
        }

        // Parse parameters
        var includeUi = true;
        if (input.TryGetProperty("include_ui", out var includeUiProp))
        {
            includeUi = includeUiProp.GetBoolean();
        }

        var resolution = configService.DefaultScreenshotResolution;
        if (input.TryGetProperty("resolution", out var resolutionProp))
        {
            var resolutionStr = resolutionProp.GetString()?.ToLowerInvariant();
            resolution = resolutionStr switch
            {
                "low" => ScreenshotResolution.Low,
                "medium" => ScreenshotResolution.Medium,
                "high" => ScreenshotResolution.High,
                "max" => ScreenshotResolution.Max,
                _ => configService.DefaultScreenshotResolution
            };
        }

        // Check if Revit is ready
        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null)
        {
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));
        }

        CaptureResult captureResult;
        string captureMode;
        bool wasAutoFallback = false;

        if (includeUi)
        {
            // Full window capture
            captureResult = captureService.CaptureRevitWindow(app, resolution);
            captureMode = "full window";
        }
        else
        {
            // Try view-only export
            var activeView = uiDoc.ActiveView;

            if (activeView != null && captureService.CanExportView(activeView))
            {
                captureResult = captureService.CaptureActiveView(app, resolution);
                captureMode = "view-only";
            }
            else
            {
                // Auto-fallback to full window capture for non-exportable views
                captureResult = captureService.CaptureRevitWindow(app, resolution);
                captureMode = $"auto-fallback to full window for {activeView?.ViewType}";
                wasAutoFallback = true;
            }
        }

        if (!captureResult.IsSuccess)
        {
            return Task.FromResult(ToolResult.Error(captureResult.ErrorMessage ?? "Screenshot capture failed."));
        }

        // Increment counter
        ScreenshotCounter.Instance.Increment();
        var newCount = ScreenshotCounter.Instance.GetCount();

        // Build description
        var tokenEstimate = TokenEstimates.GetValueOrDefault(resolution, "unknown");
        var description = wasAutoFallback
            ? $"Screenshot captured ({captureMode}, {resolution.ToString().ToLowerInvariant()} resolution, {tokenEstimate} tokens, {newCount}/{maxPerResponse} this response)"
            : $"Screenshot captured ({captureMode}, {resolution.ToString().ToLowerInvariant()} resolution, {tokenEstimate} tokens, {newCount}/{maxPerResponse} this response)";

        // Return result with image
        var base64 = Convert.ToBase64String(captureResult.ImageBytes!);
        return Task.FromResult(ToolResult.SuccessWithImage(base64, captureResult.MediaType!, description));
    }
}

/// <summary>
/// Thread-safe counter for screenshots taken in the current AI response.
/// Reset when a new user message is received.
/// </summary>
public sealed class ScreenshotCounter
{
    private static readonly Lazy<ScreenshotCounter> _instance = new(() => new ScreenshotCounter());

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static ScreenshotCounter Instance => _instance.Value;

    private int _count;
    private readonly object _lock = new();

    private ScreenshotCounter() { }

    /// <summary>
    /// Gets the current screenshot count.
    /// </summary>
    public int GetCount()
    {
        lock (_lock)
        {
            return _count;
        }
    }

    /// <summary>
    /// Increments the screenshot count.
    /// </summary>
    public void Increment()
    {
        lock (_lock)
        {
            _count++;
        }
    }

    /// <summary>
    /// Resets the counter to zero. Call when a new user message is received.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _count = 0;
        }
    }
}
