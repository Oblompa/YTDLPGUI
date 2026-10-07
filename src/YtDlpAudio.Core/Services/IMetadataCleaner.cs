namespace YtDlpAudio.Core.Services;

public interface IMetadataCleaner
{
    string CleanTitle(string rawTitle);
    string CleanArtist(string rawArtist);
}
