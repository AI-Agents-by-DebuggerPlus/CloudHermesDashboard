using System.Collections.ObjectModel;
using CloudHermesDashboard.Models;
using CloudHermesDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudHermesDashboard.ViewModels;

public sealed class TaskRowViewModel
{
    public TaskRowViewModel(TaskItemModel model)
    {
        Title = model.Title;
        Status = string.IsNullOrWhiteSpace(model.Status) ? "—" : model.Status;
        Priority = string.IsNullOrWhiteSpace(model.Priority) ? "—" : model.Priority;
        CreatedLabel = model.CreatedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
    }

    public string Title { get; }

    public string Status { get; }

    public string Priority { get; }

    public string CreatedLabel { get; }
}

public partial class TaskManagerViewModel : ObservableObject
{
    private readonly ITaskBoardService _tasks;

    public TaskManagerViewModel(ITaskBoardService tasks)
    {
        _tasks = tasks;
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

    [ObservableProperty]
    private string _banner = "Список только для просмотра. Обновление запрашивает связного.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        Banner = "Запрос system: list_tasks…";
        try
        {
            var result = await _tasks.RefreshAsync(CancellationToken.None);
            if (!result.Recognized)
            {
                Banner = "Ответ не распознан как список задач. Сырой текст записан в лог.";
                return;
            }

            Tasks.Clear();
            foreach (var task in result.Tasks)
                Tasks.Add(new TaskRowViewModel(task));
            Banner = Tasks.Count == 0 ? "Связной вернул пустой список задач." : "";
        }
        catch (Exception ex)
        {
            Banner = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy;
}
