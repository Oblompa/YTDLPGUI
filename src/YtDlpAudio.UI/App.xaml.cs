using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using YtDlpAudio.Core.Services;
using YtDlpAudio.Infrastructure.Auth;
using YtDlpAudio.Infrastructure.Dependencies;
using YtDlpAudio.Infrastructure.Process;
using YtDlpAudio.Infrastructure.Services;
using YtDlpAudio.UI.ViewModels;
using YtDlpAudio.UI.Views;

namespace YtDlpAudio.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var appPaths = new AppPathsService();
            appPaths.EnsureDirectories();

            string logFile = Path.Combine(appPaths.LogsDirectory, "log-.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    path: logFile,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Starting YtDlpAudio (Portable={IsPortable}). Data directory: {DataDir}",
                appPaths.IsPortable, appPaths.BaseDataDirectory);

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });

            services.AddSingleton<IAppPathsService>(appPaths);
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IDependencyManager>(_ => new DependencyManager(appPaths));
            services.AddSingleton<WebView2AuthManager>(_ => new WebView2AuthManager(appPaths));
            services.AddSingleton<IAuthManager>(sp => sp.GetRequiredService<WebView2AuthManager>());
            services.AddSingleton<IYtDlpRunner, YtDlpProcessRunner>();
            services.AddSingleton<ISearchService, SearchService>();
            services.AddSingleton<IMetadataCleaner, MetadataCleaner>();
            services.AddSingleton<AudioDownloadService>();

            services.AddSingleton<MainViewModel>();
            services.AddTransient<LoginWindow>();
            services.AddSingleton<Func<LoginWindow>>(sp => () => sp.GetRequiredService<LoginWindow>());
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed.");
            Log.CloseAndFlush();
            MessageBox.Show(
                $"YtDlpAudio could not start.{Environment.NewLine}{Environment.NewLine}{ex}",
                "YtDlpAudio startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Exiting YtDlpAudio application.");
        Log.CloseAndFlush();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
