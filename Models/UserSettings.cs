namespace TarkovTracker.Models;

public class UserAppSettings
{
    public string ScreenshotFolder { get; set; } = "";

    /// <summary>
    /// Game resolution preset: "auto", "1920x1080", "2560x1440", etc.
    /// When not "auto", EXFIL OCR uses this profile instead of screenshot dimensions alone.
    /// </summary>
    public string GameResolutionPreset { get; set; } = "auto";
}
