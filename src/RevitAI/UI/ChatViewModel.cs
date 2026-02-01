using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAI.Services;

namespace RevitAI.UI;

/// <summary>
/// ViewModel for the ChatPane. Manages conversation state, commands, and streaming updates.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly ConversationPersistenceService _persistenceService;
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

    public ChatViewModel()
    {
        _persistenceService = new ConversationPersistenceService();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Add welcome message
        Messages.Add(ChatMessage.CreateSystemMessage(WelcomeMessage));
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

            // Simulate streaming response (will be replaced with actual Claude API in P1-04)
            await SimulateStreamingResponseAsync(userMessage, assistantMessage, _cancellationTokenSource.Token);

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
        _persistenceService.StartNewConversation();
        StatusText = "Conversation cleared";

        // Clear status after a moment
        Task.Delay(2000).ContinueWith(_ =>
        {
            _dispatcher.Invoke(() => StatusText = "Ready");
        });
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
    /// Simulates a streaming response for testing purposes.
    /// Will be replaced with actual Claude API integration in P1-04.
    /// </summary>
    private async Task SimulateStreamingResponseAsync(string userMessage, ChatMessage assistantMessage, CancellationToken cancellationToken)
    {
        // Simulate "thinking" delay
        await Task.Delay(500, cancellationToken);

        StatusText = "Receiving...";

        // Generate a sample response with markdown
        var response = GenerateSampleResponse(userMessage);

        // Simulate streaming by sending chunks
        var chunkSize = 10;
        for (var i = 0; i < response.Length; i += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = response.Substring(i, Math.Min(chunkSize, response.Length - i));

            _dispatcher.Invoke(() => assistantMessage.AppendContent(chunk));

            // Random delay to simulate network latency
            await Task.Delay(Random.Shared.Next(20, 60), cancellationToken);
        }
    }

    /// <summary>
    /// Generates a sample response based on the user's message.
    /// </summary>
    private static string GenerateSampleResponse(string userMessage)
    {
        var lowerMessage = userMessage.ToLowerInvariant();

        if (lowerMessage.Contains("hello") || lowerMessage.Contains("hi"))
        {
            return @"Hello! I'm your RevitAI assistant. I can help you with:

- **Querying elements** in your model
- **Placing new elements** like walls, doors, and windows
- **Modifying parameters** on selected elements
- **Analyzing** your model structure

What would you like to do today?";
        }

        if (lowerMessage.Contains("wall"))
        {
            return @"I can help you with walls! Here are some things I can do:

1. **Query walls** - Get information about walls in your model
2. **Place walls** - Create new walls at specified locations
3. **Modify walls** - Change wall types, heights, or parameters

For example, you could say:
- ""Show me all exterior walls""
- ""Place a 10ft wall from point A to point B""
- ""Change the selected wall to type 'Basic Wall'""

What would you like to do with walls?";
        }

        if (lowerMessage.Contains("select"))
        {
            return @"I can see your current selection in Revit. To work with selected elements:

1. Select elements in Revit first
2. Then ask me to perform operations on them

For example:
- ""What is selected?""
- ""Show parameters of selected elements""
- ""Change the height of selected walls to 12 feet""

The context engine tracks your selection in real-time, so I always know what you're working with.";
        }

        // Default response
        return $@"I received your message: ""{userMessage}""

This is a **simulated response** for testing the chat interface. In the full implementation, I'll:

1. Analyze your request using Claude AI
2. Use available **tools** to query or modify your Revit model
3. Provide helpful responses with relevant information

Try asking about:
- Walls, doors, windows, or other elements
- Your current selection
- Model information

*Note: This is a test response. Claude API integration coming in P1-04.*";
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
