using System.Windows;
using YtDlpAudio.UI.ViewModels;

namespace YtDlpAudio.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.RequestLoginDialog += ShowLoginDialogAsync;
        Loaded += async (_, _) => await _viewModel.InitializeCommand.ExecuteAsync(null);
    }

    private Task ShowLoginDialogAsync()
    {
        var loginWin = new LoginWindow();
        loginWin.Owner = this;
        loginWin.ShowDialog();
        return Task.CompletedTask;
    }
}
