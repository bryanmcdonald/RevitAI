using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAI.Models;
using RevitAI.Services;

namespace RevitAI.UI;

/// <summary>
/// ViewModel for the ChatPane. Manages conversation state, commands, and streaming updates.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly ConversationPersistenceService _persistenceService;
    private readonly ConfigurationService _configService;
    private readonly ClaudeApiService _apiService;
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
    /// Collection of chat messages in the current conversation.
    /// </summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

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

    /// <summary>
    /// System prompt for RevitAI conversations.
    /// </summary>
    private const string SystemPrompt = @"You are RevitAI, an AI assistant embedded in Autodesk Revit. You help users with their Revit models through natural language conversation.

Your capabilities include:
- Answering questions about Revit and BIM workflows
- Helping users understand their model structure
- Providing guidance on Revit best practices

Be concise and helpful. When discussing Revit elements, use correct terminology.

Note: Tool-based model queries and modifications will be available in future updates.";

    public ChatViewModel()
    {
        _persistenceService = new ConversationPersistenceService();
        _configService = ConfigurationService.Instance;
        _apiService = new ClaudeApiService(_configService);
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Add welcome message
        Messages.Add(ChatMessage.CreateSystemMessage(WelcomeMessage));

        // Check if API key is configured
        if (!_configService.HasApiKey)
        {
            Messages.Add(ChatMessage.CreateSystemMessage(NoApiKeyMessage));
        }
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
        StatusText = "Thinking...";

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            // Create assistant message in streaming mode
            var assistantMessage = ChatMessage.CreateAssistantMessage(isStreaming: true);
            Messages.Add(assistantMessage);

            // Build conversation history for Claude
            var claudeMessages = BuildClaudeMessages();

            // Send streaming request to Claude
            await StreamClaudeResponseAsync(claudeMessages, assistantMessage, _cancellationTokenSource.Token);

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
    /// Builds the conversation history in Claude message format.
    /// </summary>
    private List<ClaudeMessage> BuildClaudeMessages()
    {
        var claudeMessages = new List<ClaudeMessage>();

        foreach (var msg in Messages)
        {
            // Skip system messages (they go in the system prompt)
            if (msg.Role == MessageRole.System)
                continue;

            // Skip empty or streaming messages
            if (string.IsNullOrEmpty(msg.Content) || msg.IsStreaming)
                continue;

            claudeMessages.Add(msg.Role == MessageRole.User
                ? ClaudeMessage.User(msg.Content)
                : ClaudeMessage.Assistant(msg.Content));
        }

        return claudeMessages;
    }

    /// <summary>
    /// Streams a response from Claude API.
    /// </summary>
    private async Task StreamClaudeResponseAsync(
        List<ClaudeMessage> messages,
        ChatMessage assistantMessage,
        CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() => StatusText = "Receiving...");

        // Run streaming on background thread to keep UI responsive
        await Task.Run(async () =>
        {
            await foreach (var streamEvent in _apiService.SendMessageStreamingAsync(
                systemPrompt: SystemPrompt,
                messages: messages,
                tools: null,
                settingsOverride: null,
                ct: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

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
