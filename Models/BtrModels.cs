using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class BtrStopInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

public class BtrMapConfig
{
    [JsonPropertyName("stops")]
    public List<BtrStopInfo> Stops { get; set; } = new();
}
