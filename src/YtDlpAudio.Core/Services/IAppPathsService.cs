namespace YtDlpAudio.Core.Services;

public interface IAppPathsService
{
    bool IsPortable { get; }
    string BaseDataDirectory { get; }
    string BinDirectory { get; }
    string AuthDirectory { get; }
    string LogsDirectory { get; }
    string SettingsFilePath { get; }
    string DefaultDownloadsDirectory { get; }
    void EnsureDirectories();
}
