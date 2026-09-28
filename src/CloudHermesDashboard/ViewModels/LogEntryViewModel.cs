namespace CloudHermesDashboard.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(string line)
    {
        Line = line;
    }

    public string Line { get; }
}
