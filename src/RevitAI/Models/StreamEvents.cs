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
/// Base class for Server-Sent Events from the Claude streaming API.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MessageStartEvent), "message_start")]
[JsonDerivedType(typeof(ContentBlockStartEvent), "content_block_start")]
[JsonDerivedType(typeof(ContentBlockDeltaEvent), "content_block_delta")]
[JsonDerivedType(typeof(ContentBlockStopEvent), "content_block_stop")]
[JsonDerivedType(typeof(MessageDeltaEvent), "message_delta")]
[JsonDerivedType(typeof(MessageStopEvent), "message_stop")]
[JsonDerivedType(typeof(PingEvent), "ping")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
public abstract class StreamEvent
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Event indicating the start of a new message.
/// </summary>
public sealed class MessageStartEvent : StreamEvent
{
    public override string Type => "message_start";

    [JsonPropertyName("message")]
    public ClaudeResponse? Message { get; init; }
}

/// <summary>
/// Event indicating the start of a content block.
/// </summary>
public sealed class ContentBlockStartEvent : StreamEvent
{
    public override string Type => "content_block_start";

    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("content_block")]
    public ContentBlock? ContentBlock { get; init; }
}

/// <summary>
/// Event containing a delta (incremental update) to a content block.
/// </summary>
public sealed class ContentBlockDeltaEvent : StreamEvent
{
    public override string Type => "content_block_delta";

    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("delta")]
    public ContentDelta? Delta { get; init; }
}

/// <summary>
/// Delta update for a content block.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextDelta), "text_delta")]
[JsonDerivedType(typeof(InputJsonDelta), "input_json_delta")]
public abstract class ContentDelta
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Text delta - incremental text content.
/// </summary>
public sealed class TextDelta : ContentDelta
{
    public override string Type => "text_delta";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// JSON input delta for tool use blocks.
/// </summary>
public sealed class InputJsonDelta : ContentDelta
{
    public override string Type => "input_json_delta";

    [JsonPropertyName("partial_json")]
    public string PartialJson { get; init; } = string.Empty;
}

/// <summary>
/// Event indicating the end of a content block.
/// </summary>
public sealed class ContentBlockStopEvent : StreamEvent
{
    public override string Type => "content_block_stop";

    [JsonPropertyName("index")]
    public int Index { get; init; }
}

/// <summary>
/// Event containing updates to the message (e.g., stop reason, usage).
/// </summary>
public sealed class MessageDeltaEvent : StreamEvent
{
    public override string Type => "message_delta";

    [JsonPropertyName("delta")]
    public MessageDelta? Delta { get; init; }

    [JsonPropertyName("usage")]
    public Usage? Usage { get; init; }
}

/// <summary>
/// Delta update for the message.
/// </summary>
public sealed class MessageDelta
{
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; init; }
}

/// <summary>
/// Event indicating the end of the message stream.
/// </summary>
public sealed class MessageStopEvent : StreamEvent
{
    public override string Type => "message_stop";
}

/// <summary>
/// Ping event to keep the connection alive.
/// </summary>
public sealed class PingEvent : StreamEvent
{
    public override string Type => "ping";
}

/// <summary>
/// Error event from the streaming API.
/// </summary>
public sealed class ErrorEvent : StreamEvent
{
    public override string Type => "error";

    [JsonPropertyName("error")]
    public ClaudeError? Error { get; init; }
}

/// <summary>
/// Helper class for parsing SSE stream data.
/// </summary>
public static class StreamEventParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses a stream event from SSE data.
    /// </summary>
    /// <param name="eventType">The SSE event type (e.g., "message_start").</param>
    /// <param name="data">The JSON data.</param>
    /// <returns>The parsed event, or null if parsing fails.</returns>
    public static StreamEvent? Parse(string eventType, string data)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        try
        {
            // The data already contains the full event object with type field
            return JsonSerializer.Deserialize<StreamEvent>(data, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
