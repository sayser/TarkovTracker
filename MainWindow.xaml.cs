using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace TarkovTracker
{
    public partial class MainWindow : Window
    {
        private readonly string _mapsFolder;
        private readonly string _configFile;
        private readonly string _extractsFile;
        private readonly string _transitsFile;
        private readonly string _spawnsFile;
        private readonly string _labelsFile;
        private readonly string _questMarkersFile;
        private string _screenshotFolder;
        private readonly string _userSettingsFile;
        private OverlayWindow? _overlayWindow;
        private string? _currentMapPath;
        private string? _currentMapHtml;
        private string? _lastMapMarkersJson;
        private (double NormalizedX, double NormalizedY, double DirectionDegrees)? _lastPlayerMarker;

        private readonly Dictionary<string, MapConfig> _mapConfigs = new();
        private readonly Dictionary<string, List<ExtractInfo>> _extractsByMapName = new();
        private readonly Dictionary<string, List<TransitInfo>> _transitsByMapName = new();
        private readonly Dictionary<string, List<SpawnInfo>> _spawnsByMapName = new();
        private readonly Dictionary<string, List<MapLabel>> _labelsByMapName = new();
        private readonly Dictionary<string, List<QuestMarker>> _questMarkersByMapName = new();

        private MapConfig? _currentMapConfig;
        private string? _currentMapDisplayName;

        private FileSystemWatcher? _screenshotWatcher;
        private DateTime _lastProcessedTime = DateTime.MinValue;

        private double? _lastGameX;
        private double? _lastGameY;
        private double? _lastGameZ;
        private double? _lastDirection;

        private bool _webViewReady = false;
        private bool _webMessageHooked = false;

        public MainWindow()
        {
            InitializeComponent();

            _mapsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps");
            _configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "maps.json");
            _extractsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "tarkov_extracts_raw.json");
            _transitsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "tarkov_transits_raw.json");
            _spawnsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "tarkov_spawns_raw.json");
            _labelsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "tarkov_labels_raw.json");
            _questMarkersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "tarkov_quest_markers.json");
            _userSettingsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SayserTarkovTracker",
                "settings.json");

            _screenshotFolder = LoadSavedScreenshotFolder();

            if (string.IsNullOrWhiteSpace(_screenshotFolder))
            {
                _screenshotFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Escape from Tarkov",
                    "Screenshots");
            }

            LoadMapConfig();
            LoadExtracts();
            LoadTransits();
            LoadSpawns();
            LoadLabels();
            LoadQuestMarkers();
            LoadMapsDropdown();
            StartScreenshotMonitoring();
        }

        private string LoadSavedScreenshotFolder()
        {
            try
            {
                if (!File.Exists(_userSettingsFile))
                    return "";

                var settings = JsonSerializer.Deserialize<UserAppSettings>(
                    File.ReadAllText(_userSettingsFile),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return settings?.ScreenshotFolder ?? "";
            }
            catch
            {
                return "";
            }
        }

        private void SaveScreenshotFolder(string folder)
        {
            string dir = Path.GetDirectoryName(_userSettingsFile)!;
            Directory.CreateDirectory(dir);

            var settings = new UserAppSettings
            {
                ScreenshotFolder = folder
            };

            File.WriteAllText(
                _userSettingsFile,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void SetScreenshotFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Escape from Tarkov Screenshots Folder",
                InitialDirectory = Directory.Exists(_screenshotFolder)
                    ? _screenshotFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                _screenshotFolder = dialog.FolderName;
                SaveScreenshotFolder(_screenshotFolder);

                _screenshotWatcher?.Dispose();
                StartScreenshotMonitoring();

                StatusText.Text = $"Screenshot folder set to: {_screenshotFolder}";
                ParserStatusText.Text = "Screenshot folder updated";
                ParserStatusText.Foreground = Brushes.LawnGreen;
            }
        }


        private void LoadMapConfig()
        {
            _mapConfigs.Clear();

            if (!File.Exists(_configFile))
            {
                StatusText.Text = $"Config file not found: {_configFile}";
                return;
            }

            var rawMaps = JsonSerializer.Deserialize<Dictionary<string, MapConfig>>(
                File.ReadAllText(_configFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (rawMaps == null)
                return;

            foreach (var map in rawMaps.Values)
            {
                if (map.Svg == null || string.IsNullOrWhiteSpace(map.Svg.File))
                    continue;

                string key = Path.GetFileNameWithoutExtension(map.Svg.File);
                _mapConfigs[key] = map;
            }
        }

        private void LoadExtracts()
        {
            _extractsByMapName.Clear();

            if (!File.Exists(_extractsFile))
            {
                StatusText.Text = $"Extracts file not found: {_extractsFile}";
                return;
            }

            var data = JsonSerializer.Deserialize<TarkovExtractsRoot>(
                File.ReadAllText(_extractsFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data?.Data?.Maps == null)
                return;

            foreach (var map in data.Data.Maps)
            {
                if (string.IsNullOrWhiteSpace(map.Name))
                    continue;

                _extractsByMapName[NormalizeMapName(map.Name)] = map.Extracts ?? new List<ExtractInfo>();
            }
        }

        private void LoadTransits()
        {
            _transitsByMapName.Clear();

            if (!File.Exists(_transitsFile))
            {
                StatusText.Text = $"Transits file not found: {_transitsFile}";
                return;
            }

            var data = JsonSerializer.Deserialize<TarkovTransitsRoot>(
                File.ReadAllText(_transitsFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data?.Data?.Maps == null)
                return;

            foreach (var map in data.Data.Maps)
            {
                if (string.IsNullOrWhiteSpace(map.Name))
                    continue;

                _transitsByMapName[NormalizeMapName(map.Name)] = map.Transits ?? new List<TransitInfo>();
            }
        }

        private void LoadSpawns()
        {
            _spawnsByMapName.Clear();

            if (!File.Exists(_spawnsFile))
            {
                StatusText.Text = $"Spawns file not found: {_spawnsFile}";
                return;
            }

            var data = JsonSerializer.Deserialize<TarkovSpawnsRoot>(
                File.ReadAllText(_spawnsFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data?.Data?.Maps == null)
                return;

            foreach (var map in data.Data.Maps)
            {
                if (string.IsNullOrWhiteSpace(map.Name))
                    continue;

                _spawnsByMapName[NormalizeMapName(map.Name)] = map.Spawns ?? new List<SpawnInfo>();
            }
        }

        private void LoadLabels()
        {
            _labelsByMapName.Clear();

            if (!File.Exists(_labelsFile))
            {
                StatusText.Text = $"Labels file not found: {_labelsFile}";
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, List<MapLabel>>>(
                File.ReadAllText(_labelsFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data == null)
                return;

            foreach (var mapEntry in data)
            {
                string mapKey = NormalizeMapName(mapEntry.Key);

                if (!_labelsByMapName.ContainsKey(mapKey))
                    _labelsByMapName[mapKey] = new List<MapLabel>();

                foreach (var label in mapEntry.Value ?? new List<MapLabel>())
                {
                    if (string.IsNullOrWhiteSpace(label.Text))
                        continue;

                    if (string.IsNullOrWhiteSpace(label.MapKey))
                        label.MapKey = mapEntry.Key;

                    _labelsByMapName[mapKey].Add(label);
                }
            }
        }

        private void LoadQuestMarkers()
        {
            _questMarkersByMapName.Clear();

            if (!File.Exists(_questMarkersFile))
            {
                StatusText.Text = $"Quest markers file not found: {_questMarkersFile}";
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, List<QuestMarker>>>(
                File.ReadAllText(_questMarkersFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data == null)
                return;

            foreach (var mapEntry in data)
            {
                string mapKey = NormalizeMapName(mapEntry.Key);

                if (!_questMarkersByMapName.ContainsKey(mapKey))
                    _questMarkersByMapName[mapKey] = new List<QuestMarker>();

                foreach (var questMarker in mapEntry.Value ?? new List<QuestMarker>())
                {
                    if (string.IsNullOrWhiteSpace(questMarker.Quest) || questMarker.X == null || questMarker.Z == null)
                        continue;

                    _questMarkersByMapName[mapKey].Add(questMarker);
                }
            }
        }

        private void LoadMapsDropdown()
        {
            MapComboBox.Items.Clear();

            if (!Directory.Exists(_mapsFolder))
            {
                StatusText.Text = $"Maps folder not found: {_mapsFolder}";
                return;
            }

            var svgFiles = Directory
                .GetFiles(_mapsFolder, "*.svg")
                .OrderBy(Path.GetFileNameWithoutExtension)
                .ToList();

            foreach (var svgFile in svgFiles)
            {
                MapComboBox.Items.Add(new ComboBoxItem
                {
                    Content = Path.GetFileNameWithoutExtension(svgFile),
                    Tag = svgFile,
                    Foreground = Brushes.Black
                });
            }

            StatusText.Text =
                $"{svgFiles.Count} maps loaded. Extract maps: {_extractsByMapName.Count}. Transit maps: {_transitsByMapName.Count}. Spawn maps: {_spawnsByMapName.Count}. Label maps: {_labelsByMapName.Count}. Quest maps: {_questMarkersByMapName.Count}.";
        }

        private async void MapComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MapComboBox.SelectedItem is not ComboBoxItem item)
                return;

            string? mapPath = item.Tag as string;

            if (string.IsNullOrWhiteSpace(mapPath))
                return;

            string mapFileName = Path.GetFileNameWithoutExtension(mapPath);
            _currentMapPath = mapPath;

            CurrentMapText.Text = mapFileName;
            _mapConfigs.TryGetValue(mapFileName, out _currentMapConfig);

            _currentMapDisplayName =
                !string.IsNullOrWhiteSpace(_currentMapConfig?.Locale?.En)
                    ? _currentMapConfig.Locale.En
                    : mapFileName;

            await LoadSvgMapInWebView(mapPath);
            LoadLayersPanel(mapPath);

            DrawMapMarkersForCurrentMap();
            RedrawLastMarker();
            _ = SyncOverlayToCurrentMapAsync();

            // Disabled for now because it hides too many SVG layers on maps like Factory.
            // if (_lastGameY != null)
            //     AutoSwitchFloorLayers(_lastGameY.Value);

            StatusText.Text = _currentMapConfig == null
                ? $"Loaded map: {mapFileName}. No config found."
                : $"Loaded map: {mapFileName}. Rotation={_currentMapConfig.Svg.CoordinateRotation}.";
        }

        private async System.Threading.Tasks.Task LoadSvgMapInWebView(string mapPath)
        {
            _webViewReady = false;

            string svg = File.ReadAllText(mapPath);
            string html = BuildMapHtml(svg);
            _currentMapPath = mapPath;
            _currentMapHtml = html;

            await MapWebView.EnsureCoreWebView2Async();

            if (!_webMessageHooked)
            {
                MapWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                _webMessageHooked = true;
            }

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                MapWebView.NavigationCompleted -= Handler;
                tcs.TrySetResult(true);
            }

            MapWebView.NavigationCompleted += Handler;
            MapWebView.NavigateToString(html);

            await tcs.Task;

            _webViewReady = true;
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var marker = JsonSerializer.Deserialize<WebMarkerClickMessage>(
                    e.WebMessageAsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (marker == null || marker.MessageType != "markerClicked")
                    return;

                SelectedMarkerNameText.Text = marker.Name;

                var details = new List<string>
                {
                    $"Type: {marker.MarkerType}"
                };

                if (!string.IsNullOrWhiteSpace(marker.Faction))
                    details.Add($"Faction/Side: {marker.Faction}");

                if (!string.IsNullOrWhiteSpace(marker.ZoneName))
                    details.Add($"Zone: {marker.ZoneName}");

                if (!string.IsNullOrWhiteSpace(marker.Categories))
                    details.Add($"Categories: {marker.Categories}");

                if (!string.IsNullOrWhiteSpace(marker.Conditions))
                    details.Add($"Conditions: {marker.Conditions}");

                if (!string.IsNullOrWhiteSpace(marker.Position))
                    details.Add($"Position: {marker.Position}");

                SelectedMarkerDetailsText.Text = string.Join(Environment.NewLine, details);
            }
            catch (Exception ex)
            {
                SelectedMarkerNameText.Text = "Marker read error";
                SelectedMarkerDetailsText.Text = ex.Message;
            }
        }

        private string BuildMapHtml(string svg)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
html, body {{
    margin: 0;
    padding: 0;
    width: 100%;
    height: 100%;
    overflow: hidden;
    background: #252526;
}}

#stage {{
    width: 100vw;
    height: 100vh;
    overflow: hidden;
    position: relative;
    background: #252526;
    cursor: grab;
}}

#content {{
    position: absolute;
    left: 0;
    top: 0;
    transform-origin: 0 0;
}}

#content svg {{
    position: absolute;
    left: 0;
    top: 0;
}}

#playerMarker {{
    position: absolute;
    width: 0;
    height: 0;
    z-index: 9999;
    transform-origin: 0 0;
    display: none;
    pointer-events: none;
}}

