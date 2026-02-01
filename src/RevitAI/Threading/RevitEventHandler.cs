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
