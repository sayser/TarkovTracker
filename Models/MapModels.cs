using System.Text.Json.Serialization;

namespace TarkovTracker.Models;

public class MapLevelStatePayload
{
    [JsonPropertyName("defaultLayerId")]
    public string? DefaultLayerId { get; set; }

    [JsonPropertyName("overlayLayerIds")]
    public List<string> OverlayLayerIds { get; set; } = new();

    [JsonPropertyName("showBaseLayer")]
    public bool ShowBaseLayer { get; set; } = true;

    [JsonPropertyName("dimBase")]
    public bool DimBase { get; set; }

    [JsonPropertyName("activeLevelIds")]
    public List<string> ActiveLevelIds { get; set; } = new();
}

public class MapLevelsConfig
{
    [JsonPropertyName("defaultSvgLayer")]
    public string? DefaultSvgLayer { get; set; }

    [JsonPropertyName("levels")]
    public List<MapLevelEntry> Levels { get; set; } = new();
}

public class MapLevelEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("svgLayer")]
    public string? SvgLayer { get; set; }

    [JsonPropertyName("defaultVisible")]
    public bool DefaultVisible { get; set; }
}

public class MapConfig
{
    [JsonPropertyName("locale")]
    public MapLocale Locale { get; set; } = new();

    [JsonPropertyName("svg")]
    public MapSvg Svg { get; set; } = new();
}

public class MapLocale
{
    [JsonPropertyName("en")]
    public string En { get; set; } = "";
}

public class MapSvg
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("coordinateRotation")]
    public int CoordinateRotation { get; set; }

    [JsonPropertyName("transform")]
    public double[]? Transform { get; set; }

    [JsonPropertyName("nativeZoom")]
    public int NativeZoom { get; set; }

    [JsonPropertyName("mapPixelSize")]
    public int MapPixelSize { get; set; }

    [JsonPropertyName("svgBounds")]
    public double[][]? SvgBounds { get; set; }

    [JsonPropertyName("bounds")]
    public double[][] Bounds { get; set; } = Array.Empty<double[]>();
}
