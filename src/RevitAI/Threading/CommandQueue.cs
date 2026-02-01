// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
