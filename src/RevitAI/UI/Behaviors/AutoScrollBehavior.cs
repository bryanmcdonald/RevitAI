using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace RevitAI.UI.Behaviors;

/// <summary>
/// Automatically scrolls a ListView to the bottom when new items are added,
/// unless the user has manually scrolled up.
/// </summary>
public class AutoScrollBehavior : Behavior<ListView>
{
    private ScrollViewer? _scrollViewer;
    private bool _autoScroll = true;
    private bool _isUpdating;

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
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isUpdating) return;

        // Check if user has scrolled up from the bottom
        if (_scrollViewer != null)
        {
            var isAtBottom = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 10;
            _autoScroll = isAtBottom || e.ExtentHeightChange > 0;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _autoScroll)
        {
            // Use dispatcher to ensure the UI has updated before scrolling
            AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
            {
                _isUpdating = true;
                try
                {
                    if (AssociatedObject.Items.Count > 0)
                    {
                        var lastItem = AssociatedObject.Items[^1];
                        AssociatedObject.ScrollIntoView(lastItem);
                    }
                }
                finally
                {
                    _isUpdating = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
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
