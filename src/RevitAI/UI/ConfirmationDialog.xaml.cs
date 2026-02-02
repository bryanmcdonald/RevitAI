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

using System.Windows;

namespace RevitAI.UI;

/// <summary>
/// Confirmation dialog for destructive tool operations.
/// Shows tool name, description, and allows user to allow/deny with optional session skip.
/// </summary>
public partial class ConfirmationDialog : Window
{
    /// <summary>
    /// Gets the tool name displayed in the dialog.
    /// </summary>
    public string ToolName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the user allowed the operation.
    /// </summary>
    public bool Allowed { get; private set; }

    /// <summary>
    /// Gets whether the user chose to skip future confirmations for this tool this session.
    /// </summary>
    public bool DontAskAgain { get; private set; }

    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Configures the dialog with the tool name and description.
    /// </summary>
    /// <param name="toolName">The name of the tool being executed.</param>
    /// <param name="description">A description of what the tool will do.</param>
    public void Configure(string toolName, string description)
    {
        ToolName = toolName ?? "Unknown Tool";
        ToolNameText.Text = ToolName;

        // Ensure we always have a description to show
        if (string.IsNullOrWhiteSpace(description))
        {
            DescriptionLabel.Text = $"This tool ({ToolName}) will modify the Revit model. Do you want to proceed?";
        }
        else
        {
            DescriptionLabel.Text = description;
        }
    }

    private void AllowButton_Click(object sender, RoutedEventArgs e)
    {
        Allowed = true;
        DontAskAgain = DontAskAgainCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        Allowed = false;
        DontAskAgain = false;
        DialogResult = false;
        Close();
    }
}
