# P2-03: Multi-Step Design Operations

**Goal**: Enable Claude to execute coordinated multi-tool sequences as single transactions.

**Prerequisites**: P2-02 complete.

**Key Files to Modify**:
- `src/RevitAI/Services/ClaudeApiService.cs` (multi-turn tool use)
- `src/RevitAI/Transactions/TransactionManager.cs`
- `src/RevitAI/Tools/ToolDispatcher.cs`

---

## Implementation Details

> *This is a preliminary outline. Detailed implementation will be added during the chunk planning session.*

### 1. Enhanced TransactionGroup Management

```csharp
// Track all tool calls within a single Claude response
public async Task ProcessClaudeResponseAsync(ClaudeResponse response)
{
    var toolCalls = response.Content.Where(c => c.Type == "tool_use").ToList();

    if (toolCalls.Count > 1)
    {
        _transactionManager.StartGroup(doc, "Multi-step AI Operation");
    }

    var results = new List<ToolResult>();
    foreach (var call in toolCalls)
    {
        var result = await _dispatcher.DispatchAsync(call.Name, call.Input, app);
        results.Add(result);

        if (!result.Success)
        {
            _transactionManager.RollbackGroup();
            break;
        }
    }

    if (results.All(r => r.Success))
        _transactionManager.CommitGroup();
}
```

### 2. Result Chaining

```csharp
// Pass previous tool results to Claude for dependent operations
// Claude requests: place columns at grid intersections
// Tool 1: get_grids -> returns grid data
// Tool 2: compute intersections (Claude) -> returns points
// Tool 3-N: place_column at each point
```

### 3. System Prompt Guidance for Multi-Step

```
When performing complex operations that require multiple elements:
1. First query the model to understand existing conditions
2. Plan the sequence of tool calls
3. Execute placement/modification tools in order
4. All related operations will be grouped for single undo
```

---

## Verification (Manual)

1. Ask Claude "Create a structural bay: 4 columns at 25' x 25' spacing, beams connecting them, and a floor slab on Level 1"
2. Verify all elements are created
3. Ctrl+Z should undo the entire operation at once
4. Ask Claude "Add columns at all grid intersections on Level 2"
