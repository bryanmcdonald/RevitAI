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

using System.Text;

namespace RevitAI.Services;

/// <summary>
/// Type of model change made by an AI tool.
/// </summary>
public enum ChangeType
{
    Created,
    Modified,
    Deleted
}

/// <summary>
/// Represents a single model change made by an AI tool.
/// </summary>
public sealed class ModelChange
{
    public required ChangeType Type { get; init; }
    public required string ToolName { get; init; }
    public required long[] ElementIds { get; init; }
    public required string Description { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

/// <summary>
/// Tracks AI-initiated model changes per session.
/// Thread-safe singleton used by ToolDispatcher (Revit thread) and ChatViewModel (background threads).
/// </summary>
public sealed class ChangeTracker
{
    private static ChangeTracker? _instance;
    private static readonly object _instanceLock = new();

    private readonly List<ModelChange> _changes = new();
    private readonly List<string> _transactionGroupNames = new();
    private readonly object _dataLock = new();

    private const int MaxSummaryChanges = 20;

    public static ChangeTracker Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new ChangeTracker();
                }
            }
            return _instance;
        }
    }

    private ChangeTracker() { }

    /// <summary>
    /// Records a model change from a tool execution.
    /// </summary>
    public void RecordChange(ChangeType type, string toolName, long[] elementIds, string description)
    {
        lock (_dataLock)
        {
            _changes.Add(new ModelChange
            {
                Type = type,
                ToolName = toolName,
                ElementIds = elementIds,
                Description = description
            });
        }
    }

    /// <summary>
    /// Records a transaction group commit (batch operation).
    /// </summary>
    public void RecordTransactionGroup(string groupName)
    {
        lock (_dataLock)
        {
            _transactionGroupNames.Add(groupName);
        }
    }

    /// <summary>
    /// Gets a compact summary of recent changes for the system prompt.
    /// Capped at <see cref="MaxSummaryChanges"/> most recent changes.
    /// </summary>
    public string GetSessionSummary()
    {
        lock (_dataLock)
        {
            if (_changes.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            var recent = _changes.Count > MaxSummaryChanges
                ? _changes.Skip(_changes.Count - MaxSummaryChanges).ToList()
                : _changes;

            foreach (var change in recent)
            {
                var idList = change.ElementIds.Length switch
                {
                    0 => "",
                    1 => $" [ID: {change.ElementIds[0]}]",
                    _ => $" [{change.ElementIds.Length} elements]"
                };
                sb.AppendLine($"- {change.Type}: {change.ToolName}{idList} - {Truncate(change.Description, 80)}");
            }

            if (_changes.Count > MaxSummaryChanges)
            {
                sb.AppendLine($"(showing {MaxSummaryChanges} of {_changes.Count} total changes)");
            }

            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Generates a detailed summary of all tool actions for persistence.
    /// </summary>
    public string GenerateToolActionSummary()
    {
        lock (_dataLock)
        {
            if (_changes.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            var grouped = _changes.GroupBy(c => c.Type);
            foreach (var group in grouped)
            {
                var totalElements = group.Sum(c => c.ElementIds.Length);
                sb.AppendLine($"{group.Key}: {group.Count()} operations ({totalElements} elements)");
                foreach (var change in group)
                {
                    sb.AppendLine($"  - {change.ToolName}: {Truncate(change.Description, 100)}");
                }
            }

            if (_transactionGroupNames.Count > 0)
            {
                sb.AppendLine($"Transaction groups: {_transactionGroupNames.Count}");
            }

            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Gets the most recent changes.
    /// </summary>
    public List<ModelChange> GetRecentChanges(int count)
    {
        lock (_dataLock)
        {
            return _changes.Count <= count
                ? new List<ModelChange>(_changes)
                : _changes.Skip(_changes.Count - count).ToList();
        }
    }

    /// <summary>
    /// Clears all tracked changes. Call on new session or conversation clear.
    /// </summary>
    public void Clear()
    {
        lock (_dataLock)
        {
            _changes.Clear();
            _transactionGroupNames.Clear();
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // Take first line only, then truncate
        var firstLine = value.Split('\n')[0].Trim();
        return firstLine.Length <= maxLength ? firstLine : firstLine[..maxLength] + "...";
    }
}
