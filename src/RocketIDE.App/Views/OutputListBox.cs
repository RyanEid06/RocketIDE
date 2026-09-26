using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RocketIDE.App.Views;

/// <summary>Shows the tail of each streamed output batch without discarding history.</summary>
public sealed class OutputListBox : ListBox
{
    private bool _scrollPending;

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (_scrollPending || Items.Count == 0) return;
        _scrollPending = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _scrollPending = false;
            UpdateLayout();
            FindScrollViewer(this)?.ScrollToEnd();
        }));
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        if (node is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(node, index)) is { } child) return child;
        return null;
    }}
