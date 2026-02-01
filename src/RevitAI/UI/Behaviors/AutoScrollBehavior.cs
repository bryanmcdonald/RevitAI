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

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace RevitAI.UI.Behaviors;

/// <summary>
/// Automatically scrolls a ListView to the bottom when new items are added,
/// unless the user has manually scrolled up.
/// Also handles streaming content updates by subscribing to PropertyChanged events.
/// </summary>
public class AutoScrollBehavior : Behavior<ListView>
{
    private ScrollViewer? _scrollViewer;
    private bool _autoScroll = true;
    private bool _isUpdating;
    private readonly List<INotifyPropertyChanged> _subscribedItems = new();

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.Loaded -= OnLoaded;

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }

        if (AssociatedObject.ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }

        // Unsubscribe from all item property changes
        foreach (var item in _subscribedItems)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
        _subscribedItems.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Find the ScrollViewer within the ListView
        _scrollViewer = FindScrollViewer(AssociatedObject);

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        // Subscribe to collection changes
        if (AssociatedObject.ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnCollectionChanged;
        }

        // Subscribe to existing items
        SubscribeToExistingItems();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isUpdating) return;

        // Only update auto-scroll state on user-initiated scrolls (when content didn't change)
        // This prevents content growth from re-enabling auto-scroll after user scrolled up
        if (_scrollViewer != null && e.ExtentHeightChange == 0)
        {
            _autoScroll = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 10;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe to new items for property change notifications
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged notifyItem && !_subscribedItems.Contains(notifyItem))
                {
                    notifyItem.PropertyChanged += OnItemPropertyChanged;
                    _subscribedItems.Add(notifyItem);
                }
            }
        }

        // Unsubscribe from removed items
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged notifyItem)
                {
                    notifyItem.PropertyChanged -= OnItemPropertyChanged;
                    _subscribedItems.Remove(notifyItem);
                }
            }
        }

        // Clear subscriptions on reset
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _subscribedItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
            _subscribedItems.Clear();
            SubscribeToExistingItems();
        }

        // Scroll to bottom when items are added
        if (e.Action == NotifyCollectionChangedAction.Add && _autoScroll)
        {
            ScrollToBottom();
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When Content changes during streaming, scroll to bottom if auto-scroll is enabled
        if (e.PropertyName == "Content" && _autoScroll)
        {
            ScrollToBottom();
        }
    }

    private void SubscribeToExistingItems()
    {
        if (AssociatedObject.ItemsSource == null) return;

        foreach (var item in AssociatedObject.ItemsSource)
        {
            if (item is INotifyPropertyChanged notifyItem && !_subscribedItems.Contains(notifyItem))
            {
                notifyItem.PropertyChanged += OnItemPropertyChanged;
                _subscribedItems.Add(notifyItem);
            }
        }
    }

    private void ScrollToBottom()
    {
        // Use dispatcher to ensure the UI has updated before scrolling
        AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
        {
            _isUpdating = true;
            try
            {
                _scrollViewer?.ScrollToEnd();
            }
            finally
            {
                _isUpdating = false;
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var result = FindScrollViewer(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
