using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using TarkovTracker.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace TarkovTracker.Services;

public class ScreenshotExfilParser
{
    // PMC raids show "EXFIL01 ..."; Scav raids show "EXIT01 ...".
    private static readonly Regex ExfilLineRegex = new(
        @"(?:EXFIL|EXIT|EXI\s*T|EX1T)\s*0?\d{1,2}\s*(.+?)(?:\s+\?{1,2}:\?{1,2}:\?{1,2})?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TransitLineRegex = new(
        @"TRANSIT\s*0?\d{1,2}\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExfilInlineRegex = new(
        @"(?:EXFIL|EXI\s*T|EX1T|EXIT)\s*0?\d{1,2}\s*(.+?)(?=\s+(?:EXFIL|EXI\s*T|EX1T|EXIT)|\s+TRANSIT|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TransitInlineRegex = new(
        @"TRANSIT\s*0?\d{1,2}\s*(.+?)(?=\s+(?:EXFIL|EXI\s*T|EX1T|EXIT)|\s+TRANSIT|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberedExtractLineRegex = new(
        @"^0?(\d{1,2})\s+([A-Za-z].+)$",
        RegexOptions.Compiled);

    private static readonly Regex TimerLineRegex = new(
        @"^\d{1,2}:\d{2}(:\d{2})?$",
        RegexOptions.Compiled);

    public async Task<ScreenshotExfilParseResult> ParseAsync(
        string imagePath,
        int configuredWidth = 0,
        int configuredHeight = 0)
    {
        var result = new ScreenshotExfilParseResult();

        if (!File.Exists(imagePath))
            return result;

        using Bitmap bitmap = LoadBitmap(imagePath);
        result.ImageWidth = bitmap.Width;
        result.ImageHeight = bitmap.Height;

        ScreenshotResolutionContext resolution = ScreenshotResolutionHelper.CreateContext(
            bitmap.Width,
            bitmap.Height,
            configuredWidth,
            configuredHeight);
        result.ResolutionProfile = resolution.Profile.Name;

        if (!DetectExfilPanel(bitmap, resolution.Profile))
            return result;

        result.PanelDetected = true;

        using Bitmap panelCrop = CropPanelRegion(bitmap, resolution.Profile);
        using Bitmap ocrBitmap = PrepareForOcr(panelCrop, resolution.Profile.OcrScaleFactor);
        using Bitmap enhancedBitmap = EnhancePanelForOcr(ocrBitmap);

        string ocrText = await RecognizeTextAsync(ocrBitmap);
        string enhancedOcrText = await RecognizeTextAsync(enhancedBitmap);
        result.RawOcrText = MergeOcrText(ocrText, enhancedOcrText);

        ParseExfilAndTransitNames(result.RawOcrText, result);
        return result;
    }

    private static string MergeOcrText(string primary, string secondary)
    {
        if (string.IsNullOrWhiteSpace(primary))
            return secondary ?? string.Empty;

        if (string.IsNullOrWhiteSpace(secondary))
            return primary;

        if (primary.Contains(secondary, StringComparison.OrdinalIgnoreCase))
            return primary;

        if (secondary.Contains(primary, StringComparison.OrdinalIgnoreCase))
            return secondary;

        return primary + Environment.NewLine + secondary;
    }

    private static Bitmap LoadBitmap(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        return new Bitmap(stream);
    }

    private static bool DetectExfilPanel(Bitmap bitmap, ScreenshotResolutionProfile profile)
    {
        int startX = (int)(bitmap.Width * profile.HeaderScanStartRatio);
        int headerBottom = Math.Max(24, (int)(bitmap.Height * profile.HeaderHeightRatio));
        int greenHits = 0;
        int samples = 0;
        int step = bitmap.Width >= 3000 ? 5 : 4;

        for (int y = 4; y < headerBottom; y += 2)
        {
            for (int x = startX; x < bitmap.Width - 4; x += step)
            {
                Color color = bitmap.GetPixel(x, y);
                if (IsExfilHeaderGreen(color))
                    greenHits++;

                samples++;
            }
        }

        if (samples == 0)
            return false;

        return (double)greenHits / samples >= profile.GreenHitThreshold;
    }

    private static bool IsExfilHeaderGreen(Color color)
    {
        return color.G >= 150 &&
               color.R <= 210 &&
               color.B <= 170 &&
               color.G >= color.R + 20 &&
               color.G >= color.B + 10;
    }

    private static Bitmap CropPanelRegion(Bitmap bitmap, ScreenshotResolutionProfile profile)
    {
        int width = Math.Max(1, (int)(bitmap.Width * profile.PanelWidthRatio));
        int height = Math.Max(1, (int)(bitmap.Height * profile.PanelHeightRatio));
        int x = Math.Max(0, bitmap.Width - width);
        const int y = 0;

        width = Math.Min(width, bitmap.Width - x);
        height = Math.Min(height, bitmap.Height - y);

        var crop = new Bitmap(width, height);
        using Graphics graphics = Graphics.FromImage(crop);
        graphics.DrawImage(bitmap, new Rectangle(0, 0, width, height), x, y, width, height, GraphicsUnit.Pixel);
        return crop;
    }

