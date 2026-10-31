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
    /// Maintain OverlayWindow settings such as position, size, and default opacity.
    /// </summary>
    public OverlaySettings OverlaySettings { get; set; } = new();

    /// <summary>
    /// Default overlay map opacity (20-100). Applied when the overlay window opens.   This is used to handle any updates to the 
    /// current settings loading, with future updates it can be removed as people transition to using OverlaySettings directly.
    /// </summary>
    [Obsolete("Use OverlaySettings.OverlayDefaultOpacityPercent instead.")]
    public double OverlayDefaultOpacityPercent { 
        get { return OverlaySettings.OverlayDefaultOpacityPercent; } 
        set { OverlaySettings.OverlayDefaultOpacityPercent = value; } 
    }

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
    /// Per-map map orientation override keyed by normalized map name.
    /// When true, the map is rotated 180 degrees for that specific map only.
    /// </summary>
    public Dictionary<string, bool> MapRotationByMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When true, each screenshot update recenters the overlay map on the player (keeps current zoom).
    /// </summary>
    public bool OverlayCenterOnPlayer { get; set; }

    /// <summary>
    /// Last selected map SVG filename without extension (e.g. "factory").
    /// </summary>
    public string LastSelectedMap { get; set; } = "";

    /// <summary>
    /// When true, screenshot player height (Y) auto-selects the matching floor in LEVELS.
    /// </summary>
    public bool AutoSelectFloorFromPlayerHeight { get; set; } = true;

    /// <summary>
    /// When true, the app checks GitHub for a newer release shortly after startup.
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Release version the user chose to skip (e.g. "2.8.3"). The startup prompt stays
    /// hidden for this version only; a newer release prompts again.
    /// </summary>
    public string SkippedUpdateVersion { get; set; } = "";

    /// <summary>
    /// When true, quest names are displayed on the maps
    /// </summary>
    public bool ShowQuestNames { get; set; } = true;
}
