using System.Text.Json;
using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class TarkovExtractsRoot
{
    [JsonPropertyName("data")]
    public TarkovExtractsData Data { get; set; } = new();
}

public class TarkovExtractsData
{
    [JsonPropertyName("maps")]
    public List<TarkovExtractMap> Maps { get; set; } = new();
}

public class TarkovExtractMap
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("extracts")]
    public List<ExtractInfo> Extracts { get; set; } = new();
}

public class ExtractInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("faction")]
    public string Faction { get; set; } = "";

    [JsonPropertyName("conditions")]
    public string? Conditions { get; set; }

    [JsonPropertyName("requirements")]
    [JsonConverter(typeof(StringOrStringListJsonConverter))]
    public List<string> Requirements { get; set; } = new();

    [JsonPropertyName("switches")]
    public List<ExtractSwitch> Switches { get; set; } = new();

    [JsonPropertyName("transferItem")]
    public JsonElement? TransferItem { get; set; }

    [JsonPropertyName("position")]
    public MapPosition? Position { get; set; }
}

public class ExtractSwitch
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class TarkovHazardsRoot
{
    [JsonPropertyName("data")]
    public TarkovHazardsData Data { get; set; } = new();
}

public class TarkovHazardsData
{
    [JsonPropertyName("maps")]
    public List<TarkovHazardMap> Maps { get; set; } = new();
}

public class TarkovHazardMap
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("hazards")]
    public List<HazardInfo> Hazards { get; set; } = new();
}

public class HazardInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("hazardType")]
    public string HazardType { get; set; } = "";

    [JsonPropertyName("position")]
    public MapPosition? Position { get; set; }

    [JsonPropertyName("outline")]
    public List<MapOutlinePoint> Outline { get; set; } = new();
}

public class MapOutlinePoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

public class TarkovSwitchesRoot
{
    [JsonPropertyName("data")]
    public TarkovSwitchesData Data { get; set; } = new();
}

public class TarkovSwitchesData
{
    [JsonPropertyName("maps")]
    public List<TarkovSwitchMap> Maps { get; set; } = new();
}

public class TarkovSwitchMap
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("switches")]
    public List<MapSwitchInfo> Switches { get; set; } = new();
}

public class MapSwitchInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("position")]
    public MapPosition? Position { get; set; }
}

public class TarkovTransitsRoot
{
    [JsonPropertyName("data")]
    public TarkovTransitsData Data { get; set; } = new();
}

public class TarkovTransitsData
{
    [JsonPropertyName("maps")]
    public List<TarkovTransitMap> Maps { get; set; } = new();
}

public class TarkovTransitMap
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("transits")]
    public List<TransitInfo> Transits { get; set; } = new();
}

public class TransitInfo
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("conditions")]
    public string? Conditions { get; set; }

    [JsonPropertyName("position")]
    public MapPosition? Position { get; set; }
}

public class TarkovSpawnsRoot
{
    [JsonPropertyName("data")]
    public TarkovSpawnsData Data { get; set; } = new();
}

public class TarkovSpawnsData
{
    [JsonPropertyName("maps")]
    public List<TarkovSpawnMap> Maps { get; set; } = new();
}

public class TarkovSpawnMap
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("spawns")]
    public List<SpawnInfo> Spawns { get; set; } = new();
}

public class SpawnInfo
{
    [JsonPropertyName("zoneName")]
    public string ZoneName { get; set; } = "";

    [JsonPropertyName("sides")]
    public List<string> Sides { get; set; } = new();

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("position")]
    public MapPosition? Position { get; set; }
}

public class BossSpawnMarker
{
    [JsonPropertyName("bossName")]
    public string BossName { get; set; } = "";

    [JsonPropertyName("normalizedName")]
    public string NormalizedName { get; set; } = "";

    [JsonPropertyName("locationName")]
    public string LocationName { get; set; } = "";

    [JsonPropertyName("zoneName")]
    public string ZoneName { get; set; } = "";

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; }

    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("z")]
    public double? Z { get; set; }
}

public class QuestMarker
{
    [JsonPropertyName("quest")]
    public string Quest { get; set; } = "";

    [JsonPropertyName("questSlug")]
    public string QuestSlug { get; set; } = "";

    [JsonPropertyName("objectiveType")]
    public string ObjectiveType { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "objective";

    [JsonPropertyName("iconType")]
    public string IconType { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("questItem")]
    public string QuestItem { get; set; } = "";

    [JsonPropertyName("itemShortName")]
    public string ItemShortName { get; set; } = "";

    [JsonPropertyName("itemIconLink")]
    public string ItemIconLink { get; set; } = "";

    [JsonPropertyName("trader")]
    public string Trader { get; set; } = "";

    [JsonPropertyName("minPlayerLevel")]
    public int MinPlayerLevel { get; set; }

    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("z")]
    public double? Z { get; set; }
}

public class MapLabel
{
    [JsonPropertyName("mapKey")]
    public string MapKey { get; set; } = "";

    [JsonIgnore]
    public string MapName
    {
        get => MapKey;
        set => MapKey = value;
    }

    [JsonPropertyName("projection")]
    public string Projection { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("rotation")]
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double Rotation { get; set; }

    [JsonPropertyName("size")]
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double Size { get; set; }
}

public class MapPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

public sealed class StringOrStringListJsonConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new List<string>();

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;

        return element.ValueKind switch
        {
            JsonValueKind.String => new List<string> { element.GetString() ?? string.Empty },
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList(),
            _ => new List<string>()
        };
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
