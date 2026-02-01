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

using System.Windows.Documents;
using Markdig;
using Neo.Markdig.Xaml;

namespace RevitAI.Services;

/// <summary>
/// Converts markdown text to WPF FlowDocument using Markdig and Neo.Markdig.Xaml.
/// </summary>
public class MarkdownService
{
    private static MarkdownService? _instance;
    private static readonly object _lock = new();

    private readonly MarkdownPipeline _pipeline;

    /// <summary>
    /// Gets the singleton instance of the MarkdownService.
    /// </summary>
    public static MarkdownService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new MarkdownService();
                }
            }
            return _instance;
        }
    }

    private MarkdownService()
    {
        // Configure Markdig pipeline with common extensions
        _pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseTaskLists()
            .UsePipeTables()
            .UseAutoLinks()
            .Build();
    }

    /// <summary>
    /// Converts markdown text to a WPF FlowDocument.
    /// </summary>
    public FlowDocument ConvertToFlowDocument(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return new FlowDocument(new Paragraph(new Run(string.Empty)));
        }

        try
        {
            // Use Neo.Markdig.Xaml to convert markdown to FlowDocument
            var xaml = MarkdownXaml.ToXaml(markdown, _pipeline);

            // Parse XAML to FlowDocument
            using var reader = new System.IO.StringReader(xaml);
            using var xmlReader = System.Xml.XmlReader.Create(reader);
            var document = System.Windows.Markup.XamlReader.Load(xmlReader) as FlowDocument;

            return document ?? CreatePlainTextDocument(markdown);
        }
        catch
        {
            // Fallback to plain text if markdown parsing fails
            return CreatePlainTextDocument(markdown);
        }
    }

    /// <summary>
    /// Creates a simple FlowDocument with plain text (fallback).
    /// </summary>
    private static FlowDocument CreatePlainTextDocument(string text)
    {
        var paragraph = new Paragraph(new Run(text));
        return new FlowDocument(paragraph)
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 14
        };
    }

    /// <summary>
    /// Converts markdown to plain text (strips formatting).
    /// </summary>
    public string StripMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        try
        {
            return Markdown.ToPlainText(markdown, _pipeline);
        }
        catch
        {
            return markdown;
        }
    }
}
