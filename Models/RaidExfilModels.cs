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

public class RaidExfilMatchResult
{
    public List<string> ExtractNames { get; set; } = new();

    public List<string> TransitNames { get; set; } = new();
}

public class RaidExfilHighlightState
{
    public bool Active { get; set; }

    public List<string> ExtractNames { get; set; } = new();

    public List<string> TransitNames { get; set; } = new();

    public void Activate(IEnumerable<string> extractNames, IEnumerable<string> transitNames)
    {
        Active = true;
        ExtractNames = extractNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        TransitNames = transitNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Clear()
    {
        Active = false;
        ExtractNames.Clear();
        TransitNames.Clear();
    }

    public RaidExfilHighlightPayload ToPayload() => new()
    {
        Active = Active,
        ExtractNames = ExtractNames,
        TransitNames = TransitNames
    };
}

public class RaidExfilHighlightPayload
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("extractNames")]
    public List<string> ExtractNames { get; set; } = new();

    [JsonPropertyName("transitNames")]
    public List<string> TransitNames { get; set; } = new();
}
