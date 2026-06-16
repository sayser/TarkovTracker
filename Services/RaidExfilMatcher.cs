using System.Text.RegularExpressions;
using TarkovTracker.Models;

namespace TarkovTracker.Services;

public static class RaidExfilMatcher
{
    public static RaidExfilMatchResult Match(
        string normalizedMapName,
        IEnumerable<string> ocrExfilNames,
        IEnumerable<string> ocrTransitNames,
        MapDataService mapData,
        string? rawOcrText = null)
    {
        var result = new RaidExfilMatchResult();

        if (mapData.ExtractsByMapName.TryGetValue(normalizedMapName, out var extracts))
        {
            foreach (string ocrName in ocrExfilNames)
            {
                foreach (ExtractInfo extract in extracts)
                {
                    if (string.IsNullOrWhiteSpace(extract.Name))
                        continue;

                    if (!NamesMatch(ocrName, extract.Name))
                        continue;

                    if (!result.ExtractNames.Contains(extract.Name, StringComparer.OrdinalIgnoreCase))
                        result.ExtractNames.Add(extract.Name);
                }
            }

            if (result.ExtractNames.Count == 0)
                SupplementExtractsFromRawOcr(result, extracts, rawOcrText);
        }

        if (mapData.TransitsByMapName.TryGetValue(normalizedMapName, out var transits))
        {
            foreach (string ocrName in ocrTransitNames)
            {
                foreach (TransitInfo transit in transits)
                {
                    if (string.IsNullOrWhiteSpace(transit.Description))
                        continue;

                    if (!NamesMatch(ocrName, transit.Description))
                        continue;

                    if (!result.TransitNames.Contains(transit.Description, StringComparer.OrdinalIgnoreCase))
                        result.TransitNames.Add(transit.Description);
                }
            }
        }

        return result;
    }

    private static void SupplementExtractsFromRawOcr(
        RaidExfilMatchResult result,
        List<ExtractInfo> extracts,
        string? rawOcrText)
    {
        if (string.IsNullOrWhiteSpace(rawOcrText))
            return;

        string extractSection = GetExtractSection(rawOcrText);
        string normalizedOcr = NormalizeName(extractSection);
        if (normalizedOcr.Length == 0)
            return;

        foreach (ExtractInfo extract in extracts)
        {
            if (string.IsNullOrWhiteSpace(extract.Name))
                continue;

            string normalizedExtract = NormalizeName(extract.Name);
            if (normalizedExtract.Length < 4)
                continue;

            if (!normalizedOcr.Contains(normalizedExtract, StringComparison.Ordinal))
                continue;

            if (!result.ExtractNames.Contains(extract.Name, StringComparer.OrdinalIgnoreCase))
                result.ExtractNames.Add(extract.Name);
        }
    }

    private static string GetExtractSection(string rawOcrText)
    {
        int transitIndex = rawOcrText.IndexOf("TRANSIT", StringComparison.OrdinalIgnoreCase);
        return transitIndex > 0 ? rawOcrText[..transitIndex] : rawOcrText;
    }

    private static bool NamesMatch(string ocrName, string dataName)
    {
        string normalizedOcr = NormalizeName(ocrName);
        string normalizedData = NormalizeName(dataName);

        if (normalizedOcr.Length == 0 || normalizedData.Length == 0)
            return false;

        if (normalizedOcr == normalizedData)
            return true;

        if (normalizedOcr.Contains(normalizedData, StringComparison.Ordinal) ||
            normalizedData.Contains(normalizedOcr, StringComparison.Ordinal))
        {
            return true;
        }

        return SimilarityRatio(normalizedOcr, normalizedData) >= 0.82;
    }

    private static string NormalizeName(string value)
    {
        return Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]", "");
    }

    private static double SimilarityRatio(string left, string right)
    {
        int distance = LevenshteinDistance(left, right);
        int maxLength = Math.Max(left.Length, right.Length);
        if (maxLength == 0)
            return 1.0;

        return 1.0 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        int[,] costs = new int[left.Length + 1, right.Length + 1];

        for (int i = 0; i <= left.Length; i++)
            costs[i, 0] = i;

        for (int j = 0; j <= right.Length; j++)
            costs[0, j] = j;

        for (int i = 1; i <= left.Length; i++)
        {
            for (int j = 1; j <= right.Length; j++)
            {
                int substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                costs[i, j] = Math.Min(
                    Math.Min(costs[i - 1, j] + 1, costs[i, j - 1] + 1),
                    costs[i - 1, j - 1] + substitutionCost);
            }
        }

        return costs[left.Length, right.Length];
    }
}
