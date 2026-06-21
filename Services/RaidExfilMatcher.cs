using System.Text.RegularExpressions;
using TarkovTracker.Models;

namespace TarkovTracker.Services;

public static class RaidExfilMatcher
{
    private const double SimilarityThreshold = 0.78;
    private const double WordOverlapThreshold = 0.72;

    public static RaidExfilMatchResult Match(
        string normalizedMapName,
        IEnumerable<string> ocrExfilNames,
        IEnumerable<string> ocrTransitNames,
        MapDataService mapData,
        string? rawOcrText = null)
    {
        var result = new RaidExfilMatchResult();
        var parsedExfilNames = ocrExfilNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parsedTransitNames = ocrTransitNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mapData.ExtractsByMapName.TryGetValue(normalizedMapName, out var extracts))
        {
            foreach (string ocrName in parsedExfilNames)
                TryMatchExtract(ocrName, extracts, result);

            SupplementExtractsFromRawOcr(result, extracts, rawOcrText);
        }

        if (mapData.TransitsByMapName.TryGetValue(normalizedMapName, out var transits))
        {
            foreach (string ocrName in parsedTransitNames)
                TryMatchTransit(ocrName, transits, result);

            SupplementTransitsFromRawOcr(result, transits, rawOcrText);
        }

        return result;
    }

    private static void TryMatchExtract(string ocrName, List<ExtractInfo> extracts, RaidExfilMatchResult result)
    {
        foreach (ExtractInfo extract in extracts)
        {
            if (string.IsNullOrWhiteSpace(extract.Name))
                continue;

            if (!NamesMatch(ocrName, extract.Name))
                continue;

            AddExtractMatch(result, extract);
        }
    }

    private static void TryMatchTransit(string ocrName, List<TransitInfo> transits, RaidExfilMatchResult result)
    {
        foreach (TransitInfo transit in transits)
        {
            if (string.IsNullOrWhiteSpace(transit.Description))
                continue;

            if (!NamesMatch(ocrName, transit.Description))
                continue;

            AddUnique(result.TransitNames, transit.Description);
        }
    }

    private static void SupplementExtractsFromRawOcr(
        RaidExfilMatchResult result,
        List<ExtractInfo> extracts,
        string? rawOcrText)
    {
        if (string.IsNullOrWhiteSpace(rawOcrText))
            return;

        string extractSection = GetExtractSection(rawOcrText);
        string normalizedExtractSection = NormalizeName(extractSection);
        string normalizedFullText = NormalizeName(rawOcrText);

        foreach (ExtractInfo extract in extracts)
        {
            if (string.IsNullOrWhiteSpace(extract.Name))
                continue;

            if (HasExtractMatch(result, extract))
                continue;

            if (AppearsInOcrText(extract.Name, normalizedExtractSection, normalizedFullText))
                AddExtractMatch(result, extract);
        }
    }

    private static void SupplementTransitsFromRawOcr(
        RaidExfilMatchResult result,
        List<TransitInfo> transits,
        string? rawOcrText)
    {
        if (string.IsNullOrWhiteSpace(rawOcrText))
            return;

        string transitSection = GetTransitSection(rawOcrText);
        string normalizedTransitSection = NormalizeName(transitSection);
        string normalizedFullText = NormalizeName(rawOcrText);

        foreach (TransitInfo transit in transits)
        {
            if (string.IsNullOrWhiteSpace(transit.Description))
                continue;

            if (result.TransitNames.Contains(transit.Description, StringComparer.OrdinalIgnoreCase))
                continue;

            if (AppearsInOcrText(transit.Description, normalizedTransitSection, normalizedFullText))
                AddUnique(result.TransitNames, transit.Description);
        }
    }

    private static bool HasExtractMatch(RaidExfilMatchResult result, ExtractInfo extract)
    {
        if (!string.IsNullOrWhiteSpace(extract.Id))
        {
            return result.ExtractMatches.Any(match =>
                string.Equals(match.Id, extract.Id, StringComparison.OrdinalIgnoreCase));
        }

        return result.ExtractMatches.Any(match =>
            string.Equals(match.Name, extract.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(match.Faction, extract.Faction, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddExtractMatch(RaidExfilMatchResult result, ExtractInfo extract)
    {
        if (HasExtractMatch(result, extract))
            return;

        result.ExtractMatches.Add(new RaidExtractMatch
        {
            Id = extract.Id ?? string.Empty,
            Name = extract.Name,
            Faction = extract.Faction ?? string.Empty
        });
    }

    private static bool AppearsInOcrText(string dataName, string normalizedSection, string normalizedFullText)
    {
        string normalizedData = NormalizeName(dataName);
        if (normalizedData.Length < 4)
            return false;

        if (normalizedSection.Contains(normalizedData, StringComparison.Ordinal) ||
            normalizedFullText.Contains(normalizedData, StringComparison.Ordinal))
        {
            return true;
        }

        if (WordOverlapScore(normalizedSection, dataName) >= WordOverlapThreshold ||
            WordOverlapScore(normalizedFullText, dataName) >= WordOverlapThreshold)
        {
            return true;
        }

        foreach (string candidate in BuildOcrCandidates(dataName))
        {
            string normalizedCandidate = NormalizeName(candidate);
            if (normalizedCandidate.Length < 4)
                continue;

            if (normalizedSection.Contains(normalizedCandidate, StringComparison.Ordinal) ||
                normalizedFullText.Contains(normalizedCandidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> BuildOcrCandidates(string dataName)
    {
        yield return dataName;

        int parenIndex = dataName.IndexOf('(');
        if (parenIndex > 0)
            yield return dataName[..parenIndex].Trim();

        string withoutParens = Regex.Replace(dataName, @"\([^)]*\)", "").Trim();
        if (!string.Equals(withoutParens, dataName, StringComparison.Ordinal))
            yield return withoutParens;
    }

    private static string GetExtractSection(string rawOcrText)
    {
        int transitIndex = rawOcrText.IndexOf("TRANSIT", StringComparison.OrdinalIgnoreCase);
        return transitIndex > 0 ? rawOcrText[..transitIndex] : rawOcrText;
    }

    private static string GetTransitSection(string rawOcrText)
    {
        int transitIndex = rawOcrText.IndexOf("TRANSIT", StringComparison.OrdinalIgnoreCase);
        return transitIndex >= 0 ? rawOcrText[transitIndex..] : rawOcrText;
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

        if (WordOverlapScore(normalizedOcr, dataName) >= WordOverlapThreshold ||
            WordOverlapScore(normalizedData, ocrName) >= WordOverlapThreshold)
            return true;

        return SimilarityRatio(normalizedOcr, normalizedData) >= SimilarityThreshold;
    }

    private static double WordOverlapScore(string normalizedOcrText, string dataName)
    {
        List<string> words = GetSignificantWords(dataName);
        if (words.Count == 0)
            return 0;

        int hits = 0;
        foreach (string word in words)
        {
            string normalizedWord = NormalizeName(word);
            if (normalizedWord.Length < 3)
                continue;

            if (normalizedOcrText.Contains(normalizedWord, StringComparison.Ordinal))
                hits++;
        }

        return (double)hits / words.Count;
    }

    private static List<string> GetSignificantWords(string value)
    {
        return Regex.Split(value.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(word => word.Length >= 3)
            .Where(word => word is not ("the" or "and" or "for" or "from"))
            .ToList();
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

    private static void AddUnique(List<string> names, string value)
    {
        if (names.Contains(value, StringComparer.OrdinalIgnoreCase))
            return;

        names.Add(value);
    }
}
