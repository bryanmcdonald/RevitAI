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

    /// <summary>
    /// Creates a user message with text and an optional image.
    /// </summary>
    /// <param name="text">The text content.</param>
    /// <param name="imageBytes">Optional PNG image bytes to include.</param>
    public static ClaudeMessage UserWithImage(string text, byte[]? imageBytes = null)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return User(text);
        }

        var contentBlocks = new List<object>
        {
            ImageContentBlock.FromPngBytes(imageBytes),
            new TextContentBlock { Text = text }
        };

        return new ClaudeMessage
        {
            Role = "user",
            Content = contentBlocks
        };
    }
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
[JsonDerivedType(typeof(ImageContentBlock), "image")]
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
/// Image content block for sending images to Claude.
/// </summary>
public sealed class ImageContentBlock : ContentBlock
{
    public override string Type => "image";

    [JsonPropertyName("source")]
    public required ImageSource Source { get; init; }

    /// <summary>
    /// Creates an image content block from base64-encoded PNG data.
    /// </summary>
    public static ImageContentBlock FromPngBytes(byte[] imageBytes)
    {
        return new ImageContentBlock
        {
            Source = new ImageSource
            {
                Type = "base64",
                MediaType = "image/png",
                Data = Convert.ToBase64String(imageBytes)
            }
        };
    }
}

/// <summary>
/// Source for image content.
/// </summary>
public sealed class ImageSource
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
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
/// Supports both text-only and image+text results.
/// </summary>
public sealed class ToolResultBlock : ContentBlock
{
    public override string Type => "tool_result";

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    /// <summary>
    /// The content can be either a string (text only) or a list of content blocks (for images).
    /// </summary>
    [JsonPropertyName("content")]
    public required object Content { get; init; }

    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; init; }

    /// <summary>
    /// Creates a tool result with text content only.
    /// </summary>
    public static ToolResultBlock FromText(string toolUseId, string text, bool isError = false)
    {
        return new ToolResultBlock
        {
            ToolUseId = toolUseId,
            Content = text,
            IsError = isError
        };
    }

    /// <summary>
    /// Creates a tool result with an image and optional text.
    /// Text is placed BEFORE the image so Claude sees metadata first.
    /// </summary>
    public static ToolResultBlock FromImage(string toolUseId, string base64, string mediaType, string? text = null)
    {
        var contentBlocks = new List<object>();

        // Add text block FIRST if provided (so Claude sees metadata before image)
        if (!string.IsNullOrEmpty(text))
        {
            contentBlocks.Add(new
            {
                type = "text",
                text = text
            });
        }

        // Then add the image
        contentBlocks.Add(new
        {
            type = "image",
            source = new
            {
                type = "base64",
                media_type = mediaType,
                data = base64
            }
        });

        return new ToolResultBlock
        {
            ToolUseId = toolUseId,
            Content = contentBlocks,
            IsError = false
        };
    }
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
