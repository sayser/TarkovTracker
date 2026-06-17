namespace TarkovTracker.Models;

public class UserAppSettings
{
    public string ScreenshotFolder { get; set; } = "";

    /// <summary>
    /// Game resolution preset: "auto", "1920x1080", "2560x1440", etc.
    /// When not "auto", EXFIL OCR uses this profile instead of screenshot dimensions alone.
    /// </summary>
    public string GameResolutionPreset { get; set; } = "auto";

    /// <summary>
    /// Default overlay map opacity (20-100). Applied when the overlay window opens.
    /// </summary>
    public double OverlayDefaultOpacityPercent { get; set; } = 80;

    /// <summary>
    /// When false, screenshot folder monitoring and manual parse actions are disabled.
    /// </summary>
    public bool ScreenshotParsingEnabled { get; set; } = true;
}
