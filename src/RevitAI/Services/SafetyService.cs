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

using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows;
using RevitAI.Tools;
using RevitAI.UI;

namespace RevitAI.Services;

/// <summary>
/// Centralized service for handling safety confirmations before tool execution.
/// Tracks session-level "don't ask again" state per tool.
/// </summary>
public sealed class SafetyService
{
    private static SafetyService? _instance;
    private static readonly object _lock = new();

    private readonly ConfigurationService _configService;
    private readonly ConcurrentDictionary<string, bool> _sessionSkips = new();

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static SafetyService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new SafetyService(ConfigurationService.Instance);
                }
            }
            return _instance;
        }
    }

    private SafetyService(ConfigurationService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// Checks if confirmation is needed and shows dialog if required.
    /// Must be called on the WPF UI thread.
    /// </summary>
    /// <param name="tool">The tool to check.</param>
    /// <param name="input">The tool input parameters.</param>
    /// <returns>True if the operation should proceed, false if denied.</returns>
    public bool CheckAndConfirm(IRevitTool tool, JsonElement input)
    {
        // Skip if confirmations are globally disabled
        if (_configService.SkipConfirmations)
        {
            return true;
        }

        // Skip if tool doesn't require confirmation
        if (!tool.RequiresConfirmation)
        {
            return true;
        }

        // Skip if session skip is active for this tool
        if (_sessionSkips.TryGetValue(tool.Name, out var skip) && skip)
        {
            return true;
        }

        // Show confirmation dialog
        var description = GetToolDescription(tool, input);
        var dialog = new ConfirmationDialog();
        dialog.Configure(tool.Name, description);

        var result = dialog.ShowDialog();

        // Record "don't ask again" choice if allowed
        if (dialog.Allowed && dialog.DontAskAgain)
        {
            _sessionSkips[tool.Name] = true;
        }

        return dialog.Allowed;
    }

    /// <summary>
    /// Checks if confirmation is needed for a batch of tools.
    /// Shows a single dialog listing all destructive tools in the batch.
    /// Must be called on the WPF UI thread.
    /// </summary>
    /// <param name="toolsWithInputs">List of (tool, input) pairs to check.</param>
    /// <returns>True if the batch should proceed, false if denied.</returns>
    public bool CheckAndConfirmBatch(IEnumerable<(IRevitTool Tool, JsonElement Input)> toolsWithInputs)
    {
        // Skip if confirmations are globally disabled
        if (_configService.SkipConfirmations)
        {
            return true;
        }

        // Collect tools that require confirmation and aren't session-skipped
        var toolsNeedingConfirmation = toolsWithInputs
            .Where(t => t.Tool.RequiresConfirmation)
            .Where(t => !(_sessionSkips.TryGetValue(t.Tool.Name, out var skip) && skip))
            .ToList();

        if (toolsNeedingConfirmation.Count == 0)
        {
            return true;
        }

        // Build batch description
        var descriptions = toolsNeedingConfirmation
            .Select(t => $"• {GetToolDescription(t.Tool, t.Input)}")
            .ToList();

        var batchDescription = $"The following {toolsNeedingConfirmation.Count} action(s) will be performed:\n\n" +
                              string.Join("\n", descriptions);

        var dialog = new ConfirmationDialog();
        dialog.Configure("Batch Operation", batchDescription);

        var result = dialog.ShowDialog();

        // If allowed with "don't ask again", mark all tools in batch as skipped
        if (dialog.Allowed && dialog.DontAskAgain)
        {
            foreach (var (tool, _) in toolsNeedingConfirmation)
            {
                _sessionSkips[tool.Name] = true;
            }
        }

        return dialog.Allowed;
    }

    /// <summary>
    /// Resets all session-level skip states.
    /// Call this when the user wants to re-enable confirmations.
    /// </summary>
    public void ResetSessionSkips()
    {
        _sessionSkips.Clear();
    }

    /// <summary>
    /// Checks if a specific tool has session skip enabled.
    /// </summary>
    /// <param name="toolName">The tool name to check.</param>
    /// <returns>True if the tool is set to skip confirmations this session.</returns>
    public bool IsSessionSkipped(string toolName)
    {
        return _sessionSkips.TryGetValue(toolName, out var skip) && skip;
    }

    /// <summary>
    /// Gets the dry-run description for a tool, handling default interface implementation quirks.
    /// </summary>
    private static string GetToolDescription(IRevitTool tool, JsonElement input)
    {
        // Build fallback first so we always have something
        var fallback = $"Execute '{tool?.Name ?? "unknown"}' tool" +
                       (tool?.RequiresTransaction == true ? " (modifies model)" : "");

        if (tool == null)
        {
            return fallback;
        }

        try
        {
            // Use dynamic to bypass interface dispatch and call the concrete implementation
            dynamic dynamicTool = tool;
            object result = dynamicTool.GetDryRunDescription(input);
            var description = result as string;

            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }
        }
        catch
        {
            // Fall through to fallback
        }

        return fallback;
    }
}
