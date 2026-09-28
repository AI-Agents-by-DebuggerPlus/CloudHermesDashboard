using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using CloudHermesDashboard.ViewModels;

namespace CloudHermesDashboard.Views;

public partial class LogWindow : Window
{
    private bool _stickToEnd = true;

    public LogWindow(LogWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_stickToEnd)
            LogScroll.ScrollToEnd();
    }

    private void LogScroll_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
            _stickToEnd = LogScroll.VerticalOffset >= LogScroll.ScrollableHeight - 8;

        if (_stickToEnd && e.ExtentHeightChange > 0)
            LogScroll.ScrollToEnd();
    }
}
