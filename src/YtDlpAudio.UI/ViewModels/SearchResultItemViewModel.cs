using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YtDlpAudio.Core.Models;

namespace YtDlpAudio.UI.ViewModels;

public partial class SearchResultItemViewModel : ObservableObject
{
    public SearchResultItem Model { get; }

    public string Id => Model.Id;
    public string Title => Model.Title;
    public string Author => Model.Author;
    public string? ThumbnailUrl => Model.ThumbnailUrl;
    public string Url => Model.Url;
    public string Source => Model.Source ?? string.Empty;
    public SearchResultType ResultType => Model.ResultType;
    public bool IsPlaylist => ResultType == SearchResultType.Playlist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandButtonLabel))]
    private bool _isPlaylistExpanded;

    [ObservableProperty] private bool _isLoadingPlaylistTracks;
    [ObservableProperty] private bool _hasLoadedPlaylistTracks;
    [ObservableProperty] private string _playlistTracksStatus = string.Empty;

    public string ExpandButtonLabel => IsPlaylistExpanded ? "Hide tracks" : "Expand tracks";
    public ObservableCollection<SearchResultItemViewModel> PlaylistTracks { get; } = new();

    public string TypeLabel => ResultType == SearchResultType.Playlist ? "Playlist" : "Track";
    
    public string DurationOrCountFormatted
    {
        get
        {
            if (ResultType == SearchResultType.Playlist && Model.TrackCount.HasValue)
            {
                return $"{Model.TrackCount.Value} tracks";
            }
            if (Model.Duration.HasValue)
            {
                return Model.Duration.Value.ToString(Model.Duration.Value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
            }
            return "--:--";
        }
    }

    public SearchResultItemViewModel(SearchResultItem model)
    {
        Model = model;
    }
}
