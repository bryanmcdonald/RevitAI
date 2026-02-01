# P1-08: Transaction Manager

**Goal**: Create robust transaction handling for model modifications with undo support.

**Prerequisites**: P1-07 complete.

**Key Files to Create**:
- `src/RevitAI/Transactions/TransactionManager.cs`
- `src/RevitAI/Transactions/TransactionScope.cs`

**Files to Modify**:
- `src/RevitAI/Tools/ToolDispatcher.cs` - Integrate TransactionManager for tools with `RequiresTransaction = true`

---

## P1-06 Integration Notes

Currently, `ToolDispatcher` returns an error for tools with `RequiresTransaction = true`:

```csharp
if (tool.RequiresTransaction)
{
    return new ToolResultBlock
    {
        ToolUseId = toolUse.Id,
        Content = "Tool requires transaction, but TransactionManager not yet implemented.",
        IsError = true
    };
}
```

This chunk should:
1. Create `TransactionManager` as a singleton or inject it into `ToolDispatcher`
2. Replace the error block with actual transaction handling
3. Support multi-tool transaction groups when Claude makes multiple tool calls

---

## Implementation Details

### 1. TransactionManager

```csharp
public class TransactionManager
{
    private TransactionGroup? _activeGroup;
    private readonly Stack<Transaction> _transactions = new();

    public void StartGroup(Document doc, string name)
    {
        _activeGroup = new TransactionGroup(doc, $"RevitAI: {name}");
        _activeGroup.Start();
    }

    public void CommitGroup()
    {
        _activeGroup?.Assimilate();
        _activeGroup = null;
    }

    public void RollbackGroup()
    {
        _activeGroup?.RollBack();
        _activeGroup = null;
    }

    public IDisposable StartTransaction(Document doc, string name)
    {
        var trans = new Transaction(doc, name);
        trans.Start();
        return new TransactionScope(trans, this);
    }
}
```

### 2. TransactionScope

IDisposable wrapper.

```csharp
public class TransactionScope : IDisposable
{
    private readonly Transaction _transaction;
    private bool _committed;

    public void Commit()
    {
        _transaction.Commit();
        _committed = true;
    }

    public void Dispose()
    {
        if (!_committed && _transaction.GetStatus() == TransactionStatus.Started)
        {
            _transaction.RollBack();
        }
    }
}
```

### 3. Integration with ToolDispatcher

```csharp
public async Task<ToolResult> DispatchAsync(...)
{
    if (tool.RequiresTransaction)
    {
        using var scope = _transactionManager.StartTransaction(doc, tool.Name);
        try
        {
            var result = await tool.ExecuteAsync(input, app, ct);
            if (result.Success)
                scope.Commit();
            return result;
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }
    // ...
}
```

### 4. Multi-Tool Transaction Groups

```csharp
// When Claude makes multiple tool calls in sequence:
_transactionManager.StartGroup(doc, "AI Operation");
try
{
    foreach (var toolCall in toolCalls)
    {
        await DispatchAsync(toolCall.Name, toolCall.Input, app);
    }
    _transactionManager.CommitGroup();
}
catch
{
    _transactionManager.RollbackGroup();
}
```

---

## Verification (Manual)

1. Build and deploy
2. Ask Claude to modify something (next chunk has modify tools)
3. Verify the modification appears
4. Press Ctrl+Z (Undo)
5. Verify the modification is undone as a single operation
6. Check that failed operations don't leave partial changes

---

## Implementation Notes (Completed)

**Status**: Complete

### Files Created

1. **`src/RevitAI/Transactions/TransactionScope.cs`**
   - IDisposable wrapper around Revit `Transaction`
   - Auto-rollback on dispose if not explicitly committed
   - Internal constructor (only `TransactionManager` creates instances)
   - Properties: `Name`, `IsCommitted`, `Status`
   - Methods: `Commit()`, `Rollback()`, `Dispose()`

2. **`src/RevitAI/Transactions/TransactionManager.cs`**
   - Singleton pattern (matches `ToolRegistry`, `ConfigurationService`)
   - Transaction group support for batching consecutive tool calls
   - Methods:
     - `StartGroup(doc, name)` - Opens a `TransactionGroup`
     - `CommitGroup()` - Calls `Assimilate()` for single undo
     - `RollbackGroup()` - Undoes all changes in group
     - `EnsureGroupClosed()` - Safe cleanup (no-throw)
     - `StartTransaction(doc, name)` - Returns `TransactionScope`
   - Property: `IsGroupActive`

3. **`src/RevitAI/Commands/TestTransactionCommand.cs`**
   - DEBUG-only test command with 6 tests:
     - Single transaction commit
     - Single transaction auto-rollback
     - Transaction group commit
     - Transaction group rollback
     - IsGroupActive tracking
     - EnsureGroupClosed safe cleanup

### Files Modified

1. **`src/RevitAI/Tools/ToolDispatcher.cs`**
   - Added `TransactionManager` dependency injection
   - Replaced placeholder error block with actual transaction handling
   - Added `ExecuteToolAsync` helper that wraps tools in transactions
   - Modified `DispatchAllAsync` for automatic batching:
     - Starts `TransactionGroup` if any tools require transactions
     - Commits group when all tools succeed
     - Rolls back entire group if any tool fails
     - Skips remaining tools after failure

2. **`src/RevitAI/App.cs`**
   - Added "Test Transactions" button in DEBUG build

### Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Batching trigger | Automatic | Consecutive tool calls in one Claude response = one undo |
| Transaction naming | "AI: {ToolName}" | Clear identification in undo history |
| Failure behavior | Rollback entire group | Prevents partial changes from confusing users |
| Manager pattern | Singleton | Consistent with existing services |

### Transaction Flow

```
Claude response with tool_use(s)
    │
    ▼
DispatchAllAsync()
    │
    ├─ StartGroup("Tool Batch")        ← if any tool requires transaction and >1 tool
    │
    ├─ For each tool:
    │      │
    │      ▼
    │   DispatchAsync()
    │      │
    │      ├─ ExecuteToolAsync()
    │      │      │
    │      │      ├─ StartTransaction("AI: {ToolName}")
    │      │      │
    │      │      ├─ tool.ExecuteAsync()
    │      │      │
    │      │      ├─ scope.Commit() on success
    │      │      │
    │      │      └─ auto-rollback on failure/exception
    │      │
    │      └─ Return ToolResultBlock
    │
    ├─ CommitGroup() if all succeeded  ← Assimilate() combines into single undo
    │
    └─ RollbackGroup() on any failure  ← Undoes everything
```
