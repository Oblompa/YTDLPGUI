using System.Windows;
using YtDlpAudio.UI.ViewModels;

namespace YtDlpAudio.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<LoginWindow> _loginWindowFactory;

    public MainWindow(MainViewModel viewModel, Func<LoginWindow> loginWindowFactory)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _loginWindowFactory = loginWindowFactory;
        DataContext = _viewModel;

        _viewModel.RequestLoginDialog += ShowLoginDialogAsync;
        Loaded += async (_, _) => await _viewModel.InitializeCommand.ExecuteAsync(null);
        Closing += async (_, _) => await _viewModel.SaveCurrentSettingsCommand.ExecuteAsync(null);
    }

    private Task ShowLoginDialogAsync()
    {
        var loginWin = _loginWindowFactory();
        loginWin.Owner = this;
        loginWin.ShowDialog();
        return Task.CompletedTask;
    }
}
