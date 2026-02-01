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

using Autodesk.Revit.DB;

namespace RevitAI.Transactions;

/// <summary>
/// Manages Revit transactions for AI tool operations.
/// Supports transaction groups to batch consecutive tool calls into a single undo operation.
/// </summary>
/// <remarks>
/// Thread safety: This class is designed to be used from the Revit main thread only.
/// All Revit API calls must be made from the main thread via ExternalEvent.
/// </remarks>
public sealed class TransactionManager
{
    private static TransactionManager? _instance;
    private static readonly object _lock = new();

    private TransactionGroup? _activeGroup;
    private Document? _groupDocument;

    /// <summary>
    /// Gets the singleton instance of the TransactionManager.
    /// </summary>
    public static TransactionManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new TransactionManager();
                }
            }
            return _instance;
        }
    }

    private TransactionManager() { }

    /// <summary>
    /// Gets whether a transaction group is currently active.
    /// </summary>
    public bool IsGroupActive => _activeGroup != null;

    /// <summary>
    /// Gets the document associated with the active group, or null if no group is active.
    /// </summary>
    public Document? GroupDocument => _groupDocument;

    /// <summary>
    /// Starts a new transaction group for batching multiple tool operations.
    /// </summary>
    /// <param name="doc">The document to operate on.</param>
    /// <param name="name">The name for the transaction group (appears in undo history).</param>
    /// <exception cref="ArgumentNullException">Thrown if doc is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a group is already active.</exception>
    public void StartGroup(Document doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (_activeGroup != null)
        {
            throw new InvalidOperationException(
                $"Cannot start a new transaction group '{name}': a group is already active. " +
                "Call CommitGroup() or RollbackGroup() first.");
        }

        _groupDocument = doc;
        _activeGroup = new TransactionGroup(doc, $"AI: {name}");
        var result = _activeGroup.Start();

        if (result != TransactionStatus.Started)
        {
            _activeGroup.Dispose();
            _activeGroup = null;
            _groupDocument = null;
            throw new InvalidOperationException(
                $"Failed to start transaction group '{name}': result was {result}.");
        }
    }

    /// <summary>
    /// Commits the active transaction group, assimilating all transactions into a single undo.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no group is active.</exception>
    public void CommitGroup()
    {
        if (_activeGroup == null)
        {
            throw new InvalidOperationException("No transaction group is active to commit.");
        }

        try
        {
            // Assimilate merges all transactions in the group into a single undo operation
            var result = _activeGroup.Assimilate();
            if (result != TransactionStatus.Committed)
            {
                throw new InvalidOperationException(
                    $"Failed to commit transaction group: result was {result}.");
            }
        }
        finally
        {
            _activeGroup.Dispose();
            _activeGroup = null;
            _groupDocument = null;
        }
    }

    /// <summary>
    /// Rolls back the active transaction group, undoing all changes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no group is active.</exception>
    public void RollbackGroup()
    {
        if (_activeGroup == null)
        {
            throw new InvalidOperationException("No transaction group is active to rollback.");
        }

        try
        {
            _activeGroup.RollBack();
        }
        finally
        {
            _activeGroup.Dispose();
            _activeGroup = null;
            _groupDocument = null;
        }
    }

    /// <summary>
    /// Ensures any active transaction group is closed. Safe to call even if no group is active.
    /// If a group is active, it will be rolled back.
    /// </summary>
    /// <remarks>
    /// Use this for cleanup in finally blocks or error handling paths.
    /// This method does not throw exceptions.
    /// </remarks>
    public void EnsureGroupClosed()
    {
        if (_activeGroup == null)
        {
            return;
        }

        try
        {
            if (_activeGroup.GetStatus() == TransactionStatus.Started)
            {
                _activeGroup.RollBack();
            }
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
        finally
        {
            try
            {
                _activeGroup.Dispose();
            }
            catch
            {
                // Swallow exceptions during dispose
            }

            _activeGroup = null;
            _groupDocument = null;
        }
    }

    /// <summary>
    /// Starts a new transaction within the current context.
    /// If a transaction group is active, the transaction will be part of that group.
    /// </summary>
    /// <param name="doc">The document to operate on.</param>
    /// <param name="name">The name for the transaction (appears in undo history if no group active).</param>
    /// <returns>A TransactionScope that must be disposed. Auto-rollback if not committed.</returns>
    /// <exception cref="ArgumentNullException">Thrown if doc is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the document doesn't match the group's document.</exception>
    public TransactionScope StartTransaction(Document doc, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);

        // If a group is active, verify we're using the same document
        if (_activeGroup != null && _groupDocument != null && !ReferenceEquals(doc, _groupDocument))
        {
            throw new InvalidOperationException(
                "Cannot start a transaction on a different document than the active transaction group.");
        }

        var transaction = new Transaction(doc, $"AI: {name}");
        var result = transaction.Start();

        if (result != TransactionStatus.Started)
        {
            transaction.Dispose();
            throw new InvalidOperationException(
                $"Failed to start transaction '{name}': result was {result}.");
        }

        return new TransactionScope(transaction);
    }
}
