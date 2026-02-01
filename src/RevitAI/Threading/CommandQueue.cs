using System.Collections.Concurrent;

namespace RevitAI.Threading;

/// <summary>
/// Thread-safe queue for RevitCommand instances.
/// Commands are enqueued from background threads and dequeued on the Revit main thread.
/// </summary>
public sealed class CommandQueue
{
    private readonly ConcurrentQueue<RevitCommand> _queue = new();

    /// <summary>
    /// Gets the number of commands currently in the queue.
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Gets whether the queue is empty.
    /// </summary>
    public bool IsEmpty => _queue.IsEmpty;

    /// <summary>
    /// Adds a command to the queue.
    /// </summary>
    /// <param name="command">The command to enqueue.</param>
    public void Enqueue(RevitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _queue.Enqueue(command);
    }

    /// <summary>
    /// Attempts to remove and return the command at the beginning of the queue.
    /// </summary>
    /// <param name="command">The dequeued command, or null if the queue is empty.</param>
    /// <returns>True if a command was successfully dequeued; otherwise, false.</returns>
    public bool TryDequeue(out RevitCommand? command)
    {
        return _queue.TryDequeue(out command);
    }

    /// <summary>
    /// Cancels all pending commands in the queue.
    /// Used during shutdown to ensure clean termination.
    /// </summary>
    public void CancelAll()
    {
        while (_queue.TryDequeue(out var command))
        {
            try
            {
                command.SetCancelled();
            }
            catch
            {
                // Ignore exceptions during cancellation - we're shutting down
            }
        }
    }
}
