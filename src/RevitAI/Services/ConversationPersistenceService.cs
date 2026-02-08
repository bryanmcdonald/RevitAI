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

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using RevitAI.Models;
using RevitAI.UI;

namespace RevitAI.Services;

/// <summary>
/// Handles saving and loading conversation history to/from disk.
/// Conversations are stored as JSON files in %APPDATA%\RevitAI\conversations\
/// </summary>
public class ConversationPersistenceService
{
    private static readonly string ConversationsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RevitAI",
        "conversations");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private string? _currentConversationId;

    /// <summary>
    /// Gets the current conversation ID.
    /// </summary>
    public string? CurrentConversationId => _currentConversationId;

    /// <summary>
    /// Derives a stable project key from a Revit document.
    /// Cloud models use project GUID; local files use a hash of the path.
    /// Returns null for untitled/unsaved documents.
    /// </summary>
    public static string? GetProjectKey(Document doc)
    {
        if (doc == null)
            return null;

        try
        {
            // Try cloud model first
            var cloudPath = doc.GetCloudModelPath();
            if (cloudPath != null)
            {
                var projectGuid = cloudPath.GetProjectGUID();
                if (projectGuid != Guid.Empty)
                {
                    return $"cloud_{projectGuid}";
                }
            }
        }
        catch
        {
            // Not a cloud model, fall through
        }

        // Local file - hash the path for a stable key
        var pathName = doc.PathName;
        if (string.IsNullOrEmpty(pathName))
            return null; // Untitled/unsaved

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(pathName));
        var hashHex = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
        return $"local_{hashHex}";
    }

    /// <summary>
    /// Sets the current conversation ID to a project key for project-keyed persistence.
    /// </summary>
    public void SetProjectKey(string projectKey)
    {
        _currentConversationId = projectKey;
    }

    /// <summary>
    /// Checks whether a conversation file exists for the given ID.
    /// </summary>
    public bool HasConversation(string id)
    {
        return File.Exists(GetConversationFilePath(id));
    }

    /// <summary>
    /// Ensures the conversations folder exists.
    /// </summary>
    public void EnsureStorageExists()
    {
        if (!Directory.Exists(ConversationsFolder))
        {
            Directory.CreateDirectory(ConversationsFolder);
        }
    }

    /// <summary>
    /// Saves the current conversation to disk.
    /// </summary>
    /// <param name="messages">The chat messages to save.</param>
    /// <param name="conversationId">Optional conversation ID override.</param>
    /// <param name="toolActionSummary">Optional summary of tool actions for session memory.</param>
    public async Task SaveConversationAsync(IEnumerable<ChatMessage> messages, string? conversationId = null, string? toolActionSummary = null)
    {
        EnsureStorageExists();

        var isProjectKeyed = conversationId != null;
        _currentConversationId = conversationId ?? _currentConversationId ?? Guid.NewGuid().ToString();

        var data = BuildConversationData(messages, isProjectKeyed ? _currentConversationId : null, toolActionSummary);

        var filePath = GetConversationFilePath(_currentConversationId);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Saves the conversation synchronously. Used during DocumentClosing where async is not viable.
    /// </summary>
    public void SaveConversation(IEnumerable<ChatMessage> messages, string? conversationId = null, string? toolActionSummary = null)
    {
        EnsureStorageExists();

        var isProjectKeyed = conversationId != null;
        _currentConversationId = conversationId ?? _currentConversationId ?? Guid.NewGuid().ToString();

        var data = BuildConversationData(messages, isProjectKeyed ? _currentConversationId : null, toolActionSummary);

        var filePath = GetConversationFilePath(_currentConversationId);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    private ConversationData BuildConversationData(IEnumerable<ChatMessage> messages, string? projectKey, string? toolActionSummary)
    {
        var data = new ConversationData
        {
            Id = _currentConversationId!,
            LastModified = DateTime.Now,
            ProjectKey = projectKey,
            ToolActionSummary = toolActionSummary,
            Messages = messages
                .Where(m => m.Role != MessageRole.System) // Don't persist system messages
                .Select(m => new MessageData
                {
                    Role = m.Role.ToString().ToLowerInvariant(),
                    Content = m.Content,
                    Timestamp = m.Timestamp
                })
                .ToList()
        };

        var firstUserMessage = data.Messages.FirstOrDefault(m => m.Role == "user");
        if (firstUserMessage != null)
        {
            data.Title = Truncate(firstUserMessage.Content, 50);
        }

        return data;
    }

    /// <summary>
    /// Loads a conversation from disk.
    /// </summary>
    public async Task<List<ChatMessage>> LoadConversationAsync(string conversationId)
    {
        var data = await LoadConversationDataAsync(conversationId);
        if (data == null)
            return new List<ChatMessage>();

        return ConvertToMessages(data.Messages);
    }

    /// <summary>
    /// Loads a conversation along with its tool action summary.
    /// </summary>
    /// <returns>A tuple of (messages, toolActionSummary).</returns>
    public async Task<(List<ChatMessage> Messages, string? ToolActionSummary)> LoadConversationWithSummaryAsync(string conversationId)
    {
        var data = await LoadConversationDataAsync(conversationId);
        if (data == null)
            return (new List<ChatMessage>(), null);

        return (ConvertToMessages(data.Messages), data.ToolActionSummary);
    }

    private async Task<ConversationData?> LoadConversationDataAsync(string conversationId)
    {
        var filePath = GetConversationFilePath(conversationId);

        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<ConversationData>(json);

            if (data?.Messages == null || data.Messages.Count == 0)
                return null;

            _currentConversationId = conversationId;
            return data;
        }
        catch
        {
            return null;
        }
    }

    private static List<ChatMessage> ConvertToMessages(List<MessageData> messageData)
    {
        return messageData.Select(m => new ChatMessage
        {
            Role = Enum.Parse<MessageRole>(m.Role, ignoreCase: true),
            Content = m.Content,
            Timestamp = m.Timestamp,
            Status = MessageStatus.Complete
        }).ToList();
    }

    /// <summary>
    /// Lists all saved conversations.
    /// </summary>
    public async Task<List<ConversationData>> ListConversationsAsync()
    {
        EnsureStorageExists();

        var conversations = new List<ConversationData>();
        var files = Directory.GetFiles(ConversationsFolder, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var data = JsonSerializer.Deserialize<ConversationData>(json);
                if (data != null)
                {
                    conversations.Add(data);
                }
            }
            catch
            {
                // Skip corrupted files
            }
        }

        return conversations.OrderByDescending(c => c.LastModified).ToList();
    }

    /// <summary>
    /// Deletes a conversation from disk.
    /// </summary>
    public void DeleteConversation(string conversationId)
    {
        var filePath = GetConversationFilePath(conversationId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        if (_currentConversationId == conversationId)
        {
            _currentConversationId = null;
        }
    }

    /// <summary>
    /// Starts a new conversation (clears current ID).
    /// </summary>
    public void StartNewConversation()
    {
        _currentConversationId = null;
    }

    private static string GetConversationFilePath(string conversationId)
    {
        return Path.Combine(ConversationsFolder, $"{conversationId}.json");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
