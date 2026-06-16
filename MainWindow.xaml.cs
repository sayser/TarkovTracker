using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TarkovTracker.Models;
using TarkovTracker.Services;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TarkovTracker
{
    public partial class MainWindow : Window
    {
        internal const string MapAssetHostName = "tarkovtracker.local";
        internal const string BaseLayerToggleTag = "__base_layer_toggle__";

        private readonly MapDataService _mapData = new();
        private readonly string _mapsFolder;
        private readonly string _userSettingsFile;
        private OverlayWindow? _overlayWindow;
        private string? _currentMapPath;
        private string? _currentMapHtml;
        private string? _lastMapMarkersJson;
        private (double NormalizedX, double NormalizedY, double DirectionDegrees)? _lastPlayerMarker;

        private string _screenshotFolder;
        private MapConfig? _currentMapConfig;
        private MapLevelsConfig? _currentMapLevelsConfig;
        private string? _currentMapDisplayName;

        private FileSystemWatcher? _screenshotWatcher;
        private DateTime _lastProcessedTime = DateTime.MinValue;

        private double? _lastGameX;
        private double? _lastGameZ;
        private double? _lastDirection;

        private bool _webViewReady = false;
        private bool _suppressMapLevelRefresh = false;
        private bool _webMessageHooked = false;
        private bool _mapWebViewHostMapped = false;

        public MainWindow()
        {
            InitializeComponent();

            _mapsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps");
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

            _mapData.LoadAll();
            if (!string.IsNullOrWhiteSpace(_mapData.LastStatusMessage))
                StatusText.Text = _mapData.LastStatusMessage;

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

        private void SetParserStatus(string message, bool isError = false)
        {
            ParserStatusText.Text = message.StartsWith("PARSER:", StringComparison.OrdinalIgnoreCase)
                ? message.ToUpperInvariant()
                : $"PARSER: {message.ToUpperInvariant()}";
            ParserStatusText.Foreground = isError
                ? (Brush)FindResource("TacticalTerminalRedBrush")
                : (Brush)FindResource("TacticalTerminalGreenBrush");
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

                StatusText.Text = $"FOLDER: {_screenshotFolder}";
                SetParserStatus("FOLDER UPDATED");
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

            var itemForeground = (Brush)FindResource("TacticalAmberBrightBrush");

            foreach (var svgFile in svgFiles)
            {
                MapComboBox.Items.Add(new ComboBoxItem
                {
                    Content = Path.GetFileNameWithoutExtension(svgFile).ToUpperInvariant(),
                    Tag = svgFile,
                    Foreground = itemForeground
                });
            }

            StatusText.Text =
                $"{svgFiles.Count} maps loaded. Extract maps: {_mapData.ExtractsByMapName.Count}. Transit maps: {_mapData.TransitsByMapName.Count}. Spawn maps: {_mapData.SpawnsByMapName.Count}. Boss spawn maps: {_mapData.BossSpawnMarkersByMapName.Count}. Label maps: {_mapData.LabelsByMapName.Count}. Quest maps: {_mapData.QuestMarkersByMapName.Count}. Hazard maps: {_mapData.HazardsByMapName.Count}. Switch maps: {_mapData.SwitchesByMapName.Count}.";
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

            CurrentMapText.Text = mapFileName.ToUpperInvariant();

            if (!_mapData.TryResolveMapConfig(mapFileName, out _currentMapConfig))
            {
                _currentMapConfig = null;
                StatusText.Text = $"CONFIG MISSING: {mapFileName.ToUpperInvariant()}";
                SetParserStatus("MAP CONFIG MISSING", isError: true);
                return;
            }

            _currentMapDisplayName =
                !string.IsNullOrWhiteSpace(_currentMapConfig?.Locale?.En)
                    ? _currentMapConfig.Locale.En
                    : mapFileName;

            await LoadSvgMapInWebView(mapPath);
            LoadLayersPanel(mapPath);
            await ApplyMapLevelStateAsync();

            DrawMapMarkersForCurrentMap();
            RedrawLastMarker();
            _ = SyncOverlayToCurrentMapAsync();

            StatusText.Text = _currentMapConfig == null
                ? $"Loaded map: {mapFileName}. No config found."
                : $"Loaded map: {mapFileName}. Rotation={_currentMapConfig.Svg.CoordinateRotation}.";
        }

        private async System.Threading.Tasks.Task EnsureMapWebViewHostMappingAsync()
        {
            await MapWebView.EnsureCoreWebView2Async();

            if (_mapWebViewHostMapped)
                return;

            MapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                MapAssetHostName,
                _mapsFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            _mapWebViewHostMapped = true;
        }

        private async System.Threading.Tasks.Task LoadSvgMapInWebView(string mapPath)
        {
            _webViewReady = false;

            string svg = PrepareSvgForWebView(File.ReadAllText(mapPath), mapPath);
            string html = MapHtmlBuilder.Build(svg, MapAssetHostName);
            _currentMapPath = mapPath;
            _currentMapHtml = html;

            await EnsureMapWebViewHostMappingAsync();

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

        private string PrepareSvgForWebView(string svg, string mapPath)
        {
            string mapDir = Path.GetDirectoryName(mapPath)!;

            return Regex.Replace(
                svg,
                @"(?<attr>(?:xlink:)?href)\s*=\s*""(?!(?:data:|https?:|file:))(?<path>[^""]+)""",
                match =>
                {
                    string relativePath = match.Groups["path"].Value;
                    string fullPath = Path.GetFullPath(Path.Combine(mapDir, relativePath));

                    if (!File.Exists(fullPath))
                        return match.Value;

                    string assetUrl = $"https://{MapAssetHostName}/{Path.GetFileName(fullPath)}";
                    return $"{match.Groups["attr"].Value}=\"{assetUrl}\"";
                },
                RegexOptions.IgnoreCase);
        }


        private void LoadLayersPanel(string mapPath)
        {
            LayersPanel.Children.Clear();

            string mapFileName = Path.GetFileNameWithoutExtension(mapPath);
            _currentMapLevelsConfig = _mapData.GetMapLevelsForMap(mapFileName);
            var levels = _currentMapLevelsConfig?.Levels ?? new List<MapLevelEntry>();
            bool hasLevels = levels.Any(level => !string.IsNullOrWhiteSpace(level.SvgLayer));

            LevelsHeaderGrid.Visibility = hasLevels ? Visibility.Visible : Visibility.Collapsed;

            if (!hasLevels)
                return;

            var levelChipStyle = (Style)FindResource("TacticalLevelChip");

            if (!string.IsNullOrWhiteSpace(_currentMapLevelsConfig?.DefaultSvgLayer))
            {
                var baseCheckBox = new CheckBox
                {
                    Content = "BASE",
                    Tag = BaseLayerToggleTag,
                    IsChecked = true,
                    Style = levelChipStyle,
                    FontWeight = FontWeights.Bold
                };

                baseCheckBox.Checked += async (_, _) =>
                {
                    if (!_suppressMapLevelRefresh)
                        await ApplyMapLevelStateAsync();
                };
                baseCheckBox.Unchecked += async (_, _) =>
                {
                    if (!_suppressMapLevelRefresh)
                        await ApplyMapLevelStateAsync();
                };

                LayersPanel.Children.Add(baseCheckBox);
            }

            foreach (var level in levels)
            {
                if (string.IsNullOrWhiteSpace(level.SvgLayer))
                    continue;

                string svgLayer = level.SvgLayer;
                var checkBox = new CheckBox
                {
                    Content = FormatLevelChipLabel(level.Name),
                    Tag = svgLayer,
                    IsChecked = level.DefaultVisible,
                    Style = levelChipStyle
                };

                checkBox.Checked += async (_, _) =>
                {
                    if (!_suppressMapLevelRefresh)
                        await ApplyMapLevelStateAsync();
                };
                checkBox.Unchecked += async (_, _) =>
                {
                    if (!_suppressMapLevelRefresh)
                        await ApplyMapLevelStateAsync();
                };

                LayersPanel.Children.Add(checkBox);
            }
        }

        private static string FormatLevelChipLabel(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "LEVEL";

            string normalized = name.Replace('_', ' ').Trim().ToUpperInvariant();
            return normalized.Length <= 14 ? normalized : normalized[..14];
        }

        private static bool IsBaseLayerCheckbox(CheckBox checkBox) =>
            string.Equals(checkBox.Tag as string, BaseLayerToggleTag, StringComparison.Ordinal);

        private MapLevelStatePayload BuildMapLevelState()
        {
            var config = _currentMapLevelsConfig!;
            var activeLevelIds = new List<string>();
            bool showBaseLayer = true;

            foreach (var child in LayersPanel.Children)
            {
                if (child is not CheckBox checkBox)
                    continue;

                if (IsBaseLayerCheckbox(checkBox))
                {
                    showBaseLayer = checkBox.IsChecked == true;
                    continue;
                }

                if (checkBox.IsChecked == true && checkBox.Tag is string svgLayer && !string.IsNullOrWhiteSpace(svgLayer))
                    activeLevelIds.Add(svgLayer);
            }

            bool dimBase = showBaseLayer && activeLevelIds.Any(id =>
                config.Levels.Any(level =>
                    string.Equals(level.SvgLayer, id, StringComparison.OrdinalIgnoreCase) && !level.DefaultVisible));

            return new MapLevelStatePayload
            {
                DefaultLayerId = config.DefaultSvgLayer,
                OverlayLayerIds = config.Levels
                    .Select(level => level.SvgLayer)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToList(),
                ShowBaseLayer = showBaseLayer,
                DimBase = dimBase,
                ActiveLevelIds = activeLevelIds
            };
        }

        private async System.Threading.Tasks.Task ApplyMapLevelStateAsync()
        {
            if (!_webViewReady || _currentMapLevelsConfig == null)
                return;

            string stateJson = JsonSerializer.Serialize(BuildMapLevelState());

            await MapWebView.ExecuteScriptAsync($"applyMapLevelState({stateJson});");

            if (_overlayWindow != null)
                await _overlayWindow.ApplyMapLevelStateAsync(stateJson);
        }

        private MarkerFilterState BuildMarkerFilterState()
        {
            return new MarkerFilterState
            {
                PmcExtracts = ShowPmcExtractsCheckBox?.IsChecked == true,
                ScavExtracts = ShowScavExtractsCheckBox?.IsChecked == true,
                SharedExtracts = ShowSharedExtractsCheckBox?.IsChecked == true,
                Transits = ShowTransitsCheckBox?.IsChecked == true,
                PmcSpawns = ShowPmcSpawnsCheckBox?.IsChecked == true,
                ScavSpawns = ShowScavSpawnsCheckBox?.IsChecked == true,
                BossSpawns = ShowBossSpawnsCheckBox?.IsChecked == true,
                Labels = ShowLabelsCheckBox?.IsChecked == true,
                QuestItems = ShowQuestItemsCheckBox?.IsChecked == true,
                QuestObjectives = ShowQuestObjectivesCheckBox?.IsChecked == true,
                Hazards = ShowHazardsCheckBox?.IsChecked == true,
                Switches = ShowSwitchesCheckBox?.IsChecked == true
            };
        }

        private void ShowAllLayers_Click(object sender, RoutedEventArgs e)
        {
            _suppressMapLevelRefresh = true;

            foreach (var child in LayersPanel.Children)
            {
                if (child is CheckBox checkBox && !IsBaseLayerCheckbox(checkBox))
                    checkBox.IsChecked = true;
            }

            _suppressMapLevelRefresh = false;
            _ = ApplyMapLevelStateAsync();
        }

        private void HideAllLayers_Click(object sender, RoutedEventArgs e)
        {
            _suppressMapLevelRefresh = true;

            foreach (var child in LayersPanel.Children)
            {
                if (child is CheckBox checkBox && !IsBaseLayerCheckbox(checkBox))
                    checkBox.IsChecked = false;
            }

            _suppressMapLevelRefresh = false;
            _ = ApplyMapLevelStateAsync();
        }

        private bool _suppressMarkerFilterRefresh = false;

        private void ShowAllMarkers_Click(object sender, RoutedEventArgs e)
        {
            _suppressMarkerFilterRefresh = true;

            ShowLabelsCheckBox.IsChecked = true;
            ShowQuestItemsCheckBox.IsChecked = true;
            ShowQuestObjectivesCheckBox.IsChecked = true;
            ShowPmcExtractsCheckBox.IsChecked = true;
            ShowScavExtractsCheckBox.IsChecked = true;
            ShowSharedExtractsCheckBox.IsChecked = true;
            ShowTransitsCheckBox.IsChecked = true;
            ShowHazardsCheckBox.IsChecked = true;
            ShowSwitchesCheckBox.IsChecked = true;
            ShowPmcSpawnsCheckBox.IsChecked = true;
            ShowScavSpawnsCheckBox.IsChecked = true;
            ShowBossSpawnsCheckBox.IsChecked = true;

            _suppressMarkerFilterRefresh = false;
            _ = ApplyMarkerVisibility();
        }

        private async void MarkerFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressMarkerFilterRefresh)
                return;

            await ApplyMarkerVisibility();
        }

        private async System.Threading.Tasks.Task ApplyMarkerVisibility()
        {
            string filtersJson = JsonSerializer.Serialize(BuildMarkerFilterState());

            if (_webViewReady)
                await MapWebView.ExecuteScriptAsync($"applyMarkerFilters({filtersJson});");

            if (_overlayWindow != null)
                await _overlayWindow.ApplyMarkerFiltersAsync(filtersJson);
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

            string normalizedName = MapDataService.NormalizeMapName(_currentMapDisplayName);
            var markers = new List<WebMapMarker>();

            if (_mapData.ExtractsByMapName.TryGetValue(normalizedName, out var extracts))
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

            if (_mapData.TransitsByMapName.TryGetValue(normalizedName, out var transits))
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

            if (_mapData.SpawnsByMapName.TryGetValue(normalizedName, out var spawns))
            {
                foreach (var spawn in spawns.Where(s => s.Position != null))
                {
                    string categories = spawn.Categories == null || spawn.Categories.Count == 0
                        ? ""
                        : string.Join(",", spawn.Categories).ToLowerInvariant();

                    if (categories.Contains("boss"))
                        continue;

                    var converted = ConvertGameToNormalizedMap(spawn.Position!.X, spawn.Position.Z);

                    string sides = spawn.Sides == null || spawn.Sides.Count == 0
                        ? ""
                        : string.Join(",", spawn.Sides).ToLowerInvariant();

                    bool isPmc = sides.Contains("pmc") || sides.Contains("all");
                    bool isScav = sides.Contains("scav");

                    if (isPmc && !isScav)
                        markers.Add(BuildSpawnMarker(spawn, converted, "spawn-pmc", "PMC Spawn", "spawn-pmc"));
                    else if (isScav)
                        markers.Add(BuildSpawnMarker(spawn, converted, "spawn-scav", "Scav Spawn", "spawn-scav"));
                }
            }

            if (_mapData.BossSpawnMarkersByMapName.TryGetValue(normalizedName, out var bossSpawns))
            {
                var bossGroups = bossSpawns
                    .Where(b => b.X != null && b.Z != null)
                    .GroupBy(b => $"{b.ZoneName}|{Math.Round(b.X!.Value, 1)}|{Math.Round(b.Z!.Value, 1)}");

                foreach (var group in bossGroups)
                {
                    var primary = group.First();
                    var converted = ConvertGameToNormalizedMap(primary.X!.Value, primary.Z!.Value);
                    markers.Add(BuildBossSpawnMarker(group.ToList(), converted));
                }
            }


            if (_mapData.QuestMarkersByMapName.TryGetValue(normalizedName, out var questMarkers))
            {
                foreach (var questMarker in questMarkers)
                {
                    if (questMarker.X == null || questMarker.Z == null)
                        continue;

                    var converted = ConvertGameToNormalizedMap(questMarker.X.Value, questMarker.Z.Value);

                    string category = NormalizeQuestCategory(questMarker.Category);
                    string cssClass = category == "item" ? "quest-item" : "quest-objective";
                    string description = string.IsNullOrWhiteSpace(questMarker.Description)
                        ? "Quest objective"
                        : questMarker.Description;

                    string itemText = string.IsNullOrWhiteSpace(questMarker.QuestItem)
                        ? ""
                        : $"\nItem: {questMarker.QuestItem}" +
                          (string.IsNullOrWhiteSpace(questMarker.ItemShortName) ? "" : $" ({questMarker.ItemShortName})");

                    string traderText = string.IsNullOrWhiteSpace(questMarker.Trader)
                        ? ""
                        : $"\nTrader: {questMarker.Trader}";

                    string levelText = questMarker.MinPlayerLevel > 0
                        ? $"\nLevel: {questMarker.MinPlayerLevel}+"
                        : "";

                    string optionalText = questMarker.Optional ? "\nOptional: yes" : "";

                    markers.Add(new WebMapMarker
                    {
                        Name = questMarker.Quest,
                        MarkerType = "quest",
                        Faction = "",
                        CssClass = cssClass,
                        Tooltip = $"{questMarker.Quest}\n{description}{itemText}{traderText}{levelText}{optionalText}",
                        ZoneName = questMarker.Quest,
                        Categories = category == "item" ? "Quest Item" : "Quest Objective",
                        Conditions = BuildQuestDetails(questMarker, description),
                        Position = $"X={questMarker.X.Value:0.##}, Y={(questMarker.Y ?? 0):0.##}, Z={questMarker.Z.Value:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY,
                        QuestCategory = category,
                        QuestObjectiveType = questMarker.ObjectiveType,
                        QuestItem = questMarker.QuestItem,
                        QuestItemShortName = questMarker.ItemShortName,
                        QuestItemIconLink = questMarker.ItemIconLink,
                        QuestTrader = questMarker.Trader,
                        QuestMinPlayerLevel = questMarker.MinPlayerLevel,
                        QuestOptional = questMarker.Optional
                    });
                }
            }


            if (_mapData.HazardsByMapName.TryGetValue(normalizedName, out var hazards))
            {
                foreach (var hazard in hazards.Where(h => h.Position != null))
                {
                    var converted = ConvertGameToNormalizedMap(hazard.Position!.X, hazard.Position.Z);
                    string hazardName = string.IsNullOrWhiteSpace(hazard.Name) ? "Hazard" : hazard.Name;

                    markers.Add(new WebMapMarker
                    {
                        Name = hazardName,
                        MarkerType = "hazard",
                        Faction = "",
                        CssClass = "hazard",
                        Tooltip = hazardName,
                        ZoneName = "",
                        Categories = hazard.HazardType ?? "hazard",
                        Conditions = "",
                        Position = $"X={hazard.Position.X:0.##}, Y={hazard.Position.Y:0.##}, Z={hazard.Position.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY
                    });
                }
            }

            if (_mapData.SwitchesByMapName.TryGetValue(normalizedName, out var switches))
            {
                foreach (var mapSwitch in switches.Where(s => s.Position != null))
                {
                    var converted = ConvertGameToNormalizedMap(mapSwitch.Position!.X, mapSwitch.Position.Z);
                    string switchName = string.IsNullOrWhiteSpace(mapSwitch.Name) ? "Switch" : mapSwitch.Name;

                    markers.Add(new WebMapMarker
                    {
                        Name = switchName,
                        MarkerType = "switch",
                        Faction = "",
                        CssClass = "map-switch",
                        Tooltip = switchName,
                        ZoneName = "",
                        Categories = "Switch",
                        Conditions = "",
                        Position = $"X={mapSwitch.Position.X:0.##}, Y={mapSwitch.Position.Y:0.##}, Z={mapSwitch.Position.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY
                    });
                }
            }

            if (_mapData.LabelsByMapName.TryGetValue(normalizedName, out var labels))
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

            SetParserStatus($"LOADED {markers.Count} MARKERS");
        }

        private List<WebMapOutlinePoint>? BuildNormalizedOutline(List<MapOutlinePoint>? outline)
        {
            if (outline == null || outline.Count == 0)
                return null;

            return outline
                .Select(point =>
                {
                    var converted = ConvertGameToNormalizedMap(point.X, point.Z);
                    return new WebMapOutlinePoint
                    {
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY
                    };
                })
                .ToList();
        }

        private string NormalizeQuestCategory(string category)
        {
            string value = (category ?? "").Trim().ToLowerInvariant();

            if (value == "item" || value == "items")
                return "item";

            return "objective";
        }

        private string BuildQuestDetails(QuestMarker questMarker, string description)
        {
            var details = new List<string>
            {
                $"Description: {description}",
                $"Category: {(NormalizeQuestCategory(questMarker.Category) == "item" ? "Item" : "Objective")}"
            };

            if (!string.IsNullOrWhiteSpace(questMarker.ObjectiveType))
                details.Add($"Objective Type: {questMarker.ObjectiveType}");

            if (!string.IsNullOrWhiteSpace(questMarker.Trader))
                details.Add($"Trader: {questMarker.Trader}");

            if (questMarker.MinPlayerLevel > 0)
                details.Add($"Required Level: {questMarker.MinPlayerLevel}");

            if (!string.IsNullOrWhiteSpace(questMarker.QuestItem))
                details.Add($"Item: {questMarker.QuestItem}");

            if (!string.IsNullOrWhiteSpace(questMarker.ItemShortName))
                details.Add($"Item Short Name: {questMarker.ItemShortName}");

            if (!string.IsNullOrWhiteSpace(questMarker.ItemIconLink))
                details.Add($"Item Icon: {questMarker.ItemIconLink}");

            details.Add($"Optional: {(questMarker.Optional ? "Yes" : "No")}");

            return string.Join(Environment.NewLine, details);
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

        private WebMapMarker BuildBossSpawnMarker(
            List<BossSpawnMarker> bossesAtLocation,
            (double normalizedX, double normalizedY) converted)
        {
            var primary = bossesAtLocation[0];
            string bossNames = string.Join(", ", bossesAtLocation.Select(b => b.BossName).Distinct());
            string locations = string.Join(", ", bossesAtLocation.Select(b => b.LocationName).Distinct());

            string chanceText = bossesAtLocation.Count == 1 && primary.SpawnChance > 0
                ? $"\nSpawn chance: {primary.SpawnChance:P0}"
                : "";

            return new WebMapMarker
            {
                Name = bossNames,
                MarkerType = "spawn-boss",
                Faction = "",
                CssClass = "spawn-boss",
                Tooltip = $"{bossNames}\n{locations}\nZone: {primary.ZoneName}{chanceText}",
                ZoneName = primary.ZoneName,
                Categories = "boss",
                Conditions = "",
                Position = $"X={primary.X:0.##}, Y={primary.Y:0.##}, Z={primary.Z:0.##}",
                NormalizedX = converted.normalizedX,
                NormalizedY = converted.normalizedY
            };
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

        private (double normalizedX, double normalizedY) ConvertGameToNormalizedMap(double gameX, double gameZ)
        {
            if (_currentMapConfig?.Svg?.Bounds == null || _currentMapConfig.Svg.Bounds.Length < 2)
                return (0.5, 0.5);

            var svg = _currentMapConfig.Svg;
            if (svg.Transform != null && svg.Transform.Length >= 4)
                return ConvertGameToNormalizedMapWithTransform(gameX, gameZ);

            double x1 = svg.Bounds[0][0];
            double z1 = svg.Bounds[0][1];
            double x2 = svg.Bounds[1][0];
            double z2 = svg.Bounds[1][1];

            double minX = Math.Min(x1, x2);
            double maxX = Math.Max(x1, x2);
            double minZ = Math.Min(z1, z2);
            double maxZ = Math.Max(z1, z2);

            double normalizedX = (gameX - minX) / (maxX - minX);
            double normalizedY = (gameZ - minZ) / (maxZ - minZ);

            switch (svg.CoordinateRotation)
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

        private (double normalizedX, double normalizedY) ConvertGameToNormalizedMapWithTransform(double gameX, double gameZ)
        {
            var svg = _currentMapConfig!.Svg;

            if (svg.MapPixelSize > 0)
            {
                int tileZoom = svg.NativeZoom > 0 ? svg.NativeZoom : 4;
                var (tilePx, tilePy) = GameCoordsToMapPixels(gameX, gameZ, tileZoom);
                return (tilePx / svg.MapPixelSize, tilePy / svg.MapPixelSize);
            }

            const int referenceZoom = 0;
            var (px, py) = GameCoordsToMapPixels(gameX, gameZ, referenceZoom);
            var (minPx, maxPx, minPy, maxPy) = GetBoundsPixelExtents(svg, referenceZoom);

            double width = maxPx - minPx;
            double height = maxPy - minPy;
            if (width <= 0 || height <= 0)
                return (0.5, 0.5);

            return ((px - minPx) / width, (py - minPy) / height);
        }

        private (double minPx, double maxPx, double minPy, double maxPy) GetBoundsPixelExtents(MapSvg svg, int zoom)
        {
            double[][] bounds = svg.SvgBounds ?? svg.Bounds;
            var corners = new (double x, double z)[]
            {
                (bounds[0][0], bounds[0][1]),
                (bounds[1][0], bounds[0][1]),
                (bounds[0][0], bounds[1][1]),
                (bounds[1][0], bounds[1][1])
            };

            double minPx = double.MaxValue;
            double maxPx = double.MinValue;
            double minPy = double.MaxValue;
            double maxPy = double.MinValue;

            foreach (var (x, z) in corners)
            {
                var (cornerPx, cornerPy) = GameCoordsToMapPixels(x, z, zoom);
                minPx = Math.Min(minPx, cornerPx);
                maxPx = Math.Max(maxPx, cornerPx);
                minPy = Math.Min(minPy, cornerPy);
                maxPy = Math.Max(maxPy, cornerPy);
            }

            return (minPx, maxPx, minPy, maxPy);
        }

        private (double px, double py) GameCoordsToMapPixels(double gameX, double gameZ, int zoom)
        {
            var svg = _currentMapConfig!.Svg;
            var (rotLat, rotLng) = ApplyMapCoordinateRotation(gameZ, gameX, svg.CoordinateRotation);

            double scale = Math.Pow(2, zoom);
            double scaleX = svg.Transform![0];
            double scaleY = svg.Transform[2] * -1;
            double marginX = svg.Transform[1];
            double marginY = svg.Transform[3];

            double px = scale * (scaleX * rotLng + marginX);
            double py = scale * (scaleY * rotLat + marginY);
            return (px, py);
        }

        private static (double lat, double lng) ApplyMapCoordinateRotation(double lat, double lng, int rotation)
        {
            if (rotation == 0)
                return (lat, lng);

            double angle = rotation * Math.PI / 180.0;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double rotatedLng = lng * cos - lat * sin;
            double rotatedLat = lng * sin + lat * cos;
            return (rotatedLat, rotatedLng);
        }

        private void StartScreenshotMonitoring()
        {
            if (!Directory.Exists(_screenshotFolder))
            {
                SetParserStatus("FOLDER NOT FOUND", isError: true);
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

            SetParserStatus("MONITORING SCREENSHOTS");
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
                SetParserStatus("PARSE FAILED", isError: true);
                StatusText.Text = "PARSE FAILED";
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
            direction = ApplyMapDirectionOffset(direction);

            _lastGameX = gameX;
            _lastGameZ = gameZ;
            _lastDirection = direction;

            GameXText.Text = gameX.ToString("0.00", CultureInfo.InvariantCulture);
            GameYText.Text = gameY.ToString("0.00", CultureInfo.InvariantCulture);
            GameZText.Text = gameZ.ToString("0.00", CultureInfo.InvariantCulture);
            DirectionText.Text = direction.ToString("0.00", CultureInfo.InvariantCulture) + "°";

            SetParserStatus($"TRACKING {DateTime.Now:T}");

            StatusText.Text = $"X={gameX:0.00} Y={gameY:0.00} Z={gameZ:0.00} DIR={direction:0.00}";

            DrawPlayerMarkerFromGameCoordinates(gameX, gameZ, direction);
        }

        private double GetYawFromQuaternion(double x, double y, double z, double w)
        {
            double sinyCosp = 2.0 * (w * y + x * z);
            double cosyCosp = 1.0 - 2.0 * (y * y + z * z);

            double yawRadians = Math.Atan2(sinyCosp, cosyCosp);
            return yawRadians * (180.0 / Math.PI);
        }

        private double ApplyMapDirectionOffset(double direction)
        {
            string mapName = MapDataService.NormalizeMapName(_currentMapDisplayName ?? CurrentMapText.Text);

            // Customs and the other maps tested use the raw screenshot direction correctly.
            // Factory/Night Factory SVG orientation is opposite for player-facing direction,
            // so correct only those maps before drawing the marker in the main window and overlay.
            if (mapName == "factory" || mapName == "nightfactory")
                direction += 90;

            while (direction > 180) direction -= 360;
            while (direction < -180) direction += 360;

            return direction;
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

            _overlayWindow.ConfigureMapAssetHost(_mapsFolder);
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

            if (_currentMapLevelsConfig != null)
            {
                string stateJson = JsonSerializer.Serialize(BuildMapLevelState());
                await _overlayWindow.ApplyMapLevelStateAsync(stateJson);
            }

            await _overlayWindow.ApplyMarkerFiltersAsync(JsonSerializer.Serialize(BuildMarkerFilterState()));
        }

        protected override void OnClosed(EventArgs e)
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
            base.OnClosed(e);
        }
    }
}
