using Autodesk.Revit.UI;

namespace RevitAI.Threading;

/// <summary>
/// Abstract base class for commands that execute on the Revit main thread.
/// Commands are queued and executed via ExternalEvent to ensure thread safety.
/// </summary>
public abstract class RevitCommand
{
    /// <summary>
    /// Cancellation token for cooperative cancellation support.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    protected RevitCommand(CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Executes the command on the Revit main thread.
    /// </summary>
    /// <param name="app">The UIApplication instance.</param>
    public abstract void Execute(UIApplication app);

    /// <summary>
    /// Marks the command as completed successfully.
    /// </summary>
    internal abstract void SetCompleted();

    /// <summary>
    /// Marks the command as failed with an exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    internal abstract void SetException(Exception exception);

    /// <summary>
    /// Marks the command as cancelled.
    /// </summary>
    internal abstract void SetCancelled();

    /// <summary>
    /// Waits for the command to complete asynchronously.
    /// </summary>
    /// <returns>A task that completes when the command finishes.</returns>
    public abstract Task WaitAsync();
}

/// <summary>
/// A command that executes a function and returns a value.
/// </summary>
/// <typeparam name="T">The return type of the function.</typeparam>
public sealed class FuncCommand<T> : RevitCommand
{
    private readonly Func<UIApplication, T> _func;
    private readonly TaskCompletionSource<T> _tcs;

    /// <summary>
    /// Creates a new FuncCommand with the specified function.
    /// </summary>
    /// <param name="func">The function to execute on the Revit thread.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public FuncCommand(Func<UIApplication, T> func, CancellationToken cancellationToken = default)
        : base(cancellationToken)
    {
        _func = func ?? throw new ArgumentNullException(nameof(func));
        // RunContinuationsAsynchronously prevents deadlocks by ensuring continuations
        // don't run synchronously on the Revit thread
        _tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Gets the task that represents the asynchronous operation.
    /// </summary>
    public Task<T> Task => _tcs.Task;

    public override void Execute(UIApplication app)
    {
        // Check for cancellation before executing
        if (CancellationToken.IsCancellationRequested)
        {
            SetCancelled();
            return;
        }

        try
        {
            var result = _func(app);
            _tcs.TrySetResult(result);
        }
        catch (OperationCanceledException)
        {
            SetCancelled();
        }
        catch (Exception ex)
        {
            SetException(ex);
        }
    }

    internal override void SetCompleted()
    {
        // For FuncCommand, completion is handled by setting the result in Execute
        // This is a fallback that sets default value
        _tcs.TrySetResult(default!);
    }

    internal override void SetException(Exception exception)
    {
        _tcs.TrySetException(exception);
    }

    internal override void SetCancelled()
    {
        _tcs.TrySetCanceled(CancellationToken);
    }

    public override Task WaitAsync() => _tcs.Task;
}

/// <summary>
/// A command that executes an action without returning a value.
/// </summary>
public sealed class ActionCommand : RevitCommand
{
    private readonly Action<UIApplication> _action;
    private readonly TaskCompletionSource _tcs;

    /// <summary>
    /// Creates a new ActionCommand with the specified action.
    /// </summary>
    /// <param name="action">The action to execute on the Revit thread.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public ActionCommand(Action<UIApplication> action, CancellationToken cancellationToken = default)
        : base(cancellationToken)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        // RunContinuationsAsynchronously prevents deadlocks by ensuring continuations
        // don't run synchronously on the Revit thread
        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Gets the task that represents the asynchronous operation.
    /// </summary>
    public Task Task => _tcs.Task;

    public override void Execute(UIApplication app)
    {
        // Check for cancellation before executing
        if (CancellationToken.IsCancellationRequested)
        {
            SetCancelled();
            return;
        }

        try
        {
            _action(app);
            _tcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            SetCancelled();
        }
        catch (Exception ex)
        {
            SetException(ex);
        }
    }

    internal override void SetCompleted()
    {
        _tcs.TrySetResult();
    }

    internal override void SetException(Exception exception)
    {
        _tcs.TrySetException(exception);
    }

    internal override void SetCancelled()
    {
        _tcs.TrySetCanceled(CancellationToken);
    }

    public override Task WaitAsync() => _tcs.Task;
}
