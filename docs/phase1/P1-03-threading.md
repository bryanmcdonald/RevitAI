# P1-03: ExternalEvent Threading Infrastructure

**Goal**: Create the threading infrastructure to safely marshal commands from background threads to Revit's main thread.

**Prerequisites**: P1-02 complete.

**Key Files to Create**:
- `src/RevitAI/Threading/RevitEventHandler.cs`
- `src/RevitAI/Threading/CommandQueue.cs`
- `src/RevitAI/Threading/RevitCommand.cs`

---

## Implementation Details

### 1. RevitCommand Base Class

```csharp
public abstract class RevitCommand
{
    public TaskCompletionSource<object?> Completion { get; } = new();
    public abstract void Execute(UIApplication app);
}
```

### 2. CommandQueue

Thread-safe queue for pending commands.

```csharp
public class CommandQueue
{
    private readonly ConcurrentQueue<RevitCommand> _queue = new();

    public void Enqueue(RevitCommand command) => _queue.Enqueue(command);
    public bool TryDequeue(out RevitCommand? command) => _queue.TryDequeue(out command);
}
```

### 3. RevitEventHandler

IExternalEventHandler implementation.

```csharp
public class RevitEventHandler : IExternalEventHandler
{
    private readonly CommandQueue _queue;

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var command))
        {
            try
            {
                command.Execute(app);
                command.Completion.SetResult(null);
            }
            catch (Exception ex)
            {
                command.Completion.SetException(ex);
            }
        }
    }

    public string GetName() => "RevitAI Event Handler";
}
```

### 4. Create ExternalEvent in App.OnStartup

```csharp
public static ExternalEvent? RevitEvent { get; private set; }
public static CommandQueue? CommandQueue { get; private set; }

public Result OnStartup(UIControlledApplication app)
{
    CommandQueue = new CommandQueue();
    var handler = new RevitEventHandler(CommandQueue);
    RevitEvent = ExternalEvent.Create(handler);
    // ...
}
```

### 5. Async Helper Method

```csharp
public static async Task<T> ExecuteOnRevitThreadAsync<T>(Func<UIApplication, T> action)
{
    var command = new FuncCommand<T>(action);
    CommandQueue.Enqueue(command);
    RevitEvent.Raise();
    return await command.Completion.Task;
}
```

---

## Verification (Manual)

1. Add a test button that queues a command to show a TaskDialog
2. Build and deploy
3. Click the test button
4. Verify TaskDialog appears (proves command executed on main thread)
5. Verify no Revit crash or threading errors