    private static Bitmap PrepareForOcr(Bitmap crop, double scaleFactor)
    {
        if (scaleFactor <= 1.01)
            return (Bitmap)crop.Clone();

        int newWidth = Math.Max(1, (int)Math.Round(crop.Width * scaleFactor));
        int newHeight = Math.Max(1, (int)Math.Round(crop.Height * scaleFactor));

        var scaled = new Bitmap(newWidth, newHeight);
        using Graphics graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(crop, 0, 0, newWidth, newHeight);
        return scaled;
    }

    private static Bitmap EnhancePanelForOcr(Bitmap source)
    {
        var enhanced = new Bitmap(source.Width, source.Height);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Color color = source.GetPixel(x, y);
                bool isText = IsLikelyPanelText(color);
                enhanced.SetPixel(x, y, isText ? Color.Black : Color.White);
            }
        }

        return enhanced;
    }

    private static bool IsLikelyPanelText(Color color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));

        // White/gray EXIT labels
        if (max >= 165 && max - min <= 90)
            return true;

        // Green header text
        if (color.G >= 140 && color.G >= color.R + 25 && color.G >= color.B + 10)
            return true;

        // Red/orange TRANSIT labels
        if (color.R >= 145 && color.R >= color.G + 25 && color.R >= color.B + 20)
            return true;

        return false;
    }

    private static async Task<string> RecognizeTextAsync(Bitmap bitmap)
    {
        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));

        if (engine == null)
            return string.Empty;

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        memoryStream.Position = 0;

        using IRandomAccessStream randomAccessStream = memoryStream.AsRandomAccessStream();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        OcrResult ocrResult = await engine.RecognizeAsync(softwareBitmap);
        return ocrResult?.Text ?? string.Empty;
    }

    private static void ParseExfilAndTransitNames(string ocrText, ScreenshotExfilParseResult result)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return;

        string normalizedText = NormalizeOcrText(ocrText);

        foreach (string line in normalizedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = CleanOcrName(line.Trim());
            if (trimmed.Length == 0 || IsIgnoredPanelLine(trimmed))
                continue;

            Match exfilMatch = ExfilLineRegex.Match(trimmed);
            if (exfilMatch.Success)
            {
                AddUnique(result.ExfilNames, CleanOcrName(exfilMatch.Groups[1].Value));
                continue;
            }

            Match transitMatch = TransitLineRegex.Match(trimmed);
            if (transitMatch.Success)
                AddUnique(result.TransitNames, CleanOcrName(transitMatch.Groups[1].Value));
        }

        foreach (Match match in ExfilInlineRegex.Matches(normalizedText))
            AddUnique(result.ExfilNames, CleanOcrName(match.Groups[1].Value));

        foreach (Match match in TransitInlineRegex.Matches(normalizedText))
            AddUnique(result.TransitNames, CleanOcrName(match.Groups[1].Value));

        if (result.ExfilNames.Count == 0)
            ParseUnlabeledExtractLines(normalizedText, result);
    }

    private static string NormalizeOcrText(string ocrText)
    {
        return ocrText
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace("EXI T", "EXIT", StringComparison.OrdinalIgnoreCase)
            .Replace("EX1T", "EXIT", StringComparison.OrdinalIgnoreCase)
            .Replace("EXI1", "EXIT", StringComparison.OrdinalIgnoreCase)
            .Replace("EX IT", "EXIT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredPanelLine(string line)
    {
        if (TimerLineRegex.IsMatch(line))
            return true;

        if (line.Contains("extraction point", StringComparison.OrdinalIgnoreCase))
            return true;

        if (line.Contains("keycard required", StringComparison.OrdinalIgnoreCase))
            return true;

        return line.StartsWith("Find an", StringComparison.OrdinalIgnoreCase);
    }

    private static void ParseUnlabeledExtractLines(string normalizedText, ScreenshotExfilParseResult result)
    {
        int transitIndex = normalizedText.IndexOf("TRANSIT", StringComparison.OrdinalIgnoreCase);
        if (transitIndex <= 0)
            return;

        string extractSection = normalizedText[..transitIndex];
        foreach (string line in extractSection.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = CleanOcrName(line.Trim());
            if (trimmed.Length == 0 || IsIgnoredPanelLine(trimmed))
                continue;

            if (ExfilLineRegex.IsMatch(trimmed))
                continue;

            Match numbered = NumberedExtractLineRegex.Match(trimmed);
            if (numbered.Success)
            {
                AddUnique(result.ExfilNames, CleanOcrName(numbered.Groups[2].Value));
                continue;
            }

            if (trimmed.Contains("TRANSIT", StringComparison.OrdinalIgnoreCase))
                continue;

            AddUnique(result.ExfilNames, trimmed);
        }
    }

    private static string CleanOcrName(string value)
    {
        string cleaned = value.Trim();
        cleaned = Regex.Replace(cleaned, @"^\*+|\*+$", "");
        cleaned = Regex.Replace(cleaned, @"\s+\?{1,2}:\?{1,2}:\?{1,2}.*$", "");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
        return cleaned.Trim(' ', '-', ':', '.', ',', '*');
    }

    private static void AddUnique(List<string> names, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (names.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            return;

        names.Add(value);
    }
}
