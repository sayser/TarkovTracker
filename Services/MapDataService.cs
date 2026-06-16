using System.IO;
using System.Text.Json;
using TarkovTracker.Models;

namespace TarkovTracker.Services;

public class MapDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _configDir;

    public Dictionary<string, MapConfig> MapConfigs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<ExtractInfo>> ExtractsByMapName { get; } = new();
    public Dictionary<string, List<TransitInfo>> TransitsByMapName { get; } = new();
    public Dictionary<string, List<SpawnInfo>> SpawnsByMapName { get; } = new();
    public Dictionary<string, List<BossSpawnMarker>> BossSpawnMarkersByMapName { get; } = new();
    public Dictionary<string, List<MapLabel>> LabelsByMapName { get; } = new();
    public Dictionary<string, List<QuestMarker>> QuestMarkersByMapName { get; } = new();
    public Dictionary<string, List<HazardInfo>> HazardsByMapName { get; } = new();
    public Dictionary<string, List<MapSwitchInfo>> SwitchesByMapName { get; } = new();
    public Dictionary<string, BtrMapConfig> BtrByMapName { get; } = new();
    public Dictionary<string, MapLevelsConfig> MapLevelsByKey { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? LastStatusMessage { get; private set; }

    public MapDataService(string? baseDirectory = null)
    {
        _configDir = Path.Combine(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "Config");
    }

    public void LoadAll()
    {
        LoadMapConfig();
        LoadMapLevelsConfig();
        LoadExtracts();
        LoadTransits();
        LoadSpawns();
        LoadBossSpawns();
        LoadLabels();
        LoadQuestMarkers();
        LoadHazards();
        LoadSwitches();
        LoadBtr();
    }

    public static bool MapSupportsBtr(string normalizedMapName) =>
        normalizedMapName is "woods" or "streetsoftarkov";

    public static string NormalizeMapName(string name)
    {
        return new string(
            (name ?? "")
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    public bool TryResolveMapConfig(string mapFileName, out MapConfig? config)
    {
        if (MapConfigs.TryGetValue(mapFileName, out config))
            return true;

        string lowerKey = mapFileName.ToLowerInvariant();
        if (MapConfigs.TryGetValue(lowerKey, out config))
            return true;

        string normalizedKey = NormalizeMapName(mapFileName);
        if (MapConfigs.TryGetValue(normalizedKey, out config))
            return true;

        config = MapConfigs.Values.FirstOrDefault(map =>
            string.Equals(
                Path.GetFileNameWithoutExtension(map.Svg.File),
                mapFileName,
                StringComparison.OrdinalIgnoreCase));

        return config != null;
    }

    public MapLevelsConfig? GetMapLevelsForMap(string mapFileName)
    {
        if (MapLevelsByKey.TryGetValue(mapFileName, out MapLevelsConfig? config))
            return config;

        if (MapLevelsByKey.TryGetValue(mapFileName.ToLowerInvariant(), out config))
            return config;

        if (MapLevelsByKey.TryGetValue(NormalizeMapName(mapFileName), out config))
            return config;

        return null;
    }

    private string ConfigPath(string fileName) => Path.Combine(_configDir, fileName);

    private void LoadMapConfig()
    {
        MapConfigs.Clear();
        string configFile = ConfigPath("maps.json");

        if (!File.Exists(configFile))
        {
            LastStatusMessage = $"Config file not found: {configFile}";
            return;
        }

        var rawMaps = JsonSerializer.Deserialize<Dictionary<string, MapConfig>>(
            File.ReadAllText(configFile),
            JsonOptions);

        if (rawMaps == null)
            return;

        foreach (var entry in rawMaps)
        {
            MapConfig map = entry.Value;
            if (map.Svg == null || string.IsNullOrWhiteSpace(map.Svg.File))
                continue;

            RegisterMapConfig(Path.GetFileNameWithoutExtension(map.Svg.File), map);
            RegisterMapConfig(entry.Key, map);

            if (!string.IsNullOrWhiteSpace(map.Locale?.En))
                RegisterMapConfig(map.Locale.En, map);
        }
    }

    private void RegisterMapConfig(string key, MapConfig map)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        MapConfigs[key] = map;

        string lowerKey = key.ToLowerInvariant();
        if (!MapConfigs.ContainsKey(lowerKey))
            MapConfigs[lowerKey] = map;

        string normalizedKey = NormalizeMapName(key);
        if (!MapConfigs.ContainsKey(normalizedKey))
            MapConfigs[normalizedKey] = map;
    }

    private void LoadMapLevelsConfig()
    {
        MapLevelsByKey.Clear();
        string mapLevelsFile = ConfigPath("map_levels.json");

        if (!File.Exists(mapLevelsFile))
            return;

        var rawLevels = JsonSerializer.Deserialize<Dictionary<string, MapLevelsConfig>>(
            File.ReadAllText(mapLevelsFile),
            JsonOptions);

        if (rawLevels == null)
            return;

        foreach (var entry in rawLevels)
        {
            RegisterMapLevels(entry.Key, entry.Value);

            if (TryResolveMapConfig(entry.Key, out MapConfig? mapConfig) && mapConfig?.Svg?.File != null)
            {
                RegisterMapLevels(Path.GetFileNameWithoutExtension(mapConfig.Svg.File), entry.Value);

                if (!string.IsNullOrWhiteSpace(mapConfig.Locale?.En))
                    RegisterMapLevels(mapConfig.Locale.En, entry.Value);
            }
        }
    }

    private void RegisterMapLevels(string key, MapLevelsConfig config)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        MapLevelsByKey[key] = config;

        string lowerKey = key.ToLowerInvariant();
        if (!MapLevelsByKey.ContainsKey(lowerKey))
            MapLevelsByKey[lowerKey] = config;

        string normalizedKey = NormalizeMapName(key);
        if (!MapLevelsByKey.ContainsKey(normalizedKey))
            MapLevelsByKey[normalizedKey] = config;
    }

    private void LoadExtracts()
    {
        ExtractsByMapName.Clear();
        string filePath = ConfigPath("tarkov_extracts_raw.json");

        if (!File.Exists(filePath))
        {
            LastStatusMessage = $"Extracts file not found: {filePath}";
            return;
        }

        var data = JsonSerializer.Deserialize<TarkovExtractsRoot>(File.ReadAllText(filePath), JsonOptions);
        if (data?.Data?.Maps == null)
            return;

        foreach (var map in data.Data.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.Name))
                continue;

            ExtractsByMapName[NormalizeMapName(map.Name)] = map.Extracts ?? new List<ExtractInfo>();
        }
    }

    private void LoadTransits()
    {
        TransitsByMapName.Clear();
        string filePath = ConfigPath("tarkov_transits_raw.json");

        if (!File.Exists(filePath))
        {
            LastStatusMessage = $"Transits file not found: {filePath}";
            return;
        }

        var data = JsonSerializer.Deserialize<TarkovTransitsRoot>(File.ReadAllText(filePath), JsonOptions);
        if (data?.Data?.Maps == null)
            return;

        foreach (var map in data.Data.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.Name))
                continue;

            TransitsByMapName[NormalizeMapName(map.Name)] = map.Transits ?? new List<TransitInfo>();
        }
    }

    private void LoadSpawns()
    {
        SpawnsByMapName.Clear();
        string filePath = ConfigPath("tarkov_spawns_raw.json");

        if (!File.Exists(filePath))
        {
            LastStatusMessage = $"Spawns file not found: {filePath}";
            return;
        }

        var data = JsonSerializer.Deserialize<TarkovSpawnsRoot>(File.ReadAllText(filePath), JsonOptions);
        if (data?.Data?.Maps == null)
            return;

        foreach (var map in data.Data.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.Name))
                continue;

            SpawnsByMapName[NormalizeMapName(map.Name)] = map.Spawns ?? new List<SpawnInfo>();
        }
    }

    private void LoadBossSpawns()
    {
        BossSpawnMarkersByMapName.Clear();
        string filePath = ConfigPath("tarkov_boss_spawn_markers.json");

        if (!File.Exists(filePath))
            return;

        var data = JsonSerializer.Deserialize<Dictionary<string, List<BossSpawnMarker>>>(
            File.ReadAllText(filePath),
            JsonOptions);

        if (data == null)
            return;

        foreach (var mapEntry in data)
        {
            string mapKey = NormalizeMapName(mapEntry.Key);

            if (!BossSpawnMarkersByMapName.ContainsKey(mapKey))
                BossSpawnMarkersByMapName[mapKey] = new List<BossSpawnMarker>();

            foreach (var marker in mapEntry.Value ?? new List<BossSpawnMarker>())
            {
                if (string.IsNullOrWhiteSpace(marker.BossName))
                    continue;

                BossSpawnMarkersByMapName[mapKey].Add(marker);
            }
        }
    }

    private void LoadLabels()
    {
        LabelsByMapName.Clear();
        string filePath = ConfigPath("tarkov_labels_raw.json");

        if (!File.Exists(filePath))
        {
            LastStatusMessage = $"Labels file not found: {filePath}";
            return;
        }

        var data = JsonSerializer.Deserialize<Dictionary<string, List<MapLabel>>>(
            File.ReadAllText(filePath),
            JsonOptions);

        if (data == null)
            return;

        foreach (var mapEntry in data)
        {
            string mapKey = NormalizeMapName(mapEntry.Key);

            if (!LabelsByMapName.ContainsKey(mapKey))
                LabelsByMapName[mapKey] = new List<MapLabel>();

            foreach (var label in mapEntry.Value ?? new List<MapLabel>())
            {
                if (string.IsNullOrWhiteSpace(label.Text))
                    continue;

                if (string.IsNullOrWhiteSpace(label.MapKey))
                    label.MapKey = mapEntry.Key;

                LabelsByMapName[mapKey].Add(label);
            }
        }
    }

    private void LoadQuestMarkers()
    {
        QuestMarkersByMapName.Clear();
        string filePath = ConfigPath("tarkov_quest_markers.json");

        if (!File.Exists(filePath))
        {
            LastStatusMessage = $"Quest markers file not found: {filePath}";
            return;
        }

        var data = JsonSerializer.Deserialize<Dictionary<string, List<QuestMarker>>>(
            File.ReadAllText(filePath),
            JsonOptions);

        if (data == null)
            return;

        foreach (var mapEntry in data)
        {
            string mapKey = NormalizeMapName(mapEntry.Key);

            if (!QuestMarkersByMapName.ContainsKey(mapKey))
                QuestMarkersByMapName[mapKey] = new List<QuestMarker>();

            foreach (var questMarker in mapEntry.Value ?? new List<QuestMarker>())
            {
                if (string.IsNullOrWhiteSpace(questMarker.Quest) || questMarker.X == null || questMarker.Z == null)
                    continue;

                if (questMarker.X == 0 && questMarker.Z == 0)
                    continue;

                QuestMarkersByMapName[mapKey].Add(questMarker);
            }
        }
    }

    private void LoadHazards()
    {
        HazardsByMapName.Clear();
        string filePath = ConfigPath("tarkov_hazards_raw.json");

        if (!File.Exists(filePath))
            return;

        var data = JsonSerializer.Deserialize<TarkovHazardsRoot>(File.ReadAllText(filePath), JsonOptions);
        if (data?.Data?.Maps == null)
            return;

        foreach (var map in data.Data.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.Name))
                continue;

            HazardsByMapName[NormalizeMapName(map.Name)] = map.Hazards ?? new List<HazardInfo>();
        }
    }

    private void LoadSwitches()
    {
        SwitchesByMapName.Clear();
        string filePath = ConfigPath("tarkov_switches_raw.json");

        if (!File.Exists(filePath))
            return;

        var data = JsonSerializer.Deserialize<TarkovSwitchesRoot>(File.ReadAllText(filePath), JsonOptions);
        if (data?.Data?.Maps == null)
            return;

        foreach (var map in data.Data.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.Name))
                continue;

            SwitchesByMapName[NormalizeMapName(map.Name)] = map.Switches ?? new List<MapSwitchInfo>();
        }
    }

    private void LoadBtr()
    {
        BtrByMapName.Clear();
        string filePath = ConfigPath("tarkov_btr.json");

        if (!File.Exists(filePath))
            return;

        var data = JsonSerializer.Deserialize<Dictionary<string, BtrMapConfig>>(
            File.ReadAllText(filePath),
            JsonOptions);

        if (data == null)
            return;

        foreach (var mapEntry in data)
        {
            if (mapEntry.Value == null)
                continue;

            BtrByMapName[NormalizeMapName(mapEntry.Key)] = mapEntry.Value;
        }
    }
}
