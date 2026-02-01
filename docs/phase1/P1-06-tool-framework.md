# P1-06: Tool Framework & Registry

**Goal**: Create the infrastructure for defining, registering, and dispatching Claude tools.

**Prerequisites**: P1-05 complete.

**Key Files to Create**:
- `src/RevitAI/Tools/IRevitTool.cs`
- `src/RevitAI/Tools/ToolDefinition.cs`
- `src/RevitAI/Tools/ToolRegistry.cs`
- `src/RevitAI/Tools/ToolDispatcher.cs`
- `src/RevitAI/Tools/ToolResult.cs`

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
