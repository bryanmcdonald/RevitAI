using CommunityToolkit.Mvvm.ComponentModel;

namespace RevitAI.UI;

/// <summary>
/// Represents the role of a message sender.
/// </summary>
public enum MessageRole
{
    User,
    Assistant,
    System
}

/// <summary>
/// Represents the status of a message.
/// </summary>
public enum MessageStatus
{
    Pending,
    Streaming,
    Complete,
    Error,
    Cancelled
}

/// <summary>
/// Represents a single chat message in the conversation.
/// Observable properties for real-time UI updates during streaming.
/// </summary>
public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private MessageRole _role;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    private MessageStatus _status = MessageStatus.Complete;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Creates a new user message.
    /// </summary>
    public static ChatMessage CreateUserMessage(string content) => new()
    {
        Role = MessageRole.User,
        Content = content,
        Status = MessageStatus.Complete
    };

    /// <summary>
    /// Creates a new assistant message, optionally in streaming mode.
    /// </summary>
    public static ChatMessage CreateAssistantMessage(string content = "", bool isStreaming = false) => new()
    {
        Role = MessageRole.Assistant,
        Content = content,
        Status = isStreaming ? MessageStatus.Streaming : MessageStatus.Complete,
        IsStreaming = isStreaming
    };

    /// <summary>
    /// Creates a system message (e.g., welcome message, errors).
    /// </summary>
    public static ChatMessage CreateSystemMessage(string content) => new()
    {
        Role = MessageRole.System,
        Content = content,
        Status = MessageStatus.Complete
    };

    /// <summary>
    /// Appends content to the message (used for streaming responses).
    /// Thread-safe when called via Dispatcher.
    /// </summary>
    public void AppendContent(string chunk)
    {
        Content += chunk;
    }

    /// <summary>
    /// Marks the message as complete after streaming finishes.
    /// </summary>
    public void CompleteStreaming()
    {
        IsStreaming = false;
        Status = MessageStatus.Complete;
    }

    /// <summary>
    /// Marks the message as cancelled.
    /// </summary>
    public void Cancel()
    {
        IsStreaming = false;
        Status = MessageStatus.Cancelled;
    }

    /// <summary>
    /// Marks the message as having an error.
    /// </summary>
    public void SetError(string errorMessage)
    {
        IsStreaming = false;
        Status = MessageStatus.Error;
        ErrorMessage = errorMessage;
    }
}
