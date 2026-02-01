using System.Text.Json.Serialization;

namespace RevitAI.Models;

/// <summary>
/// Represents a single message in a conversation for JSON serialization.
/// </summary>
public class MessageData
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Represents a complete conversation for JSON serialization/deserialization.
/// </summary>
public class ConversationData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("created")]
    public DateTime Created { get; set; } = DateTime.Now;

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; } = DateTime.Now;

    [JsonPropertyName("messages")]
    public List<MessageData> Messages { get; set; } = new();

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
