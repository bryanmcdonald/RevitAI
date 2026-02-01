using Autodesk.Revit.UI;

namespace RevitAI.Threading;

/// <summary>
/// IExternalEventHandler implementation that processes commands from the CommandQueue.
/// Executes on the Revit main thread when the ExternalEvent is raised.
/// </summary>
public sealed class RevitEventHandler : IExternalEventHandler
{
    private readonly CommandQueue _queue;

    /// <summary>
    /// Maximum number of commands to process per Execute call.
    /// Prevents UI freeze if many commands are queued.
    /// </summary>
    public const int MaxCommandsPerExecution = 50;

    /// <summary>
    /// Creates a new RevitEventHandler with the specified command queue.
    /// </summary>
    /// <param name="queue">The command queue to process.</param>
    public RevitEventHandler(CommandQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>
    /// Called by Revit when the ExternalEvent is raised.
    /// Drains commands from the queue and executes them.
    /// </summary>
    /// <param name="app">The UIApplication instance.</param>
    public void Execute(UIApplication app)
    {
        var commandsProcessed = 0;

        while (_queue.TryDequeue(out var command) && commandsProcessed < MaxCommandsPerExecution)
        {
            // Check for cancellation before executing
            if (command.CancellationToken.IsCancellationRequested)
            {
                command.SetCancelled();
                commandsProcessed++;
                continue;
            }

            try
            {
                command.Execute(app);
            }
            catch (Exception ex)
            {
                // Route exceptions to the command's TaskCompletionSource
                command.SetException(ex);
            }

            commandsProcessed++;
        }

        // If there are still commands in the queue, they'll be processed
        // in the next ExternalEvent cycle (re-entrancy support)
    }

    /// <summary>
    /// Returns the name of this handler for Revit's internal tracking.
    /// </summary>
    public string GetName() => "RevitAI Command Handler";
}
