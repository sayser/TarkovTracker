using System.IO;
using System.Reflection;

namespace TarkovTracker.Models;

public static class AppInfo
{
    public const string ProductName = "SayserTarkovTracker";
    public const string InterfaceVersion = "2.7.8";

    public const string GitHubOwner = "sayser";
    public const string GitHubRepo = "TarkovTracker";
    public static string GitHubLatestReleaseApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    public static string GitHubReleasesPageUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";

    public static string VersionLabel
    {
        get
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null)
                return InterfaceVersion;

            if (version.Build > 0)
                return $"{version.Major}.{version.Minor}.{version.Build}";

            return $"{version.Major}.{version.Minor}";
        }
    }

    public const string AboutDescription =
        "A fan-made tactical map companion for Escape from Tarkov. " +
        "Uses in-game screenshots for player tracking and raid exfil highlighting.";

    public const string DataCredit =
        "Map data and icons from tarkov.dev.";

    public const string Disclaimer =
        "Not affiliated with Battlestate Games. Use at your own risk.";

    public static string SettingsFilePath
    {
        get
        {
            // Prefer the folder containing the exe so single-file self-extract
            // (temp BaseDirectory) does not lose settings on cache refresh.
            string? processPath = Environment.ProcessPath;
            string? exeDir = string.IsNullOrWhiteSpace(processPath)
                ? null
                : Path.GetDirectoryName(processPath);

            string baseDir = !string.IsNullOrWhiteSpace(exeDir)
                ? exeDir!
                : AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Environment.CurrentDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = ".";

            return Path.GetFullPath(Path.Combine(baseDir, "settings.json"));
        }
    }
}
