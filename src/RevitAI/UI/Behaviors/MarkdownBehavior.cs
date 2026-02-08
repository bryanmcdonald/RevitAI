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
using System.Windows.Controls;
using System.Windows.Documents;
using RevitAI.Services;

namespace RevitAI.UI.Behaviors;

/// <summary>
/// Attached property for RichTextBox that converts markdown text to a FlowDocument.
/// Skips conversion while the RichTextBox is collapsed (during streaming),
/// then applies markdown when it becomes visible.
/// </summary>
public static class MarkdownBehavior
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached(
            "Markdown",
            typeof(string),
            typeof(MarkdownBehavior),
            new PropertyMetadata(null, OnMarkdownChanged));

    private static readonly DependencyProperty IsListeningToVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "IsListeningToVisibility",
            typeof(bool),
            typeof(MarkdownBehavior),
            new PropertyMetadata(false));

    public static string? GetMarkdown(DependencyObject obj) =>
        (string?)obj.GetValue(MarkdownProperty);

    public static void SetMarkdown(DependencyObject obj, string? value) =>
        obj.SetValue(MarkdownProperty, value);

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox rtb)
            return;

        // Subscribe to visibility and unload events only once
        if (!(bool)rtb.GetValue(IsListeningToVisibilityProperty))
        {
            rtb.IsVisibleChanged += OnIsVisibleChanged;
            rtb.Unloaded += OnUnloaded;
            rtb.SetValue(IsListeningToVisibilityProperty, true);
        }

        // Skip conversion if collapsed (streaming in progress)
        if (rtb.Visibility != Visibility.Visible)
            return;

        ApplyMarkdown(rtb, e.NewValue as string);
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not RichTextBox rtb)
            return;

        // When becoming visible (streaming ended), apply markdown with current content
        if (rtb.Visibility == Visibility.Visible)
        {
            var markdown = GetMarkdown(rtb);
            if (!string.IsNullOrEmpty(markdown))
            {
                ApplyMarkdown(rtb, markdown);
            }
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox rtb)
            return;

        rtb.IsVisibleChanged -= OnIsVisibleChanged;
        rtb.Unloaded -= OnUnloaded;
        rtb.SetValue(IsListeningToVisibilityProperty, false);
    }

    private static void ApplyMarkdown(RichTextBox rtb, string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            rtb.Document = new FlowDocument(new Paragraph(new Run(string.Empty)));
            return;
        }

        var flowDoc = MarkdownService.Instance.ConvertToFlowDocument(markdown);

        // Inherit theme colors and font from the RichTextBox
        flowDoc.Foreground = rtb.Foreground;
        flowDoc.FontFamily = rtb.FontFamily;
        flowDoc.FontSize = rtb.FontSize;
        flowDoc.PagePadding = new Thickness(0);

        rtb.Document = flowDoc;
    }
}
