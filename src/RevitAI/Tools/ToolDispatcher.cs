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
using RevitAI.Transactions;

namespace RevitAI.Tools;

/// <summary>
/// Dispatches tool calls from Claude to the appropriate tool implementations.
/// Handles thread marshalling to ensure Revit API calls run on the main thread.
/// </summary>
public sealed class ToolDispatcher
{
    private readonly ToolRegistry _registry;
    private readonly TransactionManager _transactionManager;

    public ToolDispatcher() : this(ToolRegistry.Instance, TransactionManager.Instance) { }

    public ToolDispatcher(ToolRegistry registry, TransactionManager transactionManager)
    {
        _registry = registry;
        _transactionManager = transactionManager;
    }

    /// <summary>
    /// Dispatches a single tool call and returns the result.
    /// If the tool requires a transaction and a group is already active, the transaction
    /// is executed within that group. Otherwise, a standalone transaction is used.
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

        try
        {
            // Execute on Revit thread to safely access Revit API
            var resultTask = await App.ExecuteOnRevitThreadAsync(
                app => ExecuteToolAsync(tool, toolUse, app, cancellationToken),
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
    /// Executes a tool, wrapping it in a transaction if required.
    /// Must be called on the Revit main thread.
    /// </summary>
    private async Task<ToolResult> ExecuteToolAsync(
        IRevitTool tool,
        ToolUseBlock toolUse,
        Autodesk.Revit.UI.UIApplication app,
        CancellationToken cancellationToken)
    {
        if (!tool.RequiresTransaction)
        {
            // Read-only tool, no transaction needed
            return await tool.ExecuteAsync(toolUse.Input, app, cancellationToken);
        }

        // Tool requires a transaction
        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
        {
            return ToolResult.Error("No document is open. Cannot execute modification tool.");
        }

        using var scope = _transactionManager.StartTransaction(doc, toolUse.Name);
        try
        {
            var result = await tool.ExecuteAsync(toolUse.Input, app, cancellationToken);

            if (!result.IsError)
            {
                scope.Commit();
            }
            // If there's an error, the transaction will auto-rollback on dispose

            return result;
        }
        catch (Exception ex)
        {
            // Transaction auto-rollback on dispose
            return ToolResult.FromException(ex);
        }
    }

    /// <summary>
    /// Dispatches multiple tool calls and returns all results.
    /// Tools are executed sequentially to avoid concurrent Revit API access.
    /// If any tools require transactions, they are batched into a single undo operation.
    /// </summary>
    /// <param name="toolUses">The tool use blocks from Claude.</param>
    /// <param name="cancellationToken">Token for cancellation.</param>
    /// <returns>A list of tool result blocks to send back to Claude.</returns>
    public async Task<List<ToolResultBlock>> DispatchAllAsync(
        IEnumerable<ToolUseBlock> toolUses,
        CancellationToken cancellationToken)
    {
        var toolList = toolUses.ToList();

        // Check if any tools require transactions
        var anyRequiresTransaction = toolList.Any(t =>
        {
            var tool = _registry.Get(t.Name);
            return tool?.RequiresTransaction == true;
        });

        // If we need transactions and have multiple tools, use a group for batching
        // Execute ALL tools in a SINGLE Revit thread call to keep transactions in the same context
        var useGroup = anyRequiresTransaction && toolList.Count > 1;

        if (useGroup)
        {
            return await ExecuteAllToolsInGroupAsync(toolList, cancellationToken);
        }
        else
        {
            // Single tool or no transactions - use simple sequential dispatch
            var results = new List<ToolResultBlock>();
            foreach (var toolUse in toolList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await DispatchAsync(toolUse, cancellationToken);
                results.Add(result);
            }
            return results;
        }
    }

    /// <summary>
    /// Executes all tools within a single Revit thread call using a transaction group.
    /// This ensures all transactions are within the same API context.
    /// </summary>
    private async Task<List<ToolResultBlock>> ExecuteAllToolsInGroupAsync(
        List<ToolUseBlock> toolList,
        CancellationToken cancellationToken)
    {
        try
        {
            return await App.ExecuteOnRevitThreadAsync(app =>
            {
                var results = new List<ToolResultBlock>();
                var doc = app.ActiveUIDocument?.Document;

                if (doc == null)
                {
                    // No document - return errors for all tools
                    foreach (var toolUse in toolList)
                    {
                        results.Add(new ToolResultBlock
                        {
                            ToolUseId = toolUse.Id,
                            Content = "No document is open. Cannot execute modification tool.",
                            IsError = true
                        });
                    }
                    return results;
                }

                // Start transaction group
                _transactionManager.StartGroup(doc, "Tool Batch");

                try
                {
                    foreach (var toolUse in toolList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var tool = _registry.Get(toolUse.Name);
                        if (tool == null)
                        {
                            var availableTools = _registry.GetAvailableToolNames();
                            results.Add(new ToolResultBlock
                            {
                                ToolUseId = toolUse.Id,
                                Content = $"Unknown tool: '{toolUse.Name}'. Available tools: {availableTools}",
                                IsError = true
                            });

                            // Rollback and skip remaining
                            _transactionManager.RollbackGroup();
                            MarkRemainingAsSkipped(toolList, toolUse, results);
                            return results;
                        }

                        ToolResult result;
                        if (tool.RequiresTransaction)
                        {
                            using var scope = _transactionManager.StartTransaction(doc, toolUse.Name);
                            try
                            {
                                // ExecuteAsync returns Task<ToolResult>, need to wait synchronously
                                result = tool.ExecuteAsync(toolUse.Input, app, cancellationToken).GetAwaiter().GetResult();

                                if (!result.IsError)
                                {
                                    scope.Commit();
                                }
                            }
                            catch (Exception ex)
                            {
                                result = ToolResult.FromException(ex);
                            }
                        }
                        else
                        {
                            try
                            {
                                result = tool.ExecuteAsync(toolUse.Input, app, cancellationToken).GetAwaiter().GetResult();
                            }
                            catch (Exception ex)
                            {
                                result = ToolResult.FromException(ex);
                            }
                        }

                        results.Add(new ToolResultBlock
                        {
                            ToolUseId = toolUse.Id,
                            Content = result.Content,
                            IsError = result.IsError
                        });

                        // If tool failed, rollback and skip remaining
                        if (result.IsError)
                        {
                            _transactionManager.RollbackGroup();
                            MarkRemainingAsSkipped(toolList, toolUse, results);
                            return results;
                        }
                    }

                    // All tools succeeded - commit the group
                    _transactionManager.CommitGroup();
                }
                catch (OperationCanceledException)
                {
                    _transactionManager.EnsureGroupClosed();
                    throw;
                }
                catch
                {
                    _transactionManager.EnsureGroupClosed();
                    throw;
                }

                return results;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return toolList.Select(t => new ToolResultBlock
            {
                ToolUseId = t.Id,
                Content = "Tool execution was cancelled.",
                IsError = true
            }).ToList();
        }
        catch (Exception ex)
        {
            return toolList.Select(t => new ToolResultBlock
            {
                ToolUseId = t.Id,
                Content = $"Tool batch execution failed: {ex.Message}",
                IsError = true
            }).ToList();
        }
    }

    /// <summary>
    /// Marks remaining tools as skipped after a failure.
    /// </summary>
    private static void MarkRemainingAsSkipped(
        List<ToolUseBlock> toolList,
        ToolUseBlock failedTool,
        List<ToolResultBlock> results)
    {
        var remainingIndex = toolList.IndexOf(failedTool) + 1;
        for (var i = remainingIndex; i < toolList.Count; i++)
        {
            results.Add(new ToolResultBlock
            {
                ToolUseId = toolList[i].Id,
                Content = "Skipped due to earlier tool failure in batch.",
                IsError = true
            });
        }
    }
}
