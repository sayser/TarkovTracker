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
    private static readonly Regex ExfilLineRegex = new(
        @"(?:EXFIL|EXIT|EXI\s*T|EX1T|EXFILO|EXITO)\s*0?(\d{1,2})\s*(.+?)(?:\s+\d{1,2}:\d{2}(?::\d{2})?|\s+\?{1,2}:\?{1,2}:\?{1,2})?(?:\s*$|\s+(?:EXFIL|EXIT|TRANSIT))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TransitLineRegex = new(
        @"TRANSIT\s*0?\d{1,2}\s*(.+?)(?:\s+\d{1,2}:\d{2}(?::\d{2})?|\s+\?{1,2}:\?{1,2}:\?{1,2})?(?:\s*$|\s+TRANSIT)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExfilInlineRegex = new(
        @"(?:EXFIL|EXI\s*T|EX1T|EXIT|EXFILO|EXITO)\s*0?\d{1,2}\s*(.+?)(?=\s+(?:EXFIL|EXI\s*T|EX1T|EXIT|EXFILO|EXITO)\s*0?\d|\s+TRANSIT\s*0?\d|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TransitInlineRegex = new(
        @"TRANSIT\s*0?\d{1,2}\s*(.+?)(?=\s+(?:EXFIL|EXI\s*T|EX1T|EXIT|EXFILO|EXITO)\s*0?\d|\s+TRANSIT\s*0?\d|$)",
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

        int panelBottom = DetectPanelBottom(bitmap, resolution.Profile);
        using Bitmap panelCrop = CropPanelRegion(bitmap, resolution.Profile, panelBottom);
        using Bitmap ocrBitmap = PrepareForOcr(panelCrop, resolution.Profile.OcrScaleFactor);
        using Bitmap enhancedBitmap = EnhancePanelForOcr(ocrBitmap);

        var primaryOcr = await RecognizeTextDetailedAsync(ocrBitmap);
        var enhancedOcr = await RecognizeTextDetailedAsync(enhancedBitmap);
        result.RawOcrText = MergeOcrText(primaryOcr.Text, enhancedOcr.Text);

        ParseExfilAndTransitNames(result.RawOcrText, result, MergeOcrLines(primaryOcr.Lines, enhancedOcr.Lines));

        int initialCount = result.ExfilNames.Count + result.TransitNames.Count;
        if (initialCount <= 3)
        {
            int tallBottom = Math.Max(panelBottom, (int)(bitmap.Height * 0.68));
            if (tallBottom > panelBottom + 8)
            {
                using Bitmap tallCrop = CropPanelRegion(bitmap, resolution.Profile, tallBottom);
                using Bitmap tallOcrBitmap = PrepareForOcr(tallCrop, resolution.Profile.OcrScaleFactor);
                using Bitmap tallEnhancedBitmap = EnhancePanelForOcr(tallOcrBitmap);

                var tallPrimary = await RecognizeTextDetailedAsync(tallOcrBitmap);
                var tallEnhanced = await RecognizeTextDetailedAsync(tallEnhancedBitmap);
                string tallText = MergeOcrText(tallPrimary.Text, tallEnhanced.Text);
                result.RawOcrText = MergeOcrText(result.RawOcrText, tallText);

                ParseExfilAndTransitNames(
                    tallText,
                    result,
                    MergeOcrLines(tallPrimary.Lines, tallEnhanced.Lines));
            }
        }

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

    private static List<string> MergeOcrLines(IEnumerable<string> primary, IEnumerable<string> secondary)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in primary.Concat(secondary))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
                continue;

            merged.Add(trimmed);
        }

        return merged;
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

    private static int DetectPanelBottom(Bitmap bitmap, ScreenshotResolutionProfile profile)
    {
        int panelX = Math.Max(0, (int)(bitmap.Width * (1.0 - profile.PanelWidthRatio)));
        int minHeight = Math.Max(1, (int)(bitmap.Height * profile.PanelHeightRatio));
        int scanLimit = Math.Min(bitmap.Height, (int)(bitmap.Height * 0.72));
        int lastTextRow = Math.Max(24, (int)(bitmap.Height * profile.HeaderHeightRatio));
        int blankRows = 0;

        for (int y = 0; y < scanLimit; y++)
        {
            bool rowHasText = false;

            for (int x = panelX; x < bitmap.Width - 2; x += Math.Max(2, (bitmap.Width - panelX) / 80))
            {
                Color color = bitmap.GetPixel(x, y);
                if (IsLikelyPanelText(color) || IsExfilHeaderGreen(color) || IsPanelBackground(color))
                {
                    rowHasText = true;
                    break;
                }
            }

            if (rowHasText)
            {
                lastTextRow = y;
                blankRows = 0;
                continue;
            }

            blankRows++;
            if (blankRows >= 28 && y > minHeight)
                break;
        }

        return Math.Min(bitmap.Height, Math.Max(minHeight, lastTextRow + 28));
    }

    private static bool IsPanelBackground(Color color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));
        return max <= 95 && max - min <= 35;
    }

    private static Bitmap CropPanelRegion(Bitmap bitmap, ScreenshotResolutionProfile profile, int? bottomY = null)
    {
        int width = Math.Max(1, (int)(bitmap.Width * profile.PanelWidthRatio));
        int height = bottomY ?? Math.Max(1, (int)(bitmap.Height * profile.PanelHeightRatio));
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

        if (max >= 165 && max - min <= 90)
            return true;

        if (color.G >= 140 && color.G >= color.R + 25 && color.G >= color.B + 10)
            return true;

        if (color.R >= 145 && color.R >= color.G + 25 && color.R >= color.B + 20)
            return true;

        return false;
    }

    private static async Task<(string Text, List<string> Lines)> RecognizeTextDetailedAsync(Bitmap bitmap)
    {
        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));

        if (engine == null)
            return (string.Empty, new List<string>());

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        memoryStream.Position = 0;

        using IRandomAccessStream randomAccessStream = memoryStream.AsRandomAccessStream();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        OcrResult ocrResult = await engine.RecognizeAsync(softwareBitmap);
        string text = ocrResult?.Text ?? string.Empty;
        var lines = ocrResult?.Lines?
            .Select(line => line.Text?.Trim() ?? string.Empty)
            .Where(line => line.Length > 0)
            .ToList() ?? new List<string>();

        return (text, lines);
    }

    private static void ParseExfilAndTransitNames(
        string ocrText,
        ScreenshotExfilParseResult result,
        IReadOnlyList<string>? structuredLines = null)
    {
        if (string.IsNullOrWhiteSpace(ocrText) && (structuredLines == null || structuredLines.Count == 0))
            return;

        string normalizedText = NormalizeOcrText(ocrText);

        IEnumerable<string> lines = structuredLines != null && structuredLines.Count > 0
            ? structuredLines.Select(NormalizeOcrText)
            : normalizedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = CleanOcrName(line.Trim());
            if (trimmed.Length == 0 || IsIgnoredPanelLine(trimmed))
                continue;

            TryAddExfilFromLine(trimmed, result);
            TryAddTransitFromLine(trimmed, result);
        }

        foreach (Match match in ExfilInlineRegex.Matches(normalizedText))
            AddUnique(result.ExfilNames, CleanOcrName(match.Groups[1].Value));

        foreach (Match match in TransitInlineRegex.Matches(normalizedText))
            AddUnique(result.TransitNames, CleanOcrName(match.Groups[1].Value));

        ParseUnlabeledExtractLines(normalizedText, result);
        ParseTransitSectionLines(normalizedText, result);
    }

    private static void TryAddExfilFromLine(string trimmed, ScreenshotExfilParseResult result)
    {
        Match exfilMatch = ExfilLineRegex.Match(trimmed);
        if (exfilMatch.Success)
        {
            AddUnique(result.ExfilNames, CleanOcrName(exfilMatch.Groups[2].Value));
            return;
        }

        Match numbered = NumberedExtractLineRegex.Match(trimmed);
        if (numbered.Success && !trimmed.Contains("TRANSIT", StringComparison.OrdinalIgnoreCase))
            AddUnique(result.ExfilNames, CleanOcrName(numbered.Groups[2].Value));
    }

    private static void TryAddTransitFromLine(string trimmed, ScreenshotExfilParseResult result)
    {
        Match transitMatch = TransitLineRegex.Match(trimmed);
        if (transitMatch.Success)
            AddUnique(result.TransitNames, CleanOcrName(transitMatch.Groups[1].Value));
    }

    private static string NormalizeOcrText(string ocrText)
    {
        return ocrText
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace("EXI T", "EXIT", StringComparison.OrdinalIgnoreCase)
            .Replace("EX1T", "EXIT", StringComparison.OrdinalIgnoreCase)
            .Replace("EXI1", "EXIT", StringComparison.OrdinalIgnoreCase)
            .Replace("EX IT", "EXIT", StringComparison.OrdinalIgnoreCase)
            .Replace("EXFILO", "EXFIL0", StringComparison.OrdinalIgnoreCase)
            .Replace("EXITO", "EXIT0", StringComparison.OrdinalIgnoreCase)
            .Replace("TRANSI T", "TRANSIT", StringComparison.OrdinalIgnoreCase)
            .Replace("TRANSI1", "TRANSIT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredPanelLine(string line)
    {
        if (TimerLineRegex.IsMatch(line))
            return true;

        if (line.Contains("extraction point", StringComparison.OrdinalIgnoreCase))
            return true;

        if (line.Contains("keycard required", StringComparison.OrdinalIgnoreCase))
            return true;

        if (line.StartsWith("Find an", StringComparison.OrdinalIgnoreCase))
            return true;

        return line.Equals("TRANSIT", StringComparison.OrdinalIgnoreCase) ||
               line.Equals("EXFIL", StringComparison.OrdinalIgnoreCase) ||
               line.Equals("EXIT", StringComparison.OrdinalIgnoreCase);
    }

    private static void ParseUnlabeledExtractLines(string normalizedText, ScreenshotExfilParseResult result)
    {
        int transitIndex = normalizedText.IndexOf("TRANSIT", StringComparison.OrdinalIgnoreCase);
        string extractSection = transitIndex > 0 ? normalizedText[..transitIndex] : normalizedText;

        foreach (string line in extractSection.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = CleanOcrName(line.Trim());
            if (trimmed.Length == 0 || IsIgnoredPanelLine(trimmed))
                continue;

            if (ExfilLineRegex.IsMatch(trimmed) || NumberedExtractLineRegex.IsMatch(trimmed))
                continue;

            if (trimmed.Contains("TRANSIT", StringComparison.OrdinalIgnoreCase))
                continue;

            if (LooksLikeExtractName(trimmed))
                AddUnique(result.ExfilNames, trimmed);
        }
    }

    private static void ParseTransitSectionLines(string normalizedText, ScreenshotExfilParseResult result)
    {
        int transitIndex = normalizedText.IndexOf("TRANSIT", StringComparison.OrdinalIgnoreCase);
        if (transitIndex < 0)
            return;

        string transitSection = normalizedText[transitIndex..];
        foreach (string line in transitSection.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = CleanOcrName(line.Trim());
            if (trimmed.Length == 0 || IsIgnoredPanelLine(trimmed))
                continue;

            if (TransitLineRegex.IsMatch(trimmed))
                continue;

            if (trimmed.StartsWith("Transit to ", StringComparison.OrdinalIgnoreCase))
                AddUnique(result.TransitNames, trimmed);

            if (trimmed.Contains(" to ", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("Transit", StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(result.TransitNames, trimmed);
            }
        }
    }

    private static bool LooksLikeExtractName(string value)
    {
        if (value.Length < 3)
            return false;

        if (!char.IsLetter(value[0]))
            return false;

        return !value.StartsWith("EXFIL", StringComparison.OrdinalIgnoreCase) &&
               !value.StartsWith("EXIT", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanOcrName(string value)
    {
        string cleaned = value.Trim();
        cleaned = Regex.Replace(cleaned, @"^\*+|\*+$", "");
        cleaned = Regex.Replace(cleaned, @"\s+\d{1,2}:\d{2}(?::\d{2})?.*$", "");
        cleaned = Regex.Replace(cleaned, @"\s+\?{1,2}:\?{1,2}:\?{1,2}.*$", "");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
        return cleaned.Trim(' ', '-', ':', '.', ',', '*', '|');
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
