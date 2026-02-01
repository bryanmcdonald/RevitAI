using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitAI.Models;

/// <summary>
/// Request body for the Claude Messages API.
/// </summary>
public sealed class ClaudeRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.7;

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? System { get; init; }

    [JsonPropertyName("messages")]
    public required List<ClaudeMessage> Messages { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolDefinition>? Tools { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

/// <summary>
/// A single message in the conversation.
/// </summary>
public sealed class ClaudeMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required object Content { get; init; }

    /// <summary>
    /// Creates a user message with text content.
    /// </summary>
    public static ClaudeMessage User(string text) => new()
    {
        Role = "user",
        Content = text
    };

    /// <summary>
    /// Creates an assistant message with text content.
    /// </summary>
    public static ClaudeMessage Assistant(string text) => new()
    {
        Role = "assistant",
        Content = text
    };

    /// <summary>
    /// Creates an assistant message with content blocks (for tool use).
    /// </summary>
    public static ClaudeMessage Assistant(List<ContentBlock> blocks) => new()
    {
        Role = "assistant",
        Content = blocks
    };

    /// <summary>
    /// Creates a user message with tool results.
    /// </summary>
    public static ClaudeMessage ToolResult(List<ToolResultBlock> results) => new()
    {
        Role = "user",
        Content = results.Cast<object>().ToList()
    };
}

/// <summary>
/// Response from the Claude Messages API.
/// </summary>
public sealed class ClaudeResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; init; } = new();

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; init; }

    [JsonPropertyName("usage")]
    public Usage? Usage { get; init; }

    /// <summary>
    /// Gets the first text content from the response, if any.
    /// </summary>
    public string? GetTextContent()
    {
        return Content
            .OfType<TextContentBlock>()
            .Select(b => b.Text)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets all tool use blocks from the response.
    /// </summary>
    public IEnumerable<ToolUseBlock> GetToolUseBlocks()
    {
        return Content.OfType<ToolUseBlock>();
    }
}

/// <summary>
/// Token usage statistics.
/// </summary>
public sealed class Usage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

/// <summary>
/// Base class for content blocks in messages.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContentBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlock), "tool_result")]
public abstract class ContentBlock
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Text content block.
/// </summary>
public sealed class TextContentBlock : ContentBlock
{
    public override string Type => "text";

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>
/// Tool use content block (assistant requesting tool execution).
/// </summary>
public sealed class ToolUseBlock : ContentBlock
{
    public override string Type => "tool_use";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("input")]
    public JsonElement Input { get; init; }
}

/// <summary>
/// Tool result content block (user providing tool output).
/// </summary>
public sealed class ToolResultBlock : ContentBlock
{
    public override string Type => "tool_result";

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; init; }
}

/// <summary>
/// Definition of a tool that Claude can use.
/// </summary>
public sealed class ToolDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("input_schema")]
    public required JsonElement InputSchema { get; init; }
}

/// <summary>
/// API error response from Claude.
/// </summary>
public sealed class ClaudeErrorResponse
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public ClaudeError? Error { get; init; }
}

/// <summary>
/// Error details from the Claude API.
/// </summary>
public sealed class ClaudeError
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
