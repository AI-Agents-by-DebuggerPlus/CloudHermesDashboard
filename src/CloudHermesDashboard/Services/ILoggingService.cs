using CloudHermesDashboard.ViewModels;

namespace CloudHermesDashboard.Services;

public interface ILoggingService
{
    IReadOnlyList<LogEntryViewModel> Entries { get; }

    event EventHandler? EntryAdded;

    void Info(string source, string message);

    void Warn(string source, string message);

    void Error(string source, string message);

    void Clear();
}
