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

using System.ComponentModel;

namespace RevitAI.Services;

/// <summary>
/// Tracks API token usage for the current session.
/// Session-only tracking (resets when Revit closes).
/// Thread-safe singleton using Interlocked operations.
/// </summary>
public sealed class UsageTracker : INotifyPropertyChanged
{
    private static UsageTracker? _instance;
    private static readonly object _lock = new();

    // Pricing per million tokens (Claude Sonnet rates as of 2025)
    private const decimal InputTokenPricePerMillion = 3.00m;
    private const decimal OutputTokenPricePerMillion = 15.00m;

    private long _inputTokens;
    private long _outputTokens;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static UsageTracker Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new UsageTracker();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Gets the total input tokens used this session.
    /// </summary>
    public long InputTokens => Interlocked.Read(ref _inputTokens);

    /// <summary>
    /// Gets the total output tokens used this session.
    /// </summary>
    public long OutputTokens => Interlocked.Read(ref _outputTokens);

    /// <summary>
    /// Gets the total tokens used this session.
    /// </summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// Gets the estimated cost based on Claude Sonnet pricing.
    /// </summary>
    public decimal EstimatedCost
    {
        get
        {
            var inputCost = (InputTokens / 1_000_000m) * InputTokenPricePerMillion;
            var outputCost = (OutputTokens / 1_000_000m) * OutputTokenPricePerMillion;
            return inputCost + outputCost;
        }
    }

    /// <summary>
    /// Gets a formatted string of the estimated cost.
    /// </summary>
    public string FormattedCost => $"${EstimatedCost:F4}";

    private UsageTracker() { }

    /// <summary>
    /// Records token usage from an API response.
    /// Thread-safe.
    /// </summary>
    /// <param name="inputTokens">Number of input tokens used.</param>
    /// <param name="outputTokens">Number of output tokens used.</param>
    public void RecordUsage(int inputTokens, int outputTokens)
    {
        Interlocked.Add(ref _inputTokens, inputTokens);
        Interlocked.Add(ref _outputTokens, outputTokens);

        // Notify UI of all property changes
        OnPropertyChanged(nameof(InputTokens));
        OnPropertyChanged(nameof(OutputTokens));
        OnPropertyChanged(nameof(TotalTokens));
        OnPropertyChanged(nameof(EstimatedCost));
        OnPropertyChanged(nameof(FormattedCost));
    }

    /// <summary>
    /// Resets all usage counters to zero.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _inputTokens, 0);
        Interlocked.Exchange(ref _outputTokens, 0);

        OnPropertyChanged(nameof(InputTokens));
        OnPropertyChanged(nameof(OutputTokens));
        OnPropertyChanged(nameof(TotalTokens));
        OnPropertyChanged(nameof(EstimatedCost));
        OnPropertyChanged(nameof(FormattedCost));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
