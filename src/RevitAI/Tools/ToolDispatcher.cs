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
using RevitAI.Models;

namespace RevitAI.Tools;

/// <summary>
/// Dispatches tool calls from Claude to the appropriate tool implementations.
/// Handles thread marshalling to ensure Revit API calls run on the main thread.
/// </summary>
public sealed class ToolDispatcher
{
    private readonly ToolRegistry _registry;

    public ToolDispatcher() : this(ToolRegistry.Instance) { }

    public ToolDispatcher(ToolRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Dispatches a single tool call and returns the result.
    /// </summary>
    /// <param name="toolUse">The tool use block from Claude.</param>
    /// <param name="cancellationToken">Token for cancellation.</param>
    /// <returns>A tool result block to send back to Claude.</returns>
    public async Task<ToolResultBlock> DispatchAsync(ToolUseBlock toolUse, CancellationToken cancellationToken)
    {
        var tool = _registry.Get(toolUse.Name);

        if (tool == null)
        {
            var availableTools = _registry.GetAvailableToolNames();
            var errorMessage = string.IsNullOrEmpty(availableTools)
                ? $"Unknown tool: '{toolUse.Name}'. No tools are currently registered."
                : $"Unknown tool: '{toolUse.Name}'. Available tools: {availableTools}";

            return new ToolResultBlock
            {
                ToolUseId = toolUse.Id,
                Content = errorMessage,
                IsError = true
            };
        }

        // Check if transaction is required but TransactionManager not available
        if (tool.RequiresTransaction)
        {
            return new ToolResultBlock
            {
                ToolUseId = toolUse.Id,
                Content = $"Tool '{toolUse.Name}' requires a transaction, but the TransactionManager is not yet implemented. " +
                         "This tool will be available after P1-08 (Transaction Manager) is complete.",
                IsError = true
            };
        }

        try
        {
            // Execute on Revit thread to safely access Revit API
            // Note: ExecuteOnRevitThreadAsync returns Task<T> where T is the return type of the func.
            // Since tool.ExecuteAsync returns Task<ToolResult>, we get Task<Task<ToolResult>> back.
            // We need to await the inner task.
            var resultTask = await App.ExecuteOnRevitThreadAsync(
                app => tool.ExecuteAsync(toolUse.Input, app, cancellationToken),
                cancellationToken);
            var result = await resultTask;

            return new ToolResultBlock
            {
                ToolUseId = toolUse.Id,
                Content = result.Content,
                IsError = result.IsError
            };
        }
        catch (OperationCanceledException)
        {
            return new ToolResultBlock
            {
                ToolUseId = toolUse.Id,
                Content = "Tool execution was cancelled.",
                IsError = true
            };
        }
        catch (InvalidOperationException ex)
        {
            // Threading infrastructure not available
            return new ToolResultBlock
            {
                ToolUseId = toolUse.Id,
                Content = $"Cannot execute tool: {ex.Message}",
                IsError = true
            };
        }
        catch (Exception ex)
        {
            return new ToolResultBlock
            {
                ToolUseId = toolUse.Id,
                Content = ToolResult.FromException(ex).Content,
                IsError = true
            };
        }
    }

    /// <summary>
    /// Dispatches multiple tool calls and returns all results.
    /// Tools are executed sequentially to avoid concurrent Revit API access.
    /// </summary>
    /// <param name="toolUses">The tool use blocks from Claude.</param>
    /// <param name="cancellationToken">Token for cancellation.</param>
    /// <returns>A list of tool result blocks to send back to Claude.</returns>
    public async Task<List<ToolResultBlock>> DispatchAllAsync(
        IEnumerable<ToolUseBlock> toolUses,
        CancellationToken cancellationToken)
    {
        var results = new List<ToolResultBlock>();

        foreach (var toolUse in toolUses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await DispatchAsync(toolUse, cancellationToken);
            results.Add(result);
        }

        return results;
    }
}
