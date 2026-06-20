using TarkovTracker.Models;

namespace TarkovTracker.Services;

public static class ScreenshotResolutionHelper
{
    public static ScreenshotResolutionContext CreateContext(
        int imageWidth,
        int imageHeight,
        int configuredWidth,
        int configuredHeight)
    {
        int tuningWidth = configuredWidth > 0 ? configuredWidth : imageWidth;
        int tuningHeight = configuredHeight > 0 ? configuredHeight : imageHeight;

        double aspect = tuningHeight > 0 ? tuningWidth / (double)tuningHeight : 16.0 / 9.0;
        ScreenshotResolutionProfile profile = ResolveProfile(tuningWidth, tuningHeight, aspect);

        return new ScreenshotResolutionContext
        {
            ImageWidth = imageWidth,
            ImageHeight = imageHeight,
            TuningWidth = tuningWidth,
            TuningHeight = tuningHeight,
            UsesManualResolution = configuredWidth > 0 && configuredHeight > 0,
            Profile = profile
        };
    }

    public static (int width, int height) ParsePreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset) ||
            preset.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0);
        }

        string[] parts = preset.Split('x', 'X');
        if (parts.Length != 2)
            return (0, 0);

        if (int.TryParse(parts[0].Trim(), out int width) &&
            int.TryParse(parts[1].Trim(), out int height) &&
            width > 0 &&
            height > 0)
        {
            return (width, height);
        }

        return (0, 0);
    }

    private static ScreenshotResolutionProfile ResolveProfile(int width, int height, double aspect)
    {
        if (aspect >= 2.15)
        {
            return new ScreenshotResolutionProfile
            {
                Name = "Ultrawide",
                PanelWidthRatio = 0.36,
                PanelHeightRatio = 0.58,
                HeaderHeightRatio = 0.075,
                HeaderScanStartRatio = 0.58,
                GreenHitThreshold = 0.10,
                OcrScaleFactor = Math.Max(1.35, ComputeOcrScale(height, 1440, 2.4))
            };
        }

        if (height >= 2160)
        {
            return new ScreenshotResolutionProfile
            {
                Name = "4K",
                PanelWidthRatio = 0.52,
                PanelHeightRatio = 0.56,
                HeaderHeightRatio = 0.065,
                HeaderScanStartRatio = 0.50,
                GreenHitThreshold = 0.12,
                OcrScaleFactor = 1.0
            };
        }

        if (height >= 1440)
        {
            return new ScreenshotResolutionProfile
            {
                Name = "1440p",
                PanelWidthRatio = 0.52,
                PanelHeightRatio = 0.58,
                HeaderHeightRatio = 0.07,
                HeaderScanStartRatio = 0.50,
                GreenHitThreshold = 0.12,
                OcrScaleFactor = 1.15
            };
        }

        return new ScreenshotResolutionProfile
        {
            Name = "1080p",
            PanelWidthRatio = 0.52,
            PanelHeightRatio = 0.58,
            HeaderHeightRatio = 0.08,
            HeaderScanStartRatio = 0.48,
            GreenHitThreshold = 0.12,
            OcrScaleFactor = ComputeOcrScale(height, 1440, 2.5)
        };
    }

    private static double ComputeOcrScale(int height, int referenceHeight, double maxScale)
    {
        if (height <= 0 || height >= referenceHeight)
            return 1.0;

        return Math.Min(maxScale, referenceHeight / (double)height);
    }
}

public sealed class ScreenshotResolutionProfile
{
    public string Name { get; init; } = "1080p";

    public double PanelWidthRatio { get; init; } = 0.52;

    public double PanelHeightRatio { get; init; } = 0.42;

    public double HeaderHeightRatio { get; init; } = 0.08;

    public double HeaderScanStartRatio { get; init; } = 0.50;

    public double GreenHitThreshold { get; init; } = 0.12;

    public double OcrScaleFactor { get; init; } = 1.0;
}

public sealed class ScreenshotResolutionContext
{
    public int ImageWidth { get; init; }

    public int ImageHeight { get; init; }

    public int TuningWidth { get; init; }

    public int TuningHeight { get; init; }

    public bool UsesManualResolution { get; init; }

    public ScreenshotResolutionProfile Profile { get; init; } = new();
}
