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
/// IDisposable wrapper around a Revit Transaction.
/// Auto-rollback on dispose if not explicitly committed.
/// </summary>
/// <remarks>
/// Only <see cref="TransactionManager"/> should create instances of this class.
/// Use <see cref="TransactionManager.StartTransaction"/> to get a TransactionScope.
/// </remarks>
public sealed class TransactionScope : IDisposable
{
    private readonly Transaction _transaction;
    private bool _committed;
    private bool _disposed;

    /// <summary>
    /// Creates a new TransactionScope wrapping the given transaction.
    /// </summary>
    /// <param name="transaction">The transaction to wrap. Must be started.</param>
    internal TransactionScope(Transaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));

        if (_transaction.GetStatus() != TransactionStatus.Started)
        {
            throw new ArgumentException("Transaction must be started before wrapping in TransactionScope.", nameof(transaction));
        }
    }

    /// <summary>
    /// Gets the name of the wrapped transaction.
    /// </summary>
    public string Name => _transaction.GetName();

    /// <summary>
    /// Gets whether the transaction has been committed.
    /// </summary>
    public bool IsCommitted => _committed;

    /// <summary>
    /// Gets the current status of the wrapped transaction.
    /// </summary>
    public TransactionStatus Status => _transaction.GetStatus();

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the transaction is not in a started state.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this scope has been disposed.</exception>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_committed)
        {
            return;
        }

        if (_transaction.GetStatus() != TransactionStatus.Started)
        {
            throw new InvalidOperationException(
                $"Cannot commit transaction '{Name}': status is {_transaction.GetStatus()}, expected Started.");
        }

        var result = _transaction.Commit();
        if (result != TransactionStatus.Committed)
        {
            throw new InvalidOperationException(
                $"Failed to commit transaction '{Name}': result was {result}.");
        }

        _committed = true;
    }

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if this scope has been disposed.</exception>
    public void Rollback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_committed)
        {
            throw new InvalidOperationException(
                $"Cannot rollback transaction '{Name}': it has already been committed.");
        }

        if (_transaction.GetStatus() == TransactionStatus.Started)
        {
            _transaction.RollBack();
        }
    }

    /// <summary>
    /// Disposes the TransactionScope. If not committed, the transaction is rolled back.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_committed && _transaction.GetStatus() == TransactionStatus.Started)
        {
            _transaction.RollBack();
        }

        _transaction.Dispose();
    }
}
