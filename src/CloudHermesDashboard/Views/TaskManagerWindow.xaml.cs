using System.Windows;
using CloudHermesDashboard.ViewModels;

namespace CloudHermesDashboard.Views;

public partial class TaskManagerWindow : Window
{
    public TaskManagerWindow(TaskManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            if (viewModel.RefreshCommand.CanExecute(null))
                viewModel.RefreshCommand.Execute(null);
        };
    }
}
