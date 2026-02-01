# P1-08: Transaction Manager

**Goal**: Create robust transaction handling for model modifications with undo support.

**Prerequisites**: P1-07 complete.

**Key Files to Create**:
- `src/RevitAI/Transactions/TransactionManager.cs`
- `src/RevitAI/Transactions/TransactionScope.cs`

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
