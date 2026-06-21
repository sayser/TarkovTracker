using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class ScreenshotExfilParseResult
{
    public bool PanelDetected { get; set; }

    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    public string ResolutionProfile { get; set; } = "";

    public List<string> ExfilNames { get; set; } = new();

    public List<string> TransitNames { get; set; } = new();

    public string? RawOcrText { get; set; }
}

public class RaidExtractMatch
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("faction")]
    public string Faction { get; set; } = "";
}

public class RaidExfilMatchResult
{
    public List<RaidExtractMatch> ExtractMatches { get; set; } = new();

    public List<string> TransitNames { get; set; } = new();

    public List<string> ExtractDisplayNames =>
        ExtractMatches
            .Select(RaidExfilHighlightState.FormatExtractDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public class RaidExfilHighlightState
{
    public bool Active { get; set; }

    public List<RaidExtractMatch> ExtractMatches { get; set; } = new();

    public List<string> TransitNames { get; set; } = new();

    public List<string> ExtractDisplayNames =>
        ExtractMatches
            .Select(FormatExtractDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void Activate(IEnumerable<RaidExtractMatch> extractMatches, IEnumerable<string> transitNames)
    {
        Active = true;
        ExtractMatches = extractMatches
            .Where(match => !string.IsNullOrWhiteSpace(match.Id) || !string.IsNullOrWhiteSpace(match.Name))
            .GroupBy(match => string.IsNullOrWhiteSpace(match.Id)
                ? $"{match.Name}|{match.Faction}"
                : match.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        TransitNames = transitNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string FormatExtractDisplayName(RaidExtractMatch match)
    {
        if (string.IsNullOrWhiteSpace(match.Faction))
            return match.Name;

        return $"{match.Name} ({match.Faction.ToUpperInvariant()})";
    }

    public void Clear()
    {
        Active = false;
        ExtractMatches.Clear();
        TransitNames.Clear();
    }

    public RaidExfilHighlightPayload ToPayload() => new()
    {
        Active = Active,
        ExtractMatches = ExtractMatches,
        ExtractNames = ExtractMatches.Select(match => match.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        TransitNames = TransitNames
    };
}

public class RaidExfilHighlightPayload
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("extractMatches")]
    public List<RaidExtractMatch> ExtractMatches { get; set; } = new();

    [JsonPropertyName("extractNames")]
    public List<string> ExtractNames { get; set; } = new();

    [JsonPropertyName("transitNames")]
    public List<string> TransitNames { get; set; } = new();
}
