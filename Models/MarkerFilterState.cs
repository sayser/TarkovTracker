using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class MarkerFilterState
{
    [JsonPropertyName("pmcExtracts")]
    public bool PmcExtracts { get; set; }

    [JsonPropertyName("scavExtracts")]
    public bool ScavExtracts { get; set; }

    [JsonPropertyName("sharedExtracts")]
    public bool SharedExtracts { get; set; }

    [JsonPropertyName("transits")]
    public bool Transits { get; set; }

    [JsonPropertyName("pmcSpawns")]
    public bool PmcSpawns { get; set; }

    [JsonPropertyName("scavSpawns")]
    public bool ScavSpawns { get; set; }

    [JsonPropertyName("bossSpawns")]
    public bool BossSpawns { get; set; }

    [JsonPropertyName("labels")]
    public bool Labels { get; set; }

    [JsonPropertyName("questItems")]
    public bool QuestItems { get; set; }

    [JsonPropertyName("questObjectives")]
    public bool QuestObjectives { get; set; }

    [JsonPropertyName("hazards")]
    public bool Hazards { get; set; }

    [JsonPropertyName("switches")]
    public bool Switches { get; set; }

    [JsonPropertyName("btrStops")]
    public bool BtrStops { get; set; }
}
