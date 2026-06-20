using System.IO;
using System.Reflection;

namespace TarkovTracker.Models;

public static class AppInfo
{
    public const string ProductName = "SayserTarkovTracker";
    public const string InterfaceVersion = "2.7";

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

    public const string DataCredit = "Map data and icons from tarkov.dev";

    public const string Disclaimer =
        "Not affiliated with Battlestate Games. Use at your own risk.";

    public static string SettingsFilePath
    {
        get
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
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
