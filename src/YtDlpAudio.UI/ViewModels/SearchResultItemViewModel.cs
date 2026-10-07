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
    public SearchResultType ResultType => Model.ResultType;

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
