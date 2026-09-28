using System.IO;
using System.Text;
using System.Windows.Threading;
using CloudHermesDashboard.ViewModels;

namespace CloudHermesDashboard.Services;

public sealed class FileLoggingService : ILoggingService
{
    private const int UiLimit = 12000;
    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly List<LogEntryViewModel> _entries = [];

    public FileLoggingService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        var dir = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    public IReadOnlyList<LogEntryViewModel> Entries => _entries;

    public event EventHandler? EntryAdded;

    public static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesDashboard",
            "logs",
            "dashboard.log");

    public void Info(string source, string message) => Write("INFO", source, message);

    public void Warn(string source, string message) => Write("WARN", source, message);

    public void Error(string source, string message) => Write("ERROR", source, message);

    public void Clear()
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(LogPath, string.Empty);
        }

        void Apply()
        {
            _entries.Clear();
            EntryAdded?.Invoke(this, EventArgs.Empty);
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.Invoke(Apply);
    }

    private void Write(string level, string source, string message)
    {
        var safe = message ?? "";
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{source}] {safe}";
        Console.WriteLine(line.Length > 4000 ? line[..4000] + "…" : line);

        lock (_gate)
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }

        var uiLine = line.Length <= UiLimit
            ? line
            : line[..UiLimit] + "… (полный текст в dashboard.log)";
        var entry = new LogEntryViewModel(uiLine);

        void Apply()
        {
            _entries.Add(entry);
            while (_entries.Count > 2000)
                _entries.RemoveAt(0);
            EntryAdded?.Invoke(this, EventArgs.Empty);
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.Invoke(Apply);
    }
}
