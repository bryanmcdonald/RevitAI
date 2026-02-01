# P1-06: Tool Framework & Registry

**Status**: ✅ Complete

**Goal**: Create the infrastructure for defining, registering, and dispatching Claude tools.

**Prerequisites**: P1-05 complete.

**Files Created**:
- `src/RevitAI/Tools/IRevitTool.cs` - Interface for all tools
- `src/RevitAI/Tools/ToolResult.cs` - Immutable result wrapper
- `src/RevitAI/Tools/ToolRegistry.cs` - Singleton registry with ConcurrentDictionary
- `src/RevitAI/Tools/ToolDispatcher.cs` - Routes tool calls with Revit thread marshalling
- `src/RevitAI/Tools/EchoTool.cs` - Test tool to verify framework

**Files Modified**:
- `src/RevitAI/App.cs` - Added `RegisterTools()` method
- `src/RevitAI/UI/ChatViewModel.cs` - Added tool execution loop with `ResponseAccumulator`

**Note**: `ToolDefinition` already exists in `Models/ClaudeModels.cs` - reuse that class.

---

## Implementation Details

### 1. IRevitTool Interface

```csharp
public interface IRevitTool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    bool RequiresTransaction { get; }

    Task<ToolResult> ExecuteAsync(
        JsonElement input,
        UIApplication app,
        CancellationToken ct);
}
```

### 2. ToolDefinition

For Claude API.

```csharp
public class ToolDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public JsonElement InputSchema { get; set; }
}
```

### 3. ToolRegistry

```csharp
public class ToolRegistry
{
    private readonly Dictionary<string, IRevitTool> _tools = new();

    public void Register(IRevitTool tool) => _tools[tool.Name] = tool;
    public IRevitTool? Get(string name) => _tools.GetValueOrDefault(name);
    public IEnumerable<ToolDefinition> GetDefinitions() =>
        _tools.Values.Select(t => new ToolDefinition
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        });
}
```

### 4. ToolDispatcher

Handles tool_use from Claude response.

```csharp
public class ToolDispatcher
{
    public async Task<ToolResult> DispatchAsync(
        string toolName,
        JsonElement input,
        UIApplication app)
    {
        var tool = _registry.Get(toolName);
        if (tool == null)
            return ToolResult.Error($"Unknown tool: {toolName}");

        if (tool.RequiresTransaction)
        {
            return await ExecuteWithTransactionAsync(tool, input, app);
        }

        return await tool.ExecuteAsync(input, app, CancellationToken.None);
    }
}
```

### 5. ToolResult

```csharp
public class ToolResult
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public string? Error { get; set; }

    public static ToolResult Ok(string content) => new() { Success = true, Content = content };
    public static ToolResult Error(string error) => new() { Success = false, Error = error };
}
```

### 6. Simple Test Tool

```csharp
public class EchoTool : IRevitTool
{
    public string Name => "echo";
    public string Description => "Echoes back the input message (for testing)";
    public bool RequiresTransaction => false;
    // ...
}
```

---

## Verification (Manual)

1. Build and deploy
2. Open chat, ask Claude to use the echo tool
3. Verify Claude calls the tool and receives the result
4. Verify tool result appears in conversation

---

## Implementation Notes (Post-Completion)

### Key Architecture Decisions

1. **ToolRegistry is a singleton** - Access via `ToolRegistry.Instance`
2. **Thread marshalling** - `ToolDispatcher` uses `App.ExecuteOnRevitThreadAsync()` to ensure tools run on Revit's main thread
3. **Transaction guard** - Tools with `RequiresTransaction = true` return an error until P1-08 TransactionManager is implemented
4. **Streaming tool parsing** - `ChatViewModel.ResponseAccumulator` inner class accumulates streamed JSON deltas to build complete `ToolUseBlock` objects

### Tool Registration Pattern

Tools are registered in `App.RegisterTools()` called from `OnStartup`:

```csharp
private static void RegisterTools()
{
    var registry = ToolRegistry.Instance;
    registry.Register(new EchoTool());
    // Future tools registered here
}
```

### Creating New Tools

Use `EchoTool` as a template. Key requirements:
- Implement `IRevitTool` interface
- Use `JsonDocument.Parse()` for `InputSchema` (parse once in static constructor)
- Set `RequiresTransaction = true` for any tool that modifies the model
- Return `ToolResult.Ok()` or `ToolResult.Error()` from `ExecuteAsync`

### Chat Integration

The tool execution loop in `ChatViewModel.StreamClaudeResponseAsync`:
1. Streams response and accumulates content blocks
2. Detects `stop_reason: "tool_use"` from `MessageDeltaEvent`
3. Executes tools via `ToolDispatcher.DispatchAllAsync()`
4. Sends `ToolResultBlock` back to Claude
5. Loops until Claude stops using tools
