using CommunityToolkit.Mvvm.ComponentModel;
using YtDlpAudio.Core.Models;

namespace YtDlpAudio.UI.ViewModels;

public partial class TrackItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private TimeSpan? _duration;
    [ObservableProperty] private string? _thumbnailUrl;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private DownloadState _state = DownloadState.Queued;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _speed = string.Empty;
    [ObservableProperty] private string _eta = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public string DurationFormatted => Duration.HasValue 
        ? Duration.Value.ToString(Duration.Value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss")
        : "--:--";
}
