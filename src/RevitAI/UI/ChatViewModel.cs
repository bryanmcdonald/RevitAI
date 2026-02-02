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

using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAI.Models;
using RevitAI.Services;
using RevitAI.Tools;
using RevitAI.Tools.ViewTools;

namespace RevitAI.UI;

/// <summary>
/// ViewModel for the ChatPane. Manages conversation state, commands, and streaming updates.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly ConversationPersistenceService _persistenceService;
    private readonly ConfigurationService _configService;
    private readonly ClaudeApiService _apiService;
    private readonly ContextEngine _contextEngine;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _showStatus;

    /// <summary>
    /// Gets or sets the screenshot tool state (Off, OneTime, Always).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenshotStateText))]
    [NotifyPropertyChangedFor(nameof(ScreenshotStateTooltip))]
    private ScreenshotToolState _screenshotState = ScreenshotToolState.Off;

    /// <summary>
    /// Collection of chat messages in the current conversation.
    /// </summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>
    /// Gets display text for the current screenshot state.
    /// </summary>
    public string ScreenshotStateText => ScreenshotState switch
    {
        ScreenshotToolState.Off => "Off",
        ScreenshotToolState.OneTime => "1x",
        ScreenshotToolState.Always => "Auto",
        _ => "Off"
    };

    /// <summary>
    /// Gets tooltip text for the current screenshot state.
    /// </summary>
    public string ScreenshotStateTooltip => ScreenshotState switch
    {
        ScreenshotToolState.Off => "Screenshots disabled. Click to enable one-time screenshot mode.",
        ScreenshotToolState.OneTime => "Screenshot will be attached to each message you send. Click to enable AI-controlled mode.",
        ScreenshotToolState.Always => "AI can capture screenshots as needed. Click to disable.",
        _ => "Screenshot settings"
    };

    /// <summary>
    /// Gets the welcome message text.
    /// </summary>
    private const string WelcomeMessage = @"Welcome to **RevitAI**!

I can help you with your Revit model. Try asking me to:
- Query element information
- Place or modify elements
- Analyze your model

Type a message below to get started.";

    /// <summary>
    /// Message shown when no API key is configured.
    /// </summary>
    private const string NoApiKeyMessage = @"**API Key Required**

Please click the gear icon (⚙) in the header to configure your Anthropic API key.

