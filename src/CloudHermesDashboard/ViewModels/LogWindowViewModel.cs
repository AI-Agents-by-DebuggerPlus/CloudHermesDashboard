using System.Collections.ObjectModel;
using System.Windows.Threading;
using CloudHermesDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudHermesDashboard.ViewModels;

public partial class LogWindowViewModel : ObservableObject
{
    private readonly ILoggingService _log;
    private readonly Dispatcher _dispatcher;

    public LogWindowViewModel(ILoggingService log)
    {
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;
        foreach (var entry in log.Entries)
            Entries.Add(entry);
        log.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    public string LogFilePath => FileLoggingService.LogPath;

    [RelayCommand]
    private void Clear()
    {
        _log.Clear();
        Entries.Clear();
    }

    private void OnEntryAdded(object? sender, EventArgs e)
    {
        void Apply()
        {
            Entries.Clear();
            foreach (var entry in _log.Entries)
                Entries.Add(entry);
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.Invoke(Apply);
    }
}
