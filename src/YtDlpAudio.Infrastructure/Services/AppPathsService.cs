using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Services;

public class AppPathsService : IAppPathsService
{
    public bool IsPortable { get; }
    public string BaseDataDirectory { get; }
    public string BinDirectory => Path.Combine(BaseDataDirectory, "bin");
    public string AuthDirectory => Path.Combine(BaseDataDirectory, "auth");
    public string LogsDirectory => Path.Combine(BaseDataDirectory, "logs");
    public string SettingsFilePath => Path.Combine(BaseDataDirectory, "settings.json");
    public string DefaultDownloadsDirectory { get; }

    public AppPathsService(bool? forcePortable = null, string? customAppDir = null)
    {
        string appDir = customAppDir ?? AppDomain.CurrentDomain.BaseDirectory;

        bool hasPortableMarker = File.Exists(Path.Combine(appDir, "portable.dat")) ||
                                 File.Exists(Path.Combine(appDir, "portable.mode"));

        bool hasCliFlag = Environment.GetCommandLineArgs().Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase));

        IsPortable = forcePortable ?? (hasPortableMarker || hasCliFlag);

        if (IsPortable)
        {
            BaseDataDirectory = Path.Combine(appDir, "data");
            DefaultDownloadsDirectory = Path.Combine(BaseDataDirectory, "downloads");
        }
        else
        {
            BaseDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YtDlpAudio"
            );
            DefaultDownloadsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "YtDlpDownloads"
            );
        }
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDataDirectory);
        Directory.CreateDirectory(BinDirectory);
        Directory.CreateDirectory(AuthDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DefaultDownloadsDirectory);
    }
}
