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
    private readonly ContextEngine _contextEngine;
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
    /// Gets or sets whether to include a screenshot of the active view with messages.
    /// </summary>
    [ObservableProperty]
    private bool _includeScreenshot;

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

    public ChatViewModel()
    {
        _persistenceService = new ConversationPersistenceService();
        _configService = ConfigurationService.Instance;
        _apiService = new ClaudeApiService(_configService);
        _contextEngine = new ContextEngine();
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
        StatusText = "Gathering context...";

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            // Gather context and optionally capture screenshot
            var systemPrompt = await BuildContextualSystemPromptAsync(_cancellationTokenSource.Token);
            byte[]? screenshot = null;

            if (IncludeScreenshot)
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
    /// Streams a response from Claude API.
    /// </summary>
    private async Task StreamClaudeResponseAsync(
        string systemPrompt,
        List<ClaudeMessage> messages,
        ChatMessage assistantMessage,
        CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() => StatusText = "Receiving...");

        // Run streaming on background thread to keep UI responsive
        await Task.Run(async () =>
        {
            await foreach (var streamEvent in _apiService.SendMessageStreamingAsync(
                systemPrompt: systemPrompt,
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
