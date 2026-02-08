# P2-07: Conversation Memory

**Goal**: Persist conversation history per project and enable session-level change tracking.

**Prerequisites**: P2-06 complete.

**Status**: Complete

---

## Implementation Details

### Design Decisions

- **Evolve existing `ConversationPersistenceService`** rather than creating a parallel service
- **Auto-load** previous conversation when a project opens via `DocumentOpened` event
- **Auto-save** on `DocumentClosing` (synchronous event — uses sync file write)
- **Skip undo tool/button** — Revit has no programmatic undo API; users use Ctrl+Z
- **Summary approach** for API history: persist display messages + text summary of tool actions (tool call/result structure not preserved across sessions)
- **One-shot previous session injection**: tool action summary from last session injected once into the system prompt, then cleared to avoid repetition

### Key Files

| Action | File | Purpose |
|--------|------|---------|
| **Created** | `src/RevitAI/Services/ChangeTracker.cs` | Session-scoped change tracking singleton |
| **Modified** | `src/RevitAI/Models/ConversationData.cs` | Added `ProjectKey`, `ToolActionSummary` fields |
| **Modified** | `src/RevitAI/Services/ConversationPersistenceService.cs` | Project-key keying, tool summary persistence |
| **Modified** | `src/RevitAI/Tools/ToolDispatcher.cs` | Record changes after tool execution |
| **Modified** | `src/RevitAI/UI/ChatViewModel.cs` | Load/save project conversations, system prompt enrichment |
| **Modified** | `src/RevitAI/App.cs` | DocumentOpened/DocumentClosing event wiring |

### 1. ChangeTracker Service

Thread-safe singleton (`lock(_dataLock)` on all list operations) tracking AI-initiated changes:

```
Public API:
- RecordChange(ChangeType type, string toolName, long[] elementIds, string description)
- RecordTransactionGroup(string groupName)
- GetSessionSummary() -> string    // For system prompt (capped at 20 entries)
- GenerateToolActionSummary() -> string  // For persistence (detailed)
- GetRecentChanges(int count) -> List<ModelChange>
- Clear()

Types:
- enum ChangeType { Created, Modified, Deleted }
- class ModelChange { Type, ToolName, ElementIds, Description, Timestamp }
```

Change type is inferred from tool name prefix: `place_`/`create_`/`copy_`/`array_` = Created, `delete_` = Deleted, everything else = Modified.

### 2. ConversationData Extensions

Two new JSON properties:
- `projectKey`: Set only for project-keyed conversations (null for random-GUID conversations)
- `toolActionSummary`: Text summary of tool actions from the session

### 3. ConversationPersistenceService Enhancements

- **`GetProjectKey(Document doc)`** static method: derives stable key
  - Cloud models: `"cloud_{projectGUID}"`
  - Local files: `"local_{SHA256(pathName)[0..16]}"`
  - Untitled/unsaved: returns `null`
- **`SetProjectKey(string)`**: sets current conversation ID to project key
- **`HasConversation(string id)`**: quick `File.Exists` check
- **`SaveConversation(...)` (sync)**: for `DocumentClosing` where async is not viable
- **`LoadConversationWithSummaryAsync(...)`**: returns `(messages, toolActionSummary)` tuple
- Shared `BuildConversationData()` and `LoadConversationDataAsync()` helpers to avoid duplication

### 4. ToolDispatcher Integration

After successful tool execution (`scope.Commit()`), `RecordToolChange()` records the change:
- In single-tool dispatch (`ExecuteToolAsync`)
- In batch dispatch (`ExecuteAllToolsInGroupAsync`) — after each individual commit, plus a `RecordTransactionGroup("Tool Batch")` after group commit

### 5. ChatViewModel Integration

- **`LoadProjectConversationAsync(string projectKey)`**: loads messages + tool action summary, populates UI via WPF dispatcher, rebuilds API history as simple text pairs
- **`RebuildApiHistoryFromDisplayMessages(...)`**: converts display messages to `List<ClaudeMessage>`, merging consecutive same-role messages to maintain strict user/assistant alternation
- **`SaveCurrentConversation(string? projectKey)`**: snapshots messages on WPF thread (thread safety), saves synchronously with tool action summary
- **`BuildContextualSystemPromptAsync`**: appends "AI Session Changes" section (from `ChangeTracker.GetSessionSummary()`) and "Previous Session Actions" section (from loaded summary, cleared after first use)
- **`ClearConversation`**: also clears `ChangeTracker.Instance` and `_loadedToolActionSummary`
- **`SendAsync`**: passes project key and tool summary to `SaveConversationAsync`

### 6. App.cs Document Event Wiring

- `DocumentOpened`: gets project key, clears ChangeTracker, loads conversation on background thread via `Task.Run` with try/catch
- `DocumentClosing`: gets project key, calls `SaveCurrentConversation` synchronously

---

## Thread Safety Notes

- `ChangeTracker` uses `lock(_dataLock)` for all list operations (tool execution on Revit thread, reads from background API threads)
- `SaveCurrentConversation` snapshots `Messages` on WPF thread via `_dispatcher.Invoke()` before writing to disk from Revit thread
- `_loadedToolActionSummary` is set/cleared inside `_dispatcher.InvokeAsync` blocks to avoid cross-thread races

## Edge Cases

- **Untitled/unsaved documents**: `GetProjectKey()` returns null, all auto-load/save skipped. Random-GUID persistence still works as fallback.
- **Local file moved/renamed**: Hash-based key breaks the link. Acceptable trade-off, documented.
- **Corrupted save files**: Existing `try/catch` returns empty list/null. No additional handling needed.
- **Multiple documents open**: Conversation tracks the last-opened document. No auto-switch on active document change.
- **Empty conversation on load**: Skip loading if no messages in saved file.
- **API history alternation**: Consecutive same-role messages are merged when rebuilding history from display messages.

## Deferred Items

- **Programmatic undo tool**: Revit has no API for undo. Users use Ctrl+Z. Undo tracking (`_transactionGroupNames`) is maintained but not exposed as a user action.
- **Multi-document conversation switching**: Only single-document tracking implemented.
- **Preview graphics**: Not related to this chunk (deferred in P2-05).

---

## Verification (Manual)

1. Open a project, have a conversation, make AI changes (e.g., place elements)
2. Close and reopen the project
3. Verify conversation history is restored with "Previous conversation restored" message
4. Send a new message — verify AI references previous session actions in its response
5. Ask "What changes have you made?" — verify AI can summarize both current and previous session actions
6. Clear conversation, verify ChangeTracker is also cleared
7. Test with local file and cloud model (if available) — both should auto-persist
8. Test with untitled document — verify no errors, graceful fallback
9. Build succeeds with no warnings
