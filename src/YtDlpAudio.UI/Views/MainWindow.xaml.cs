using System.Windows;
using System.Windows.Controls;
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
        _viewModel.RequestClearQueueConfirmation += ConfirmClearQueueAsync;
        Loaded += async (_, _) => await _viewModel.InitializeCommand.ExecuteAsync(null);
        Closing += async (_, _) => await _viewModel.SaveCurrentSettingsCommand.ExecuteAsync(null);
    }

    private void QueueDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SetSelectedQueueTracks(QueueDataGrid.SelectedItems
            .Cast<TrackItemViewModel>());
    }

    private Task<bool> ConfirmClearQueueAsync(int rowCount)
    {
        var result = MessageBox.Show(
            this,
            $"Remove all {rowCount} rows from the queue? Downloaded files will not be deleted.",
            "Clear download queue",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private Task ShowLoginDialogAsync()
    {
        var loginWin = _loginWindowFactory();
        loginWin.Owner = this;
        loginWin.ShowDialog();
        return Task.CompletedTask;
    }
}