You can get an API key from [console.anthropic.com](https://console.anthropic.com).";

    public ChatViewModel()
    {
        _persistenceService = new ConversationPersistenceService();
        _configService = ConfigurationService.Instance;
        _apiService = new ClaudeApiService(_configService);
        _contextEngine = new ContextEngine();
        _toolDispatcher = new ToolDispatcher();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Add welcome message
        Messages.Add(ChatMessage.CreateSystemMessage(WelcomeMessage));

        // Check if API key is configured
        if (!_configService.HasApiKey)
        {
            Messages.Add(ChatMessage.CreateSystemMessage(NoApiKeyMessage));
        }

        // Initialize screenshot state from config
        ScreenshotState = _configService.ScreenshotToolEnabled;
    }

    /// <summary>
    /// Cycles through screenshot states: Off -> OneTime -> Always -> Off.
    /// </summary>
    [RelayCommand]
    private void CycleScreenshotState()
    {
        ScreenshotState = ScreenshotState switch
        {
            ScreenshotToolState.Off => ScreenshotToolState.OneTime,
            ScreenshotToolState.OneTime => ScreenshotToolState.Always,
            ScreenshotToolState.Always => ScreenshotToolState.Off,
            _ => ScreenshotToolState.Off
        };

        // Persist to config
        _configService.ScreenshotToolEnabled = ScreenshotState;
    }

    /// <summary>
    /// Determines if a message can be sent.
    /// </summary>
    private bool CanSend() => !string.IsNullOrWhiteSpace(InputText) && !IsProcessing;

    /// <summary>
    /// Sends the current input message.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        // Check for API key
        if (!_configService.HasApiKey)
        {
            Messages.Add(ChatMessage.CreateSystemMessage(
                "Please configure your API key in Settings (gear icon) before sending messages."));
            return;
        }

        var userMessage = InputText.Trim();
        InputText = string.Empty;

        // Add user message
        Messages.Add(ChatMessage.CreateUserMessage(userMessage));

        // Start processing
        IsProcessing = true;
        ShowStatus = true;
        StatusText = "Gathering context...";

        // Reset screenshot counter for this new response
        ScreenshotCounter.Instance.Reset();

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            // Gather context and optionally capture screenshot
            var systemPrompt = await BuildContextualSystemPromptAsync(_cancellationTokenSource.Token);
            byte[]? screenshot = null;

            // In OneTime mode, capture and attach screenshot to user message
            if (ScreenshotState == ScreenshotToolState.OneTime)
            {
                StatusText = "Capturing view...";
                screenshot = await CaptureScreenshotAsync(_cancellationTokenSource.Token);
            }

            StatusText = "Thinking...";

            // Create assistant message in streaming mode
            var assistantMessage = ChatMessage.CreateAssistantMessage(isStreaming: true);
            Messages.Add(assistantMessage);

            // Build conversation history for Claude
            var claudeMessages = BuildClaudeMessages(screenshot);

            // Send streaming request to Claude
            await StreamClaudeResponseAsync(systemPrompt, claudeMessages, assistantMessage, _cancellationTokenSource.Token);

            assistantMessage.CompleteStreaming();

            // Save conversation after successful response
            await _persistenceService.SaveConversationAsync(Messages);
        }
        catch (OperationCanceledException)
        {
            // Mark the last assistant message as cancelled if it exists
            var lastMessage = Messages.LastOrDefault();
            if (lastMessage?.Role == MessageRole.Assistant)
            {
                lastMessage.Cancel();
                if (string.IsNullOrEmpty(lastMessage.Content))
                {
                    lastMessage.Content = "(Cancelled)";
                }
            }
        }
        catch (ClaudeApiException ex)
        {
            HandleApiError(ex);
        }
        catch (Exception ex)
        {
            var lastMessage = Messages.LastOrDefault();
            if (lastMessage?.Role == MessageRole.Assistant)
            {
                lastMessage.SetError(ex.Message);
            }
            else
            {
                Messages.Add(ChatMessage.CreateSystemMessage($"Error: {ex.Message}"));
            }
        }
        finally
        {
            IsProcessing = false;
            ShowStatus = false;
            StatusText = "Ready";
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// Builds a system prompt with current Revit context.
    /// </summary>
    private async Task<string> BuildContextualSystemPromptAsync(CancellationToken cancellationToken)
    {
        try
        {
            var verbosity = _configService.ContextVerbosity;

            var context = await App.ExecuteOnRevitThreadAsync(
                app => _contextEngine.GatherContext(app, verbosity),
                cancellationToken);

            return _contextEngine.BuildSystemPrompt(context, verbosity);
        }
        catch (InvalidOperationException)
        {
            // Threading infrastructure not initialized - use fallback
            return ContextEngine.BuildFallbackSystemPrompt();
        }
        catch (Exception)
        {
            // Any other error - use fallback
            return ContextEngine.BuildFallbackSystemPrompt();
        }
    }

    /// <summary>
    /// Captures a screenshot of the active Revit view.
    /// </summary>
    private async Task<byte[]?> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await App.ExecuteOnRevitThreadAsync(
                app => _contextEngine.CaptureActiveView(app),
                cancellationToken);
        }
        catch
        {
            // Screenshot capture failed - continue without image
            return null;
        }
    }

    /// <summary>
    /// Builds the conversation history in Claude message format.
    /// </summary>
    /// <param name="screenshot">Optional screenshot to include with the last user message.</param>
    private List<ClaudeMessage> BuildClaudeMessages(byte[]? screenshot = null)
    {
        var claudeMessages = new List<ClaudeMessage>();
        var userMessages = Messages
            .Where(m => m.Role != MessageRole.System && !string.IsNullOrEmpty(m.Content) && !m.IsStreaming)
            .ToList();

        for (int i = 0; i < userMessages.Count; i++)
        {
            var msg = userMessages[i];
            var isLastUserMessage = i == userMessages.Count - 1 && msg.Role == MessageRole.User;

            if (msg.Role == MessageRole.User)
            {
                // Include screenshot only with the most recent user message
                if (isLastUserMessage && screenshot != null && screenshot.Length > 0)
                {
                    claudeMessages.Add(ClaudeMessage.UserWithImage(msg.Content, screenshot));
                }
                else
                {
                    claudeMessages.Add(ClaudeMessage.User(msg.Content));
                }
            }
            else
            {
                claudeMessages.Add(ClaudeMessage.Assistant(msg.Content));
            }
        }

        return claudeMessages;
    }

    /// <summary>
    /// Streams a response from Claude API with tool execution loop.
    /// </summary>
    private async Task StreamClaudeResponseAsync(
        string systemPrompt,
        List<ClaudeMessage> messages,
        ChatMessage assistantMessage,
        CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() => StatusText = "Receiving...");

        // Get tool definitions from registry (filtered based on screenshot settings)
        var toolDefinitions = ToolRegistry.Instance.GetDefinitionsForRequest();
        var hasTools = toolDefinitions.Count > 0;

        // Keep conversation messages for multi-turn tool use
        var conversationMessages = new List<ClaudeMessage>(messages);

        // Tool execution loop - continues until Claude stops using tools
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accumulator = new ResponseAccumulator();

            // Run streaming on background thread to keep UI responsive
            await Task.Run(async () =>
            {
                await foreach (var streamEvent in _apiService.SendMessageStreamingAsync(
                    systemPrompt: systemPrompt,
                    messages: conversationMessages,
                    tools: hasTools ? toolDefinitions : null,
                    settingsOverride: null,
                    ct: cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    accumulator.ProcessEvent(streamEvent);

                    if (streamEvent is ContentBlockDeltaEvent deltaEvent)
                    {
                        if (deltaEvent.Delta is TextDelta textDelta)
                        {
                            // Use async invoke to allow UI to repaint between chunks
                            await _dispatcher.InvokeAsync(() => assistantMessage.AppendContent(textDelta.Text));
                        }
                    }
                    else if (streamEvent is ErrorEvent errorEvent)
                    {
                        var errorMessage = errorEvent.Error?.Message ?? "Unknown streaming error";
                        throw new ClaudeApiException(errorMessage);
                    }
                }
            }, cancellationToken);

            // Check if we need to execute tools
            if (accumulator.StopReason != "tool_use" || accumulator.ToolUseBlocks.Count == 0)
            {
                // No more tool calls - we're done
                break;
            }

            // Add assistant's response (with tool use) to conversation history
            conversationMessages.Add(ClaudeMessage.Assistant(accumulator.GetContentBlocks()));

            // Execute all tool calls
            await _dispatcher.InvokeAsync(() => StatusText = $"Executing {accumulator.ToolUseBlocks.Count} tool(s)...");

            var toolResults = await _toolDispatcher.DispatchAllAsync(accumulator.ToolUseBlocks, cancellationToken);

            // Show tool execution in the chat
            foreach (var toolUse in accumulator.ToolUseBlocks)
            {
                var result = toolResults.First(r => r.ToolUseId == toolUse.Id);
                var statusText = result.IsError ? "failed" : "completed";
                await _dispatcher.InvokeAsync(() => assistantMessage.AppendContent($"\n\n[Tool: {toolUse.Name} {statusText}]\n\n"));
            }

            // Add tool results to conversation history
            conversationMessages.Add(ClaudeMessage.ToolResult(toolResults));

            // Continue the loop to get Claude's response to the tool results
            await _dispatcher.InvokeAsync(() => StatusText = "Processing tool results...");
        }
    }

    /// <summary>
    /// Accumulates streaming response data to track stop reason and tool use blocks.
    /// </summary>
    private sealed class ResponseAccumulator
    {
        private readonly Dictionary<int, ToolUseBlockBuilder> _toolBuilders = new();
        private readonly List<ContentBlock> _contentBlocks = new();

        /// <summary>
        /// Gets the stop reason from the response.
        /// </summary>
        public string? StopReason { get; private set; }

        /// <summary>
        /// Gets the accumulated tool use blocks.
        /// </summary>
        public List<ToolUseBlock> ToolUseBlocks => _toolBuilders.Values
            .Select(b => b.Build())
            .Where(b => b != null)
            .Cast<ToolUseBlock>()
            .ToList();

        /// <summary>
        /// Processes a stream event and accumulates relevant data.
        /// </summary>
        public void ProcessEvent(StreamEvent streamEvent)
        {
            switch (streamEvent)
            {
                case ContentBlockStartEvent startEvent:
                    if (startEvent.ContentBlock is ToolUseBlock toolUseStart)
                    {
                        _toolBuilders[startEvent.Index] = new ToolUseBlockBuilder(toolUseStart.Id, toolUseStart.Name);
                    }
                    else if (startEvent.ContentBlock is TextContentBlock textBlock)
                    {
                        _contentBlocks.Add(textBlock);
                    }
                    break;

                case ContentBlockDeltaEvent deltaEvent:
                    if (deltaEvent.Delta is InputJsonDelta jsonDelta)
                    {
                        if (_toolBuilders.TryGetValue(deltaEvent.Index, out var builder))
                        {
                            builder.AppendJson(jsonDelta.PartialJson);
                        }
                    }
                    else if (deltaEvent.Delta is TextDelta textDelta)
                    {
                        // Update text content block
                        if (_contentBlocks.Count > 0 && _contentBlocks[^1] is TextContentBlock lastText)
                        {
                            // Create a new block with accumulated text
                            var newText = lastText.Text + textDelta.Text;
                            _contentBlocks[^1] = new TextContentBlock { Text = newText };
                        }
                    }
                    break;

                case MessageDeltaEvent messageDelta:
                    StopReason = messageDelta.Delta?.StopReason;
                    break;
            }
        }

        /// <summary>
        /// Gets all content blocks (text and tool use) for the conversation history.
        /// </summary>
        public List<ContentBlock> GetContentBlocks()
        {
            var blocks = new List<ContentBlock>(_contentBlocks);
            blocks.AddRange(ToolUseBlocks);
            return blocks;
        }
    }

    /// <summary>
    /// Builds a ToolUseBlock from streamed JSON fragments.
    /// </summary>
    private sealed class ToolUseBlockBuilder
    {
        private readonly string _id;
        private readonly string _name;
        private readonly StringBuilder _jsonBuilder = new();

        public ToolUseBlockBuilder(string id, string name)
        {
            _id = id;
            _name = name;
        }

        public void AppendJson(string json)
        {
            _jsonBuilder.Append(json);
        }

        public ToolUseBlock? Build()
        {
            var json = _jsonBuilder.ToString();
            if (string.IsNullOrWhiteSpace(json))
            {
                json = "{}";
            }

            try
            {
                var input = JsonDocument.Parse(json).RootElement;
                return new ToolUseBlock
                {
                    Id = _id,
                    Name = _name,
                    Input = input.Clone()
                };
            }
            catch (JsonException)
            {
                // Failed to parse JSON - return with empty input
                return new ToolUseBlock
                {
                    Id = _id,
                    Name = _name,
                    Input = JsonDocument.Parse("{}").RootElement
                };
            }
        }
    }

    /// <summary>
    /// Handles API errors with appropriate user messaging.
    /// </summary>
    private void HandleApiError(ClaudeApiException ex)
    {
        var lastMessage = Messages.LastOrDefault();

        string userFriendlyMessage = ex.ErrorType switch
        {
            "authentication_error" =>
                "Authentication failed. Please check your API key in Settings.",
            "rate_limit_error" =>
                "Rate limit exceeded. Please wait a moment and try again.",
            "invalid_request_error" =>
                $"Invalid request: {ex.Message}",
            _ =>
                ex.Message
        };

        if (lastMessage?.Role == MessageRole.Assistant)
        {
            lastMessage.SetError(userFriendlyMessage);
        }
        else
        {
            Messages.Add(ChatMessage.CreateSystemMessage($"Error: {userFriendlyMessage}"));
        }
    }

    /// <summary>
    /// Determines if the current operation can be cancelled.
    /// </summary>
    private bool CanCancel() => IsProcessing;

    /// <summary>
    /// Cancels the current operation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        _apiService.CancelCurrentRequest();
        StatusText = "Cancelling...";
    }

    /// <summary>
    /// Clears the conversation and starts fresh.
    /// </summary>
    [RelayCommand]
    private void ClearConversation()
    {
        Messages.Clear();
        Messages.Add(ChatMessage.CreateSystemMessage(WelcomeMessage));

        if (!_configService.HasApiKey)
        {
            Messages.Add(ChatMessage.CreateSystemMessage(NoApiKeyMessage));
        }

        _persistenceService.StartNewConversation();
        StatusText = "Conversation cleared";

        // Clear status after a moment
        Task.Delay(2000).ContinueWith(_ =>
        {
            _dispatcher.Invoke(() => StatusText = "Ready");
        });
    }

    /// <summary>
    /// Opens the settings dialog.
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        try
        {
            var dialog = new SettingsDialog
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var result = dialog.ShowDialog();

            // If settings were saved and we now have an API key, remove the warning message
            if (result == true && _configService.HasApiKey)
            {
                // Remove any "API Key Required" messages
                var warningMessages = Messages
                    .Where(m => m.Role == MessageRole.System && m.Content.Contains("API Key Required"))
                    .ToList();

                foreach (var msg in warningMessages)
                {
                    Messages.Remove(msg);
                }
            }
        }
        catch (Exception ex)
        {
            Messages.Add(ChatMessage.CreateSystemMessage($"Failed to open settings: {ex.Message}"));
        }
    }

    /// <summary>
    /// Appends content to the current streaming message from a background thread.
    /// </summary>
    public void AppendStreamingContent(string chunk)
    {
        _dispatcher.Invoke(() =>
        {
            var lastMessage = Messages.LastOrDefault();
            if (lastMessage?.IsStreaming == true)
            {
                lastMessage.AppendContent(chunk);
            }
        });
    }

    /// <summary>
    /// Handles keyboard input for sending messages.
    /// Enter sends, Shift+Enter adds newline.
    /// </summary>
    public void HandleKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            {
                // Shift+Enter: insert newline (default behavior)
                return;
            }

            // Enter alone: send message
            e.Handled = true;
            if (SendCommand.CanExecute(null))
            {
                SendCommand.Execute(null);
            }
        }
    }
}
