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
using System.Windows;
using RevitAI.Models;
using RevitAI.Services;
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
    private readonly ConfigurationService _configService;
    private readonly SafetyService _safetyService;

    public ToolDispatcher() : this(ToolRegistry.Instance, TransactionManager.Instance,
        ConfigurationService.Instance, SafetyService.Instance) { }

    public ToolDispatcher(ToolRegistry registry, TransactionManager transactionManager,
        ConfigurationService configService, SafetyService safetyService)
    {
        _registry = registry;
        _transactionManager = transactionManager;
        _configService = configService;
        _safetyService = safetyService;
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
            // Check dry-run mode first
            if (_configService.DryRunMode && tool.RequiresTransaction)
            {
                var dryRunDescription = GetToolDryRunDescription(tool, toolUse.Input);
                return new ToolResultBlock
                {
                    ToolUseId = toolUse.Id,
                    Content = $"[DRY RUN - NO CHANGES MADE] {dryRunDescription} (Dry-run mode is enabled. The model was NOT modified. Tell the user this was a dry run.)",
                    IsError = false
                };
            }

            // Check confirmation on WPF UI thread
            if (tool.RequiresConfirmation)
            {
                var confirmed = await ConfirmOnUIThreadAsync(tool, toolUse.Input);
                if (!confirmed)
                {
                    return new ToolResultBlock
                    {
                        ToolUseId = toolUse.Id,
                        Content = "Operation was cancelled by the user.",
                        IsError = true
                    };
                }
            }

            // Execute on Revit thread to safely access Revit API
            var resultTask = await App.ExecuteOnRevitThreadAsync(
                app => ExecuteToolAsync(tool, toolUse, app, cancellationToken),
                cancellationToken);
            var result = await resultTask;

            // Handle image results
            if (result.HasImage)
            {
                return ToolResultBlock.FromImage(toolUse.Id, result.ImageBase64!, result.ImageMediaType!, result.Content);
            }

            return ToolResultBlock.FromText(toolUse.Id, result.Content, result.IsError);
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
    /// Shows confirmation dialog on the WPF UI thread.
    /// </summary>
    private async Task<bool> ConfirmOnUIThreadAsync(IRevitTool tool, JsonElement input)
    {
        // Use WPF dispatcher to show dialog on UI thread
        return await Application.Current.Dispatcher.InvokeAsync(() =>
            _safetyService.CheckAndConfirm(tool, input));
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

        // Check dry-run mode for batch - return dry run results for all tools that require transactions
        if (_configService.DryRunMode)
        {
            var dryRunResults = new List<ToolResultBlock>();
            foreach (var toolUse in toolList)
            {
                var tool = _registry.Get(toolUse.Name);
                if (tool == null)
                {
                    dryRunResults.Add(new ToolResultBlock
                    {
                        ToolUseId = toolUse.Id,
                        Content = $"Unknown tool: '{toolUse.Name}'",
                        IsError = true
                    });
                }
                else if (tool.RequiresTransaction)
                {
                    var description = GetToolDryRunDescription(tool, toolUse.Input);
                    dryRunResults.Add(new ToolResultBlock
                    {
                        ToolUseId = toolUse.Id,
                        Content = $"[DRY RUN - NO CHANGES MADE] {description} (Dry-run mode is enabled. The model was NOT modified. Tell the user this was a dry run.)",
                        IsError = false
                    });
                }
                else
                {
                    // Execute read-only tools normally even in dry-run mode
                    var result = await DispatchAsync(toolUse, cancellationToken);
                    dryRunResults.Add(result);
                }
            }
            return dryRunResults;
        }

        // Check batch confirmation for tools that require it
        var toolsWithInputs = new List<(IRevitTool Tool, JsonElement Input)>();
        foreach (var toolUse in toolList)
        {
            var tool = _registry.Get(toolUse.Name);
            if (tool != null)
            {
                toolsWithInputs.Add((tool, toolUse.Input));
            }
        }

        // Show batch confirmation dialog on UI thread
        var anyRequiresConfirmation = toolsWithInputs.Any(t => t.Tool.RequiresConfirmation);
        if (anyRequiresConfirmation && toolList.Count > 1)
        {
            var confirmed = await Application.Current.Dispatcher.InvokeAsync(() =>
                _safetyService.CheckAndConfirmBatch(toolsWithInputs));

            if (!confirmed)
            {
                return toolList.Select(t => new ToolResultBlock
                {
                    ToolUseId = t.Id,
                    Content = "Batch operation was cancelled by the user.",
                    IsError = true
                }).ToList();
            }
        }

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
            // Note: Single tool confirmation is handled in DispatchAsync
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

                        // Handle image results in batch
                        if (result.HasImage)
                        {
                            results.Add(ToolResultBlock.FromImage(toolUse.Id, result.ImageBase64!, result.ImageMediaType!, result.Content));
                        }
                        else
                        {
                            results.Add(ToolResultBlock.FromText(toolUse.Id, result.Content, result.IsError));
                        }

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

    /// <summary>
    /// Gets the dry-run description for a tool.
    /// Uses reflection to bypass Default Interface Method dispatch issues.
    /// </summary>
    private static string GetToolDryRunDescription(IRevitTool tool, JsonElement input)
    {
        try
        {
            // Use reflection to get the concrete type's method, bypassing DIM dispatch issues.
            // Default Interface Methods have known issues where calling through an interface
            // reference may invoke the default instead of the class's implementation.
            var concreteType = tool.GetType();
            var method = concreteType.GetMethod("GetDryRunDescription",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(JsonElement) },
                null);

            if (method != null && method.DeclaringType != typeof(IRevitTool))
            {
                var result = method.Invoke(tool, new object[] { input });
                if (result is string description && !string.IsNullOrWhiteSpace(description))
                {
                    return description;
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        // Fallback: provide a generic description based on tool name
        return $"Execute '{tool.Name}' tool" + (tool.RequiresTransaction ? " (modifies model)" : "");
    }
}
