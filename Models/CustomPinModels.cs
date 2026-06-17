using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class CustomPinEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("normalizedX")]
    public double NormalizedX { get; set; }

    [JsonPropertyName("normalizedY")]
    public double NormalizedY { get; set; }
}

public class CustomPinsChangedMessage
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = "";

    [JsonPropertyName("pins")]
    public List<CustomPinEntry> Pins { get; set; } = new();
}
