namespace RevitAI.Tools;

/// <summary>
/// Represents the result of a tool execution.
/// Immutable wrapper with success/error states.
/// </summary>
public sealed class ToolResult
{
    /// <summary>
    /// Gets whether the tool execution was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets whether the tool execution resulted in an error.
    /// </summary>
    public bool IsError => !Success;

    /// <summary>
    /// Gets the result content or error message.
    /// </summary>
    public string Content { get; }

    private ToolResult(bool success, string content)
    {
        Success = success;
        Content = content;
    }

    /// <summary>
    /// Creates a successful result with the given content.
    /// </summary>
    /// <param name="content">The result content.</param>
    public static ToolResult Ok(string content) => new(true, content);

    /// <summary>
    /// Creates an error result with the given message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public static ToolResult Error(string message) => new(false, message);

    /// <summary>
    /// Creates an error result from an exception.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    public static ToolResult FromException(Exception exception)
    {
        var message = exception switch
        {
            OperationCanceledException => "Tool execution was cancelled.",
            InvalidOperationException ex => ex.Message,
            _ => $"Tool execution failed: {exception.Message}"
        };
        return new ToolResult(false, message);
    }
}
