using System.Windows;
using CloudHermesDashboard.Services;
using CloudHermesDashboard.ViewModels;
using CloudHermesDashboard.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CloudHermesDashboard;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var collection = new ServiceCollection();
        collection.AddSingleton(DashboardSettings.Load());
        collection.AddSingleton<ILoggingService, FileLoggingService>();
        collection.AddSingleton<HermesEndpointResolver>();
        collection.AddSingleton<IHermesChatClient, HermesSessionChatClient>();
        collection.AddSingleton<IHermesConfigService, HermesConfigService>();
        collection.AddSingleton<HermesCapabilityProbeService>();
        collection.AddSingleton<IAgentDataService, HermesIntrospectionService>();
        collection.AddSingleton<ITaskBoardService, HermesTaskBoardService>();
        collection.AddSingleton<PermissionsViewModel>();
        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<LogWindowViewModel>();
        collection.AddSingleton<TaskManagerViewModel>();
        collection.AddTransient<LogWindow>();
        collection.AddTransient<TaskManagerWindow>();
        collection.AddTransient<MainWindow>();

        _services = collection.BuildServiceProvider();
        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = _services.GetRequiredService<MainViewModel>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
