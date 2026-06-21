using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class WebMapMarker
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("markerType")]
    public string MarkerType { get; set; } = "";

    [JsonPropertyName("faction")]
    public string Faction { get; set; } = "";

    [JsonPropertyName("questCategory")]
    public string QuestCategory { get; set; } = "";

    [JsonPropertyName("questObjectiveType")]
    public string QuestObjectiveType { get; set; } = "";

    [JsonPropertyName("questItem")]
    public string QuestItem { get; set; } = "";

    [JsonPropertyName("questItemShortName")]
    public string QuestItemShortName { get; set; } = "";

    [JsonPropertyName("questItemIconLink")]
    public string QuestItemIconLink { get; set; } = "";

    [JsonPropertyName("questTrader")]
    public string QuestTrader { get; set; } = "";

    [JsonPropertyName("questMinPlayerLevel")]
    public int QuestMinPlayerLevel { get; set; }

    [JsonPropertyName("questOptional")]
    public bool QuestOptional { get; set; }

    [JsonPropertyName("cssClass")]
    public string CssClass { get; set; } = "";

    [JsonPropertyName("tooltip")]
    public string Tooltip { get; set; } = "";

    [JsonPropertyName("zoneName")]
    public string ZoneName { get; set; } = "";

    [JsonPropertyName("categories")]
    public string Categories { get; set; } = "";

    [JsonPropertyName("conditions")]
    public string Conditions { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";

    [JsonPropertyName("labelRotation")]
    public double LabelRotation { get; set; }

    [JsonPropertyName("labelSize")]
    public double LabelSize { get; set; }

    [JsonPropertyName("normalizedX")]
    public double NormalizedX { get; set; }

    [JsonPropertyName("normalizedY")]
    public double NormalizedY { get; set; }

    [JsonPropertyName("hideLabel")]
    public bool HideLabel { get; set; }

    [JsonPropertyName("gameY")]
    public double? GameY { get; set; }

    [JsonPropertyName("outline")]
    public List<WebMapOutlinePoint>? Outline { get; set; }

    [JsonPropertyName("switchId")]
    public string SwitchId { get; set; } = "";

    [JsonPropertyName("linkedSwitchIds")]
    public List<string> LinkedSwitchIds { get; set; } = new();

    [JsonPropertyName("questSlug")]
    public string QuestSlug { get; set; } = "";

    [JsonPropertyName("extractId")]
    public string ExtractId { get; set; } = "";
}

public class WebMapOutlinePoint
{
    [JsonPropertyName("normalizedX")]
    public double NormalizedX { get; set; }

    [JsonPropertyName("normalizedY")]
    public double NormalizedY { get; set; }
}

public class WebQuestFilterPayload
{
    [JsonPropertyName("questName")]
    public string QuestName { get; set; } = "";

    [JsonPropertyName("trader")]
    public string Trader { get; set; } = "all";
}

public class WebMarkerClickMessage
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("markerType")]
    public string MarkerType { get; set; } = "";

    [JsonPropertyName("faction")]
    public string Faction { get; set; } = "";

    [JsonPropertyName("zoneName")]
    public string ZoneName { get; set; } = "";

    [JsonPropertyName("categories")]
    public string Categories { get; set; } = "";

    [JsonPropertyName("conditions")]
    public string Conditions { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";

    [JsonPropertyName("questSlug")]
    public string QuestSlug { get; set; } = "";

    [JsonPropertyName("linkedSwitchIds")]
    public List<string> LinkedSwitchIds { get; set; } = new();
}
