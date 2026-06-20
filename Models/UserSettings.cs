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

    /// <summary>
    /// When true, <see cref="MarkerFilters"/> is saved and restored between sessions.
    /// When false, marker toggles reset to defaults on each startup.
    /// </summary>
    public bool SaveMarkerFilters { get; set; }

    /// <summary>
    /// Saved MARKERS panel toggle states (used when <see cref="SaveMarkerFilters"/> is true).
    /// </summary>
    public MarkerFilterPreferences MarkerFilters { get; set; } = new();

    /// <summary>
    /// Custom waypoint pins keyed by normalized map name (e.g. "factory", "customs").
    /// </summary>
    public Dictionary<string, List<CustomPinEntry>> CustomPinsByMap { get; set; } = new();

    /// <summary>
    /// When true, each screenshot update recenters the overlay map on the player (keeps current zoom).
    /// </summary>
    public bool OverlayCenterOnPlayer { get; set; }

    /// <summary>
    /// Last selected map SVG filename without extension (e.g. "factory").
    /// </summary>
    public string LastSelectedMap { get; set; } = "";
}
