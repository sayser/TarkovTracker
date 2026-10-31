using System;

public class OverlaySettings
{
    public double OverlayLeft { get; set; } = 0;

    public double OverlayTop { get; set; } = 0;

    /// <summary>
    /// Height of the overlay window.
    /// </summary>
    public double OverlayHeight { get; set; } = 600;

    /// <summary>
    /// Width of the overlay window.
    /// </summary>
    public double OverlayWidth { get; set; } = 800;    
    
    /// <summary>
    /// Default overlay map opacity (20-100). Applied when the overlay window opens.
    /// </summary>
    public double OverlayDefaultOpacityPercent { get; set; } = 80;

    /// <summary>
    /// When true, the map is rotated 180 degrees so it matches the orientation used by MapGenie.
    /// Applies to both the main map view and the overlay.
    /// </summary>
    public bool RotateMap180Degrees { get; set; }
}