#playerMarkerInner {{
    width: 0;
    height: 0;
    border-left: 13px solid transparent;
    border-right: 13px solid transparent;
    border-bottom: 30px solid red;
    filter: drop-shadow(0 0 2px white);
    transform: translate(-50%, -50%);
}}

.mapMarker {{
    position: absolute;
    z-index: 9000;
    transform-origin: 0 0;
    cursor: pointer;
}}

.markerDot {{
    width: 13px;
    height: 13px;
    border-radius: 50%;
    border: 2px solid white;
    box-shadow: 0 0 4px black;
    transform: translate(-50%, -50%);
}}

.markerLabel {{
    position: absolute;
    left: 10px;
    top: -9px;
    white-space: nowrap;
    font-family: Arial, sans-serif;
    font-size: 12px;
    font-weight: bold;
    color: white;
    background: rgba(0,0,0,0.65);
    padding: 2px 5px;
    border-radius: 3px;
    text-shadow: 1px 1px 2px black;
}}

.extract-pmc .markerDot {{ background: #00c853; }}
.extract-scav .markerDot {{ background: #ffd600; }}
.extract-shared .markerDot {{ background: #40c4ff; }}

.transit .markerDot {{
    background: #b000ff;
    width: 16px;
    height: 16px;
    border-radius: 4px;
}}

.spawn-pmc .markerDot {{
    background: #2979ff;
    width: 10px;
    height: 10px;
}}

.spawn-scav .markerDot {{
    background: #ff9800;
    width: 10px;
    height: 10px;
}}

.spawn-boss .markerDot {{
    background: #ff1744;
    width: 22px;
    height: 22px;
    border-radius: 50%;
}}

.quest-objective .markerDot {{
    background: #e040fb;
    width: 16px;
    height: 16px;
    border-radius: 2px;
    transform: translate(-50%, -50%) rotate(45deg);
}}

.quest-objective .markerLabel {{
    color: #f3c4ff;
}}

.map-label .markerDot {{
    display: none;
}}

.map-label .markerLabel {{
    left: 0;
    top: 0;
    background: transparent;
    color: #d8d8d8;
    font-family: Arial, sans-serif;
    font-weight: bold;
    padding: 0;
    border-radius: 0;
    text-shadow:
        1px 1px 2px black,
        -1px -1px 2px black,
        0 0 4px black;
    transform-origin: center center;
}}
</style>
</head>

<body>
<div id='stage'>
    <div id='content'>
        {svg}
        <div id='markerLayer'></div>
        <div id='playerMarker'>
            <div id='playerMarkerInner'></div>
        </div>
    </div>
</div>

<script>
let stage = document.getElementById('stage');
let content = document.getElementById('content');
let playerMarker = document.getElementById('playerMarker');
let markerLayer = document.getElementById('markerLayer');
let svg = content.querySelector('svg');

let scale = 1;
let panX = 0;
let panY = 0;
let isPanning = false;
let lastX = 0;
let lastY = 0;
let playerMarkerData = null;

function getViewBox() {{
    let raw = svg.getAttribute('viewBox');
    if (!raw) return {{ x: 0, y: 0, w: 1000, h: 1000 }};

    let vb = raw.trim().split(/\s+/).map(Number);
    return {{ x: vb[0], y: vb[1], w: vb[2], h: vb[3] }};
}}

function initialize() {{
    let vb = getViewBox();

    svg.setAttribute('width', vb.w);
    svg.setAttribute('height', vb.h);

    content.style.width = vb.w + 'px';
    content.style.height = vb.h + 'px';

    resetView();
}}

function applyTransform() {{
    content.style.transform = `translate(${{panX}}px, ${{panY}}px) scale(${{scale}})`;
    updatePlayerMarkerVisual();
    updateMapMarkerVisuals();
}}

function resetView() {{
    let vb = getViewBox();

    let sx = stage.clientWidth / vb.w;
    let sy = stage.clientHeight / vb.h;

    scale = Math.min(sx, sy) * 0.95;

    panX = (stage.clientWidth - vb.w * scale) / 2;
    panY = (stage.clientHeight - vb.h * scale) / 2;

    applyTransform();
}}

function setPlayerMarkerNormalized(nx, ny, directionDegrees) {{
    let vb = getViewBox();

    playerMarkerData = {{
        x: nx * vb.w,
        y: ny * vb.h,
        direction: directionDegrees
    }};

    updatePlayerMarkerVisual();
}}

function updatePlayerMarkerVisual() {{
    if (!playerMarkerData) return;

    let inverseScale = 1 / scale;

    playerMarker.style.left = playerMarkerData.x + 'px';
    playerMarker.style.top = playerMarkerData.y + 'px';
    playerMarker.style.display = 'block';

    playerMarker.style.transform =
        `rotate(${{playerMarkerData.direction + 180}}deg) scale(${{inverseScale}})`;
}}

function clearMapMarkers() {{
    markerLayer.innerHTML = '';
}}

function addMapMarkers(markers) {{
    clearMapMarkers();

    let vb = getViewBox();

    for (let m of markers) {{
        let marker = document.createElement('div');

        marker.className = 'mapMarker ' + m.cssClass;
        marker.dataset.markerType = m.markerType;
        marker.dataset.faction = m.faction || '';

        marker.title = m.tooltip;

        let x = m.normalizedX * vb.w;
        let y = m.normalizedY * vb.h;

        marker.style.left = x + 'px';
        marker.style.top = y + 'px';

        let dot = document.createElement('div');
        dot.className = 'markerDot';

        marker.appendChild(dot);

        if (
            m.markerType !== 'spawn-pmc' &&
            m.markerType !== 'spawn-scav' &&
            m.markerType !== 'spawn-boss'
        ) {{
            let label = document.createElement('div');
            label.className = 'markerLabel';
            label.textContent = m.name;

            if (m.markerType === 'label') {{
                label.style.fontSize = (m.labelSize || 14) + 'px';
                label.style.transform = 'translate(-50%, -50%) rotate(' + (m.labelRotation || 0) + 'deg)';
            }}

            marker.appendChild(label);
        }}

        marker.addEventListener('click', function(e) {{
            e.stopPropagation();

            if (window.chrome && window.chrome.webview) {{
                window.chrome.webview.postMessage({{
                    messageType: 'markerClicked',
                    name: m.name,
                    markerType: m.markerType,
                    faction: m.faction || '',
                    zoneName: m.zoneName || '',
                    categories: m.categories || '',
                    conditions: m.conditions || '',
                    position: m.position || ''
                }});
            }}
        }});

        markerLayer.appendChild(marker);
    }}

    updateMapMarkerVisuals();
}}

function updateMapMarkerVisuals() {{
    let inverseScale = 1 / scale;

    document.querySelectorAll('.mapMarker').forEach(function(marker) {{
        marker.style.transform = `scale(${{inverseScale}})`;
    }});
}}

function setExtractFactionVisibility(faction, visible) {{
    document.querySelectorAll('.mapMarker[data-marker-type=""extract""][data-faction=""' + faction + '""]').forEach(function(marker) {{
        marker.style.display = visible ? 'block' : 'none';
    }});
}}

function setTransitVisibility(visible) {{
    document.querySelectorAll('.mapMarker[data-marker-type=""transit""]').forEach(function(marker) {{
        marker.style.display = visible ? 'block' : 'none';
    }});
}}

function setSpawnVisibility(spawnType, visible) {{
    document.querySelectorAll('.mapMarker[data-marker-type=""' + spawnType + '""]').forEach(function(marker) {{
        marker.style.display = visible ? 'block' : 'none';
    }});
}}

function setLabelVisibility(visible) {{
    document.querySelectorAll('.mapMarker[data-marker-type=""label""]').forEach(function(marker) {{
        marker.style.display = visible ? 'block' : 'none';
    }});
}}

function setQuestVisibility(visible) {{
    document.querySelectorAll('.mapMarker[data-marker-type=""quest""]').forEach(function(marker) {{
        marker.style.display = visible ? 'block' : 'none';
    }});
}}

function setLayerVisibility(id, visible) {{
    let el = document.getElementById(id);
    if (!el) return;
    el.style.display = visible ? '' : 'none';
}}

stage.addEventListener('wheel', function(e) {{
    e.preventDefault();

    let oldScale = scale;
    let zoomFactor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
    let newScale = Math.max(0.2, Math.min(oldScale * zoomFactor, 12));

    let rect = stage.getBoundingClientRect();
    let mx = e.clientX - rect.left;
    let my = e.clientY - rect.top;

    let ratio = newScale / oldScale;

    panX = mx - (mx - panX) * ratio;
    panY = my - (my - panY) * ratio;

    scale = newScale;
    applyTransform();
}});

stage.addEventListener('mousedown', function(e) {{
    isPanning = true;
    lastX = e.clientX;
    lastY = e.clientY;
    stage.style.cursor = 'grabbing';
}});

window.addEventListener('mouseup', function() {{
    isPanning = false;
    stage.style.cursor = 'grab';
}});

window.addEventListener('mousemove', function(e) {{
    if (!isPanning) return;

    panX += e.clientX - lastX;
    panY += e.clientY - lastY;

    lastX = e.clientX;
    lastY = e.clientY;

    applyTransform();
}});

window.addEventListener('resize', resetView);

initialize();
</script>
</body>
</html>";
        }

        private void LoadLayersPanel(string mapPath)
        {
            LayersPanel.Children.Clear();

            var layerIds = GetSvgLayerIds(mapPath);

            foreach (string layerId in layerIds)
            {
                var checkBox = new CheckBox
                {
                    Content = layerId,
                    IsChecked = true,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                checkBox.Checked += async (_, _) => await SetSvgLayerVisibility(layerId, true);
                checkBox.Unchecked += async (_, _) => await SetSvgLayerVisibility(layerId, false);

                LayersPanel.Children.Add(checkBox);
            }
        }

        private List<string> GetSvgLayerIds(string mapPath)
        {
            try
            {
                XDocument doc = XDocument.Load(mapPath);

                return doc
                    .Descendants()
                    .Where(e => e.Name.LocalName == "g")
                    .Select(e => e.Attribute("id")?.Value)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private async System.Threading.Tasks.Task SetSvgLayerVisibility(string layerId, bool visible)
        {
            string idJson = JsonSerializer.Serialize(layerId);
            string visibleJson = visible ? "true" : "false";

            if (_webViewReady)
            {
                await MapWebView.ExecuteScriptAsync(
                    $"setLayerVisibility({idJson}, {visibleJson});");
            }

            if (_overlayWindow != null)
                await _overlayWindow.SetLayerVisibilityAsync(layerId, visible);
        }

        private void ShowAllLayers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in LayersPanel.Children)
            {
                if (child is CheckBox checkBox)
                    checkBox.IsChecked = true;
            }
        }

        private void HideAllLayers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in LayersPanel.Children)
            {
                if (child is CheckBox checkBox)
                    checkBox.IsChecked = false;
            }
        }

        private async void MarkerFilter_Changed(object sender, RoutedEventArgs e)
        {
            await ApplyMarkerVisibility();
        }

        private async System.Threading.Tasks.Task ApplyMarkerVisibility()
        {
            bool showPmcExtracts = ShowPmcExtractsCheckBox?.IsChecked == true;
            bool showScavExtracts = ShowScavExtractsCheckBox?.IsChecked == true;
            bool showSharedExtracts = ShowSharedExtractsCheckBox?.IsChecked == true;
            bool showTransits = ShowTransitsCheckBox?.IsChecked == true;
            bool showPmcSpawns = ShowPmcSpawnsCheckBox?.IsChecked == true;
            bool showScavSpawns = ShowScavSpawnsCheckBox?.IsChecked == true;
            bool showBossSpawns = ShowBossSpawnsCheckBox?.IsChecked == true;
            bool showLabels = ShowLabelsCheckBox?.IsChecked == true;
            bool showQuestMarkers = ShowQuestMarkersCheckBox?.IsChecked == true;

            if (_webViewReady)
            {
                await MapWebView.ExecuteScriptAsync($"setExtractFactionVisibility('pmc', {(showPmcExtracts ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setExtractFactionVisibility('scav', {(showScavExtracts ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setExtractFactionVisibility('shared', {(showSharedExtracts ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setTransitVisibility({(showTransits ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setSpawnVisibility('spawn-pmc', {(showPmcSpawns ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setSpawnVisibility('spawn-scav', {(showScavSpawns ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setSpawnVisibility('spawn-boss', {(showBossSpawns ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setLabelVisibility({(showLabels ? "true" : "false")});");
                await MapWebView.ExecuteScriptAsync($"setQuestVisibility({(showQuestMarkers ? "true" : "false")});");
            }

            if (_overlayWindow != null)
            {
                await _overlayWindow.ApplyMarkerVisibilityAsync(
                    showPmcExtracts,
                    showScavExtracts,
                    showSharedExtracts,
                    showTransits,
                    showPmcSpawns,
                    showScavSpawns,
                    showBossSpawns,
                    showLabels,
                    showQuestMarkers);
            }
        }

        private async void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (!_webViewReady)
                return;

            await MapWebView.ExecuteScriptAsync("resetView();");
            RedrawLastMarker();
            DrawMapMarkersForCurrentMap();
        }

        private void DrawMapMarkersForCurrentMap()
        {
            if (!_webViewReady || _currentMapConfig == null || string.IsNullOrWhiteSpace(_currentMapDisplayName))
                return;

            string normalizedName = NormalizeMapName(_currentMapDisplayName);
            var markers = new List<WebMapMarker>();

            if (_extractsByMapName.TryGetValue(normalizedName, out var extracts))
            {
                foreach (var extract in extracts.Where(e => e.Position != null))
                {
                    var converted = ConvertGameToNormalizedMap(extract.Position!.X, extract.Position.Z);
                    string faction = NormalizeFaction(extract.Faction);
                    string conditions = GetExtractConditions(extract);

                    markers.Add(new WebMapMarker
                    {
                        Name = extract.Name,
                        MarkerType = "extract",
                        Faction = faction,
                        CssClass = "extract-" + faction,
                        Tooltip = $"{extract.Name} ({faction.ToUpperInvariant()} Extract)\nConditions: {conditions}",
                        ZoneName = "",
                        Categories = "",
                        Conditions = conditions,
                        Position = $"X={extract.Position.X:0.##}, Y={extract.Position.Y:0.##}, Z={extract.Position.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY
                    });
                }
            }

            if (_transitsByMapName.TryGetValue(normalizedName, out var transits))
            {
                foreach (var transit in transits.Where(t => t.Position != null))
                {
                    var converted = ConvertGameToNormalizedMap(transit.Position!.X, transit.Position.Z);

                    string conditions = string.IsNullOrWhiteSpace(transit.Conditions)
                        ? "None"
                        : transit.Conditions!;

                    markers.Add(new WebMapMarker
                    {
                        Name = transit.Description,
                        MarkerType = "transit",
                        Faction = "",
                        CssClass = "transit",
                        Tooltip = $"{transit.Description}\nConditions: {conditions}",
                        Conditions = conditions,
                        Position = $"X={transit.Position.X:0.##}, Y={transit.Position.Y:0.##}, Z={transit.Position.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY
                    });
                }
            }

            bool mapHasBoss = CurrentMapHasRealBoss();

            if (_spawnsByMapName.TryGetValue(normalizedName, out var spawns))
            {
                foreach (var spawn in spawns.Where(s => s.Position != null))
                {
                    var converted = ConvertGameToNormalizedMap(spawn.Position!.X, spawn.Position.Z);

                    string sides = spawn.Sides == null || spawn.Sides.Count == 0
                        ? ""
                        : string.Join(",", spawn.Sides).ToLowerInvariant();

                    string categories = spawn.Categories == null || spawn.Categories.Count == 0
                        ? ""
                        : string.Join(",", spawn.Categories).ToLowerInvariant();

                    bool isBoss = mapHasBoss && categories.Contains("boss");
                    bool isPmc = sides.Contains("pmc") || sides.Contains("all");
                    bool isScav = sides.Contains("scav");

                    if (isBoss)
                        markers.Add(BuildSpawnMarker(spawn, converted, "spawn-boss", "Boss Spawn Zone", "spawn-boss"));
                    else if (isPmc && !isScav)
                        markers.Add(BuildSpawnMarker(spawn, converted, "spawn-pmc", "PMC Spawn", "spawn-pmc"));
                    else if (isScav)
                        markers.Add(BuildSpawnMarker(spawn, converted, "spawn-scav", "Scav Spawn", "spawn-scav"));
                }
            }


            if (_questMarkersByMapName.TryGetValue(normalizedName, out var questMarkers))
            {
                foreach (var questMarker in questMarkers)
                {
                    if (questMarker.X == null || questMarker.Z == null)
                        continue;

                    var converted = ConvertGameToNormalizedMap(questMarker.X.Value, questMarker.Z.Value);
                    string description = string.IsNullOrWhiteSpace(questMarker.Description)
                        ? "Quest objective"
                        : questMarker.Description;

                    markers.Add(new WebMapMarker
                    {
                        Name = questMarker.Quest,
                        MarkerType = "quest",
                        Faction = "",
                        CssClass = "quest-objective",
                        Tooltip = $"{questMarker.Quest}\n{description}",
                        ZoneName = questMarker.Quest,
                        Categories = "Quest Objective",
                        Conditions = description,
                        Position = $"X={questMarker.X.Value:0.##}, Y={(questMarker.Y ?? 0):0.##}, Z={questMarker.Z.Value:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY
                    });
                }
            }


            if (_labelsByMapName.TryGetValue(normalizedName, out var labels))
            {
                foreach (var label in labels)
                {
                    var converted = ConvertGameToNormalizedMap(label.X, label.Z);

                    markers.Add(new WebMapMarker
                    {
                        Name = label.Text,
                        MarkerType = "label",
                        Faction = "",
                        CssClass = "map-label",
                        Tooltip = label.Text,
                        ZoneName = "",
                        Categories = "",
                        Conditions = "",
                        Position = $"X={label.X:0.##}, Z={label.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY,
                        LabelRotation = label.Rotation,
                        LabelSize = GetLabelFontSize(label.Size)
                    });
                }
            }

            string json = JsonSerializer.Serialize(markers);
            _lastMapMarkersJson = json;

            _ = MapWebView.ExecuteScriptAsync($"addMapMarkers({json});");
            _ = ApplyMarkerVisibility();

            if (_overlayWindow != null)
                _ = _overlayWindow.SetMapMarkersAsync(json);

            ParserStatusText.Text = $"Loaded {markers.Count} map markers";
            ParserStatusText.Foreground = Brushes.LawnGreen;
        }

        private double GetLabelFontSize(double size)
        {
            if (size <= 0)
                return 14;

            return Math.Max(11, Math.Min(26, size / 5.0));
        }

        private string GetExtractConditions(ExtractInfo extract)
        {
            var conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(extract.Conditions))
                conditions.Add(extract.Conditions);

            if (extract.Requirements != null && extract.Requirements.Count > 0)
                conditions.AddRange(extract.Requirements.Where(r => !string.IsNullOrWhiteSpace(r)));

            if (extract.Switches != null && extract.Switches.Count > 0)
            {
                var switchNames = extract.Switches
                    .Select(s => string.IsNullOrWhiteSpace(s.Name) ? s.Id : s.Name)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (switchNames.Count > 0)
                    conditions.Add("Requires switch: " + string.Join(", ", switchNames));
            }

            string? transferItemName = GetNameFromJsonElement(extract.TransferItem);

            if (!string.IsNullOrWhiteSpace(transferItemName))
                conditions.Add("Requires item: " + transferItemName);

            return conditions.Count == 0
                ? "None"
                : string.Join("; ", conditions.Distinct());
        }

        private string? GetNameFromJsonElement(JsonElement? element)
        {
            if (element == null)
                return null;

            JsonElement value = element.Value;

            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("name", out JsonElement nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    return nameElement.GetString();
                }

                foreach (var property in value.EnumerateObject())
                {
                    string? found = GetNameFromJsonElement(property.Value);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    string? found = GetNameFromJsonElement(item);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }

            return null;
        }

        private bool CurrentMapHasRealBoss()
        {
            if (_currentMapConfig?.Enemies == null || _currentMapConfig.Enemies.Count == 0)
                return false;

            string[] nonBossEnemies =
            {
                "Scavs",
                "Cultists",
                "Raiders",
                "Rogues"
            };

            return _currentMapConfig.Enemies.Any(enemy =>
                !nonBossEnemies.Any(nonBoss =>
                    string.Equals(enemy, nonBoss, StringComparison.OrdinalIgnoreCase)));
        }

        private WebMapMarker BuildSpawnMarker(
            SpawnInfo spawn,
            (double normalizedX, double normalizedY) converted,
            string markerType,
            string displayType,
            string cssClass)
        {
            string sides = spawn.Sides == null ? "" : string.Join(", ", spawn.Sides);
            string categories = spawn.Categories == null ? "" : string.Join(", ", spawn.Categories);

            return new WebMapMarker
            {
                Name = displayType,
                MarkerType = markerType,
                Faction = sides,
                CssClass = cssClass,
                Tooltip = $"{displayType}\nZone: {spawn.ZoneName}\nSides: {sides}\nCategories: {categories}",
                ZoneName = spawn.ZoneName,
                Categories = categories,
                Conditions = "",
                Position = $"X={spawn.Position!.X:0.##}, Y={spawn.Position.Y:0.##}, Z={spawn.Position.Z:0.##}",
                NormalizedX = converted.normalizedX,
                NormalizedY = converted.normalizedY
            };
        }

        private string NormalizeFaction(string faction)
        {
            string f = (faction ?? "").Trim().ToLowerInvariant();

            if (f == "pmc")
                return "pmc";

            if (f == "scav")
                return "scav";

            return "shared";
        }

        private string NormalizeMapName(string name)
        {
            return new string(
                (name ?? "")
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
        }

        private (double normalizedX, double normalizedY) ConvertGameToNormalizedMap(double gameX, double gameZ)
        {
            if (_currentMapConfig?.Svg?.Bounds == null || _currentMapConfig.Svg.Bounds.Length < 2)
                return (0.5, 0.5);

            double x1 = _currentMapConfig.Svg.Bounds[0][0];
            double z1 = _currentMapConfig.Svg.Bounds[0][1];
            double x2 = _currentMapConfig.Svg.Bounds[1][0];
            double z2 = _currentMapConfig.Svg.Bounds[1][1];

            double minX = Math.Min(x1, x2);
            double maxX = Math.Max(x1, x2);
            double minZ = Math.Min(z1, z2);
            double maxZ = Math.Max(z1, z2);

            double normalizedX = (gameX - minX) / (maxX - minX);
            double normalizedY = (gameZ - minZ) / (maxZ - minZ);

            switch (_currentMapConfig.Svg.CoordinateRotation)
            {
                case 90:
                    double oldX = normalizedX;
                    double oldY = normalizedY;
                    normalizedX = 1 - oldY;
                    normalizedY = 1 - oldX;
                    break;

                case 180:
                    normalizedX = 1 - normalizedX;
                    break;

                case 270:
                    (normalizedX, normalizedY) = (1 - normalizedY, normalizedX);
                    break;
            }

            return (normalizedX, normalizedY);
        }

        private void StartScreenshotMonitoring()
        {
            if (!Directory.Exists(_screenshotFolder))
            {
                ParserStatusText.Text = "Screenshot folder not found";
                ParserStatusText.Foreground = Brushes.OrangeRed;
                return;
            }

            _screenshotWatcher = new FileSystemWatcher(_screenshotFolder)
            {
                Filter = "*.png",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _screenshotWatcher.Created += ScreenshotWatcher_Changed;
            _screenshotWatcher.Renamed += ScreenshotWatcher_Changed;

            ParserStatusText.Text = "Monitoring screenshots";
            ParserStatusText.Foreground = Brushes.LawnGreen;
        }

        private void ScreenshotWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(500);

                var file = new FileInfo(e.FullPath);

                if (!file.Exists)
                    return;

                if (file.CreationTime <= _lastProcessedTime)
                    return;

                _lastProcessedTime = file.CreationTime;

                ParseScreenshotFilename(file.Name);
            });
        }

        private void ReadLatestScreenshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(_screenshotFolder))
                {
                    MessageBox.Show($"Screenshot folder not found:\n{_screenshotFolder}");
                    return;
                }

                var newestFile = new DirectoryInfo(_screenshotFolder)
                    .GetFiles("*.png")
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                if (newestFile == null)
                {
                    MessageBox.Show("No screenshots found.");
                    return;
                }

                _lastProcessedTime = newestFile.CreationTime;
                ParseScreenshotFilename(newestFile.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ParseScreenshotFilename(string filename)
        {
            LastScreenshotText.Text = filename;

            var regex = new Regex(
                @"_(-?\d+\.\d+),\s*(-?\d+\.\d+),\s*(-?\d+\.\d+)_" +
                @"(-?\d+\.\d+),\s*(-?\d+\.\d+),\s*(-?\d+\.\d+),\s*(-?\d+\.\d+)_" +
                @"(-?\d+\.\d+)");

            Match match = regex.Match(filename);

            if (!match.Success)
            {
                ParserStatusText.Text = "Parse failed";
                ParserStatusText.Foreground = Brushes.OrangeRed;
                StatusText.Text = "Failed to parse screenshot filename.";
                return;
            }

            double gameX = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double gameY = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            double gameZ = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            double qx = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            double qy = double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
            double qz = double.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture);
            double qw = double.Parse(match.Groups[7].Value, CultureInfo.InvariantCulture);

            double direction = GetYawFromQuaternion(qx, qy, qz, qw);

            _lastGameX = gameX;
            _lastGameY = gameY;
            _lastGameZ = gameZ;
            _lastDirection = direction;

            GameXText.Text = gameX.ToString("0.00", CultureInfo.InvariantCulture);
            GameYText.Text = gameY.ToString("0.00", CultureInfo.InvariantCulture);
            GameZText.Text = gameZ.ToString("0.00", CultureInfo.InvariantCulture);
            DirectionText.Text = direction.ToString("0.00", CultureInfo.InvariantCulture) + "°";

            ParserStatusText.Text = $"Updated: {DateTime.Now:T}";
            ParserStatusText.Foreground = Brushes.LawnGreen;

            StatusText.Text = $"Parsed: X={gameX:0.00}, Y={gameY:0.00}, Z={gameZ:0.00}, Direction={direction:0.00}°";

            DrawPlayerMarkerFromGameCoordinates(gameX, gameZ, direction);
            // Disabled for now because it hides too many SVG layers on maps like Factory.
            // AutoSwitchFloorLayers(gameY);
        }

        private void AutoSwitchFloorLayers(double gameY)
        {
            string mapName = NormalizeMapName(_currentMapDisplayName ?? CurrentMapText.Text);
            var targetLayer = GetFloorLayerForMapAndY(mapName, gameY);

            if (string.IsNullOrWhiteSpace(targetLayer))
                return;

            foreach (var child in LayersPanel.Children)
            {
                if (child is not CheckBox checkBox)
                    continue;

                string layerName = checkBox.Content?.ToString() ?? "";

                if (IsFloorLayer(layerName))
                    checkBox.IsChecked = string.Equals(layerName, targetLayer, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string? GetFloorLayerForMapAndY(string normalizedMapName, double gameY)
        {
            if (normalizedMapName == "factory" || normalizedMapName == "nightfactory")
            {
                if (gameY < -1.0) return "Basement";
                if (gameY < 3.5) return "GroundFloor";
                if (gameY < 6.5) return "SecondFloor";
                return "ThirdFloor";
            }

            if (normalizedMapName == "groundzero" || normalizedMapName == "groundzero21")
            {
                if (gameY < 18) return "Underground_Level";
                if (gameY < 28) return "Ground_Level";
                if (gameY < 36) return "First_Floor";
                if (gameY < 44) return "Second_Floor";
                return "Third_Floor";
            }

            if (normalizedMapName == "thelab" || normalizedMapName == "lab")
            {
                if (gameY < -1) return "Technical_Level";
                if (gameY < 4) return "First_Level";
                return "Second_Level";
            }

            if (normalizedMapName == "interchange")
            {
                if (gameY < 22) return "Ground_Level";
                if (gameY < 30) return "First_Floor";
                return "Second_Floor";
            }

            return null;
        }

        private bool IsFloorLayer(string layerName)
        {
            string lower = layerName.ToLowerInvariant();

            return lower.Contains("floor") ||
                   lower.Contains("level") ||
                   lower.Contains("basement") ||
                   lower.Contains("bunker") ||
                   lower.Contains("groundfloor") ||
                   lower.Contains("ground_level") ||
                   lower.Contains("technical_level") ||
                   lower.Contains("first_level") ||
                   lower.Contains("second_level");
        }

        private double GetYawFromQuaternion(double x, double y, double z, double w)
        {
            double sinyCosp = 2.0 * (w * y + x * z);
            double cosyCosp = 1.0 - 2.0 * (y * y + z * z);

            double yawRadians = Math.Atan2(sinyCosp, cosyCosp);
            return yawRadians * (180.0 / Math.PI);
        }

        private void DrawPlayerMarkerFromGameCoordinates(double gameX, double gameZ, double directionDegrees)
        {
            var converted = ConvertGameToNormalizedMap(gameX, gameZ);

            SetPlayerMarkerInWebView(
                converted.normalizedX,
                converted.normalizedY,
                directionDegrees);
        }

        private async void SetPlayerMarkerInWebView(double normalizedX, double normalizedY, double directionDegrees)
        {
            _lastPlayerMarker = (normalizedX, normalizedY, directionDegrees);

            if (_webViewReady)
            {
                await MapWebView.ExecuteScriptAsync(
                    $"setPlayerMarkerNormalized({normalizedX.ToString(CultureInfo.InvariantCulture)}, " +
                    $"{normalizedY.ToString(CultureInfo.InvariantCulture)}, " +
                    $"{directionDegrees.ToString(CultureInfo.InvariantCulture)});");
            }

            if (_overlayWindow != null)
                await _overlayWindow.SetPlayerMarkerAsync(normalizedX, normalizedY, directionDegrees);
        }

        private void RedrawLastMarker()
        {
            if (_lastGameX == null || _lastGameZ == null || _lastDirection == null)
                return;

            DrawPlayerMarkerFromGameCoordinates(
                _lastGameX.Value,
                _lastGameZ.Value,
                _lastDirection.Value);
        }

        private void OpenScreenshotFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(_screenshotFolder))
            {
                MessageBox.Show($"Folder not found:\n{_screenshotFolder}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _screenshotFolder,
                UseShellExecute = true
            });
        }

        private async void OpenOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new OverlayWindow();
                _overlayWindow.Closed += (_, _) => _overlayWindow = null;
                _overlayWindow.Show();
            }
            else
            {
                _overlayWindow.Activate();
            }

            await SyncOverlayToCurrentMapAsync();
        }

        private async System.Threading.Tasks.Task SyncOverlayToCurrentMapAsync()
        {
            if (_overlayWindow == null)
                return;

            if (string.IsNullOrWhiteSpace(_currentMapHtml))
                return;

            await _overlayWindow.LoadMapHtmlAsync(_currentMapHtml);

            if (!string.IsNullOrWhiteSpace(_lastMapMarkersJson))
                await _overlayWindow.SetMapMarkersAsync(_lastMapMarkersJson);

            if (_lastPlayerMarker != null)
            {
                await _overlayWindow.SetPlayerMarkerAsync(
                    _lastPlayerMarker.Value.NormalizedX,
                    _lastPlayerMarker.Value.NormalizedY,
                    _lastPlayerMarker.Value.DirectionDegrees);
            }

            await SyncOverlayLayersAndFiltersAsync();
        }

        private async System.Threading.Tasks.Task SyncOverlayLayersAndFiltersAsync()
        {
            if (_overlayWindow == null)
                return;

            foreach (var child in LayersPanel.Children)
            {
                if (child is not CheckBox checkBox)
                    continue;

                string layerId = checkBox.Content?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(layerId))
                    await _overlayWindow.SetLayerVisibilityAsync(layerId, checkBox.IsChecked == true);
            }

            await _overlayWindow.ApplyMarkerVisibilityAsync(
                ShowPmcExtractsCheckBox.IsChecked == true,
                ShowScavExtractsCheckBox.IsChecked == true,
                ShowSharedExtractsCheckBox.IsChecked == true,
                ShowTransitsCheckBox.IsChecked == true,
                ShowPmcSpawnsCheckBox.IsChecked == true,
                ShowScavSpawnsCheckBox.IsChecked == true,
                ShowBossSpawnsCheckBox.IsChecked == true,
                ShowLabelsCheckBox.IsChecked == true,
                ShowQuestMarkersCheckBox.IsChecked == true);
        }

        protected override void OnClosed(EventArgs e)
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
            base.OnClosed(e);
        }
    }

    public class MapConfig
    {
        [JsonPropertyName("locale")]
        public MapLocale Locale { get; set; } = new();

        [JsonPropertyName("enemies")]
        public List<string> Enemies { get; set; } = new();

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

        [JsonPropertyName("bounds")]
        public double[][] Bounds { get; set; } = Array.Empty<double[]>();
    }

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
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("faction")]
        public string Faction { get; set; } = "";

        [JsonPropertyName("conditions")]
        public string? Conditions { get; set; }

        [JsonPropertyName("requirements")]
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

    public class QuestMarker
    {
        [JsonPropertyName("quest")]
        public string Quest { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

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

    public class UserAppSettings
    {
        public string ScreenshotFolder { get; set; } = "";
    }


    public class WebMapMarker
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("markerType")]
        public string MarkerType { get; set; } = "";

        [JsonPropertyName("faction")]
        public string Faction { get; set; } = "";

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
    }

    public class FlexibleDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.TryGetDouble(out double value) ? value : 0;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string? text = reader.GetString();

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    return value;
            }

            if (reader.TokenType == JsonTokenType.Null)
                return 0;

            return 0;
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

}