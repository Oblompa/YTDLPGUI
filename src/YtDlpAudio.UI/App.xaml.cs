using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using YtDlpAudio.Core.Services;
using YtDlpAudio.Infrastructure.Auth;
using YtDlpAudio.Infrastructure.Dependencies;
using YtDlpAudio.Infrastructure.Process;
using YtDlpAudio.UI.ViewModels;
using YtDlpAudio.UI.Views;

namespace YtDlpAudio.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Register Core & Infrastructure
        services.AddSingleton<IDependencyManager, DependencyManager>();
        services.AddSingleton<IAuthManager, WebView2AuthManager>();
        services.AddSingleton<IYtDlpRunner, YtDlpProcessRunner>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IMetadataCleaner, MetadataCleaner>();
        services.AddSingleton<AudioDownloadService>();

        // Register ViewModels & Views
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
        services.AddTransient<LoginWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
