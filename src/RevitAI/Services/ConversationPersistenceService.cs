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
using System.Text.Json;
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
    public async Task SaveConversationAsync(IEnumerable<ChatMessage> messages, string? conversationId = null)
    {
        EnsureStorageExists();

        _currentConversationId = conversationId ?? _currentConversationId ?? Guid.NewGuid().ToString();

        var data = new ConversationData
        {
            Id = _currentConversationId,
            LastModified = DateTime.Now,
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

        // Set title from first user message if not set
        var firstUserMessage = data.Messages.FirstOrDefault(m => m.Role == "user");
        if (firstUserMessage != null)
        {
            data.Title = Truncate(firstUserMessage.Content, 50);
        }

        var filePath = GetConversationFilePath(_currentConversationId);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Loads a conversation from disk.
    /// </summary>
    public async Task<List<ChatMessage>> LoadConversationAsync(string conversationId)
    {
        var filePath = GetConversationFilePath(conversationId);

        if (!File.Exists(filePath))
        {
            return new List<ChatMessage>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<ConversationData>(json);

            if (data?.Messages == null)
            {
                return new List<ChatMessage>();
            }

            _currentConversationId = conversationId;

            return data.Messages.Select(m => new ChatMessage
            {
                Role = Enum.Parse<MessageRole>(m.Role, ignoreCase: true),
                Content = m.Content,
                Timestamp = m.Timestamp,
                Status = MessageStatus.Complete
            }).ToList();
        }
        catch
        {
            return new List<ChatMessage>();
        }
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
