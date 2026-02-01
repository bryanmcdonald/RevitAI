# P2-07: Conversation Memory

**Goal**: Persist conversation history per project and enable session-level change tracking.

**Prerequisites**: P2-06 complete.

**Key Files to Create**:
- `src/RevitAI/Services/ConversationMemoryService.cs`
- `src/RevitAI/Services/ChangeTracker.cs`
- `src/RevitAI/Models/PersistedConversation.cs`

---

## Implementation Details

> *This is a preliminary outline. Detailed implementation will be added during the chunk planning session.*

### 1. ConversationMemoryService

```csharp
public class ConversationMemoryService
{
    private readonly string _storageDir;

    public async Task SaveConversationAsync(string projectGuid, List<Message> messages)
    {
        var path = Path.Combine(_storageDir, $"{projectGuid}.json");
        var data = new PersistedConversation
        {
            ProjectGuid = projectGuid,
            Messages = messages,
            LastUpdated = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data));
    }

    public async Task<List<Message>> LoadConversationAsync(string projectGuid)
    {
        var path = Path.Combine(_storageDir, $"{projectGuid}.json");
        if (!File.Exists(path)) return new List<Message>();

        var json = await File.ReadAllTextAsync(path);
        var data = JsonSerializer.Deserialize<PersistedConversation>(json);
        return data?.Messages ?? new List<Message>();
    }
}
```

### 2. ChangeTracker

Track model modifications made by AI with undo support.

```csharp
public class ChangeTracker
{
    private readonly List<ModelChange> _changes = new();
    private readonly List<string> _transactionGroupNames = new();

    public void RecordChange(ChangeType type, ElementId elementId, string description)
    {
        _changes.Add(new ModelChange
        {
            Type = type,
            ElementId = elementId.Value,
            Description = description,
            Timestamp = DateTime.UtcNow
        });
    }

    public void RecordTransactionGroup(string groupName)
    {
        _transactionGroupNames.Add(groupName);
    }

    public string GetSessionSummary()
    {
        var summary = new StringBuilder();
        summary.AppendLine("Changes made this session:");
        foreach (var group in _changes.GroupBy(c => c.Type))
        {
            summary.AppendLine($"- {group.Key}: {group.Count()} elements");
        }
        return summary.ToString();
    }

    public int GetUndoCount() => _transactionGroupNames.Count;

    public void Clear()
    {
        _changes.Clear();
        _transactionGroupNames.Clear();
    }
}

public class ModelChange
{
    public ChangeType Type { get; set; }
    public long ElementId { get; set; }
    public string Description { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum ChangeType { Created, Modified, Deleted }
```

### 3. Undo All AI Changes Tool

```csharp
// Tool: undo_all_ai_changes
// Allows user to undo all changes made by AI in current session
public class UndoAllAiChangesTool : IRevitTool
{
    public string Name => "undo_all_ai_changes";
    public string Description => "Undoes all changes made by the AI assistant in this session";
    public bool RequiresTransaction => false;

    private readonly ChangeTracker _changeTracker;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken ct)
    {
        var undoCount = _changeTracker.GetUndoCount();
        if (undoCount == 0)
        {
            return Task.FromResult(ToolResult.Ok("No AI changes to undo in this session."));
        }

        // Revit undo is performed via multiple Ctrl+Z commands
        // Each TransactionGroup we created becomes one undo step
        var doc = app.ActiveUIDocument.Document;

        for (int i = 0; i < undoCount; i++)
        {
            // Note: Revit API doesn't expose direct undo, but we can inform user
        }

        return Task.FromResult(ToolResult.Ok(
            $"To undo all {undoCount} AI operations, press Ctrl+Z {undoCount} times, " +
            $"or use Edit > Undo (AI Operation) repeatedly."));
    }
}
```

### 4. UI: Undo All AI Changes Button

```xaml
<!-- Add to ChatPane.xaml toolbar -->
<Button Content="Undo All AI Changes"
        Command="{Binding UndoAllCommand}"
        ToolTip="{Binding UndoButtonTooltip}"
        Visibility="{Binding HasAiChanges, Converter={StaticResource BoolToVisibility}}"/>
```

```csharp
// ChatViewModel
public bool HasAiChanges => _changeTracker.GetUndoCount() > 0;
public string UndoButtonTooltip => $"Undo {_changeTracker.GetUndoCount()} AI operations";

public ICommand UndoAllCommand => new RelayCommand(() =>
{
    var count = _changeTracker.GetUndoCount();
    var result = MessageBox.Show(
        $"This will undo {count} AI operations. Continue?",
        "Undo All AI Changes",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

    if (result == MessageBoxResult.Yes)
    {
        // Inform user to use Ctrl+Z
        Messages.Add(new ChatMessage
        {
            Role = "system",
            Content = $"To undo all AI changes, press Ctrl+Z {count} times."
        });
    }
});
```

### 5. Integration with Tool Execution

```csharp
// After successful tool execution
if (tool.RequiresTransaction && result.Success)
{
    _changeTracker.RecordChange(
        ChangeType.Created, // or Modified, Deleted
        newElementId,
        $"{tool.Name}: {result.Content}");
}

// After TransactionGroup commit
_changeTracker.RecordTransactionGroup(groupName);
```

### 6. Context Enhancement

```csharp
// Include recent changes in system prompt
var recentChanges = _changeTracker.GetRecentChanges(5);
systemPrompt += $"\n\n## Recent AI Actions:\n{FormatChanges(recentChanges)}";
```

### 7. Auto-save on Document Close

```csharp
app.ControlledApplication.DocumentClosing += (sender, args) =>
{
    var projectGuid = args.Document.GetCloudModelPath()?.GetProjectGUID().ToString()
        ?? args.Document.Title;
    _memoryService.SaveConversationAsync(projectGuid, _messages).Wait();
};
```

---

## Verification (Manual)

1. Open a project, have a conversation with Claude, make some changes
2. Close and reopen the project
3. Verify conversation history is restored
4. Ask Claude "What changes have you made in this session?"
5. Verify Claude can summarize recent modifications
