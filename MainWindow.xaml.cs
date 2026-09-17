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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TarkovTracker.Helpers;
using TarkovTracker.Models;
using TarkovTracker.Services;

namespace TarkovTracker
{
    public partial class MainWindow : Window
    {
        internal const string MapAssetHostName = "tarkovtracker.local";
        internal const string BaseLayerToggleTag = "__base_layer_toggle__";

        private readonly MapDataService _mapData = new();
        private readonly ScreenshotExfilParser _screenshotExfilParser = new();
        private readonly string _mapsFolder;
        private readonly string _userSettingsFile;
        private OverlayWindow? _overlayWindow;
        private string? _currentMapPath;
        private string? _currentMapHtml;
        private string? _lastMapMarkersJson;
        private string? _lastRaidExfilHighlightsJson;
        private (double NormalizedX, double NormalizedY, double DirectionDegrees)? _lastPlayerMarker;

        private readonly RaidExfilHighlightState _raidExfilHighlights = new();
        private readonly List<CustomPinEntry> _customPins = new();

        private string _screenshotFolder;
        private MapConfig? _currentMapConfig;
        private MapLevelsConfig? _currentMapLevelsConfig;
        private string? _currentMapDisplayName;

        private FileSystemWatcher? _screenshotWatcher;
        private DateTime _lastProcessedTime = DateTime.MinValue;

        private double? _lastGameX;
        private double? _lastGameY;
        private double? _lastGameZ;
        private double? _lastDirection;
        private bool _suppressQuestFilterRefresh;

        private bool _webViewReady = false;
        private bool _suppressMapLevelRefresh = false;
        private bool _webMessageHooked = false;
        private bool _mapWebViewHostMapped = false;
        private bool _mapWebViewHardened = false;

        private readonly UserAppSettings _userSettings = new();
        private bool _markerFiltersHaveSavedState;
        private bool _suppressMapSelectionPersistence;
        private bool _isInitializing;
        private bool _startupChecksStarted;
        private Task<MapDataRefreshResult>? _mapDataRefreshTask;
        private readonly object _mapDataRefreshLock = new();
        private string? _lastQuestTarkovDevUrl;
        private string? _lastQuestWikiUrl;

        private GlobalHotKeyManager? _globalHotKeyManager = null;

        private static readonly JsonSerializerOptions WebMessageJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly string[] AllowedExternalBrowserHosts =
        {
            "tarkov.dev",
            "www.tarkov.dev",
            "escapefromtarkov.fandom.com",
            "github.com",
            "www.github.com"
        };

        private static readonly JsonSerializerOptions UserSettingsJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };

        internal string ScreenshotFolderPath => _screenshotFolder;

        internal string CurrentGameResolutionPreset =>
            string.IsNullOrWhiteSpace(_userSettings.GameResolutionPreset)
                ? "auto"
                : _userSettings.GameResolutionPreset;

        internal bool IsScreenshotParsingEnabled => _userSettings.ScreenshotParsingEnabled;

        internal double OverlayDefaultOpacityPercent =>
            Math.Clamp(_userSettings.OverlaySettings.OverlayDefaultOpacityPercent, 20, 100);

        internal bool OverlayCenterOnPlayer => _userSettings.OverlayCenterOnPlayer;

        internal bool CheckForUpdatesOnStartup => _userSettings.CheckForUpdatesOnStartup;

        public MainWindow()
        {
            _isInitializing = true;
            _suppressMarkerFilterRefresh = true;

            _mapsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps");
            _userSettingsFile = AppInfo.SettingsFilePath;

            InitializeComponent();

            AppVersionSubtitleText.Text = $"TACTICAL MAP INTERFACE v{AppInfo.VersionLabel}";
            Title = $"{AppInfo.ProductName} v{AppInfo.VersionLabel}";

            TryMigrateLegacySettingsFile();
            _userSettings = LoadUserSettings();
            _screenshotFolder = _userSettings.ScreenshotFolder;

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

            ApplySaveMarkerFiltersSettingToUi();
            ApplyAutoSelectFloorSettingToUi();
            ApplyShowQuestNamesSettingToUi();
            ApplyMarkerFiltersForSession();

            _suppressMarkerFilterRefresh = false;
            _isInitializing = false;

            LoadMapsDropdown();
            RestoreLastSelectedMap();
            UpdateScreenshotMonitoringStatus();

            Closing += (_, _) => SaveUserSettings();
            Loaded += MainWindow_Loaded;
            SourceInitialized += MainWindow_SourceInitialized;
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            _globalHotKeyManager = new GlobalHotKeyManager(this);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startupChecksStarted)
                return;

            _startupChecksStarted = true;

            await RunStartupMapDataRefreshAsync();

            if (_userSettings.CheckForUpdatesOnStartup)
                await RunStartupUpdateCheckAsync();
        }

        private async Task RunStartupMapDataRefreshAsync()
        {
            try
            {
                var progress = new Progress<string>(status => StatusText.Text = status.ToUpperInvariant());
                MapDataRefreshResult result = await RefreshMapDataFromTarkovDevAsync(progress);
                if (!string.IsNullOrWhiteSpace(result.Message))
                    StatusText.Text = result.Message.ToUpperInvariant();
            }
            catch
            {
                StatusText.Text = "MAP DATA REFRESH FAILED";
            }
        }

        private async Task RunStartupUpdateCheckAsync()
        {
            AppUpdateCheckResult check;
            try
            {
                check = await AppUpdateService.CheckForUpdatesAsync();
            }
            catch
            {
                // Startup checks stay silent; SETTINGS has the manual check for diagnostics.
                return;
            }

            if (!check.Succeeded || !check.UpdateAvailable)
                return;

            if (string.Equals(
                    check.LatestVersion,
                    _userSettings.SkippedUpdateVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                this,
                $"{check.Message}\n\n" +
                "YES – download and install now (the app restarts)\n" +
                "NO – skip this version\n" +
                "CANCEL – remind me at next startup",
                "Update Available",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (choice == MessageBoxResult.No)
            {
                _userSettings.SkippedUpdateVersion = check.LatestVersion;
                SaveUserSettings();
                StatusText.Text = $"Skipped update {check.LatestVersion}";
                return;
            }

            if (choice != MessageBoxResult.Yes)
                return;

            var progress = new Progress<string>(status => StatusText.Text = status);
            await AppUpdateFlow.DownloadAndRestartAsync(this, check, progress);
        }

        private void TryMigrateLegacySettingsFile()
        {
            string localPath = GetUserSettingsPath();
            if (File.Exists(localPath))
                return;

            string legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SayserTarkovTracker",
                "settings.json");

            if (!File.Exists(legacyPath))
                return;

            try
            {
                string? legacyDirectory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(legacyDirectory))
                    Directory.CreateDirectory(legacyDirectory);

                File.Copy(legacyPath, localPath);
            }
            catch
            {
                // Best-effort migration only.
            }
        }

        private UserAppSettings LoadUserSettings()
        {
            try
            {
                string settingsPath = GetUserSettingsPath();
                if (!File.Exists(settingsPath))
                {
                    _markerFiltersHaveSavedState = false;
                    return new UserAppSettings();
                }

                string json = File.ReadAllText(settingsPath);
                _markerFiltersHaveSavedState =
                    json.Contains("\"MarkerFilters\"", StringComparison.OrdinalIgnoreCase);

                var settings = JsonSerializer.Deserialize<UserAppSettings>(
                    json,
                    UserSettingsJsonOptions);

                settings ??= new UserAppSettings();
                settings.MarkerFilters ??= new MarkerFilterPreferences();
                return settings;
            }
            catch
            {
                _markerFiltersHaveSavedState = false;
                return new UserAppSettings();
            }
        }

        private string GetUserSettingsPath()
        {
            if (!string.IsNullOrWhiteSpace(_userSettingsFile))
                return _userSettingsFile;

            return AppInfo.SettingsFilePath;
        }

        private void SaveUserSettings()
        {
            if (_isInitializing)
                return;

            SyncSaveMarkerFiltersFromUiIfReady();
            SyncMarkerFiltersFromUiIfReady();

            string settingsPath = GetUserSettingsPath();
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(_userSettings, UserSettingsJsonOptions);
            string tempPath = settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, settingsPath, overwrite: true);
        }

        private void SyncSaveMarkerFiltersFromUiIfReady()
        {
            if (SaveMarkerFiltersCheckBox == null)
                return;

            _userSettings.SaveMarkerFilters = SaveMarkerFiltersCheckBox.IsChecked == true;
        }

        private void SyncMarkerFiltersFromUiIfReady()
        {
            if (_isInitializing || !_userSettings.SaveMarkerFilters || !AreMarkerFilterControlsReady())
                return;

            _userSettings.MarkerFilters = ReadMarkerFiltersFromUi();
            _markerFiltersHaveSavedState = true;
        }

        private void RestoreLastSelectedMap()
        {
            if (string.IsNullOrWhiteSpace(_userSettings.LastSelectedMap))
                return;

            string target = _userSettings.LastSelectedMap.Trim();

            foreach (ComboBoxItem item in MapComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is not string mapPath)
                    continue;

                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(mapPath),
                        target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _suppressMapSelectionPersistence = true;
                MapComboBox.SelectedItem = item;
                _suppressMapSelectionPersistence = false;
                return;
            }
        }

        private void PersistLastSelectedMap(string mapFileName)
        {
            if (_suppressMapSelectionPersistence)
                return;

            _userSettings.LastSelectedMap = mapFileName;
              SaveUserSettings();
        }

        private void SaveScreenshotFolder(string folder)
        {
            _userSettings.ScreenshotFolder = folder;
              SaveUserSettings();
        }

        internal void ApplyGameResolutionPreset(string preset)
        {
            _userSettings.GameResolutionPreset = string.IsNullOrWhiteSpace(preset) ? "auto" : preset;
              SaveUserSettings();
        }

        internal void ApplyOverlayDefaultOpacityPercent(double percent)
        {
            _userSettings.OverlaySettings.OverlayDefaultOpacityPercent = Math.Clamp(percent, 20, 100);
              SaveUserSettings();
            _overlayWindow?.ApplyDefaultOpacityPercent(_userSettings.OverlaySettings.OverlayDefaultOpacityPercent);
        }

        internal void ApplyOverlayCenterOnPlayer(bool enabled)
        {
            _userSettings.OverlayCenterOnPlayer = enabled;
            SaveUserSettings();
        }

        internal void ApplyScreenshotParsingEnabled(bool enabled)
        {
            _userSettings.ScreenshotParsingEnabled = enabled;
            SaveUserSettings();
            UpdateScreenshotMonitoringStatus();
        }

        internal void ApplyCheckForUpdatesOnStartup(bool enabled)
        {
            _userSettings.CheckForUpdatesOnStartup = enabled;

            // Re-enabling means the user wants prompts again, including for a skipped release.
            if (enabled)
                _userSettings.SkippedUpdateVersion = "";

            SaveUserSettings();
        }

        internal void ClearRaidExfilHighlights()
        {
            _raidExfilHighlights.Clear();
            UpdateRaidExfilUi();
            _ = ApplyRaidExfilHighlightsAsync();
        }

        internal bool PromptForScreenshotFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Escape from Tarkov Screenshots Folder",
                InitialDirectory = Directory.Exists(_screenshotFolder)
                    ? _screenshotFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() != true)
                return false;

            _screenshotFolder = dialog.FolderName;
            SaveScreenshotFolder(_screenshotFolder);

            _screenshotWatcher?.Dispose();
            UpdateScreenshotMonitoringStatus();

            StatusText.Text = $"FOLDER: {_screenshotFolder}";
            SetParserStatus("FOLDER UPDATED");
            return true;
        }

        internal void OpenScreenshotFolder()
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

        internal int DeleteAllScreenshots()
        {
            if (string.IsNullOrWhiteSpace(_screenshotFolder) || !Directory.Exists(_screenshotFolder))
                return 0;

            int deleted = 0;

            foreach (string file in Directory.GetFiles(_screenshotFolder, "*.png"))
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    // Skip files that are locked or otherwise unavailable.
                }
            }

            _lastProcessedTime = DateTime.MinValue;
            LastScreenshotText.Text = "NONE";
            SetParserStatus("SCREENSHOTS CLEARED");

            return deleted;
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(this);
            settingsWindow.ShowDialog();
        }

        private (int width, int height) GetConfiguredGameResolution()
        {
            return ScreenshotResolutionHelper.ParsePreset(_userSettings.GameResolutionPreset);
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

            StatusText.Text = BuildLoadedDataStatus(svgFiles.Count);
        }

        private string BuildLoadedDataStatus(int? mapCount = null)
        {
            string mapsPrefix = mapCount.HasValue
                ? $"{mapCount.Value} maps loaded. "
                : "";

            return
                $"{mapsPrefix}Extract maps: {_mapData.ExtractsByMapName.Count}. Transit maps: {_mapData.TransitsByMapName.Count}. Spawn maps: {_mapData.SpawnsByMapName.Count}. Boss spawn maps: {_mapData.BossSpawnMarkersByMapName.Count}. Label maps: {_mapData.LabelsByMapName.Count}. Quest maps: {_mapData.QuestMarkersByMapName.Count}. Hazard maps: {_mapData.HazardsByMapName.Count}. Switch maps: {_mapData.SwitchesByMapName.Count}. Loot maps: {_mapData.TrackedLootByMapName.Count}.";
        }

        internal MapDataRefreshResult? LastMapDataRefresh =>
            TarkovDevMapRefreshService.TryReadLastRefresh(_mapData.OverlayConfigDirectory);

        internal async Task<MapDataRefreshResult> RefreshMapDataFromTarkovDevAsync(
            IProgress<string>? progress,
            CancellationToken cancellationToken = default)
        {
            Task<MapDataRefreshResult> pending;
            lock (_mapDataRefreshLock)
            {
                _mapDataRefreshTask ??= RunMapDataRefreshAsync(progress, cancellationToken);
                pending = _mapDataRefreshTask;
            }

            try
            {
                return await pending;
            }
            finally
            {
                lock (_mapDataRefreshLock)
                {
                    if (ReferenceEquals(_mapDataRefreshTask, pending))
                        _mapDataRefreshTask = null;
                }
            }
        }

        private async Task<MapDataRefreshResult> RunMapDataRefreshAsync(
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            MapDataRefreshResult result = await TarkovDevMapRefreshService.RefreshAsync(
                _mapData.OverlayConfigDirectory,
                progress,
                cancellationToken);

            if (result.FilesWritten <= 0)
                return result;

            await Task.Run(() => _mapData.LoadAll(), cancellationToken);

            if (MapComboBox.SelectedItem is ComboBoxItem)
                await LoadSelectedMapAsync();
            else
                StatusText.Text = BuildLoadedDataStatus();

            return result;
        }

        private async void MapComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await RunSafeAsync(LoadSelectedMapAsync, "Map load");
        }

        private async Task LoadSelectedMapAsync()
        {
            if (MapComboBox.SelectedItem is not ComboBoxItem item)
                return;

            string? mapPath = item.Tag as string;

            if (string.IsNullOrWhiteSpace(mapPath))
                return;

            string mapFileName = Path.GetFileNameWithoutExtension(mapPath);
            _currentMapPath = mapPath;

            CurrentMapText.Text = mapFileName.ToUpperInvariant();
            PersistLastSelectedMap(mapFileName);

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

            ClearRaidExfilHighlightsForMapChange();
            LoadCustomPinsForCurrentMap();

            await LoadSvgMapInWebView(mapPath);
            LoadLayersPanel(mapPath);
            await ApplyMapLevelStateAsync();

            UpdateBtrControlsVisibility();
            UpdateCultistControlsVisibility();
            ApplyMapSpecificMarkerFiltersFromSaved();
            await DrawMapMarkersForCurrentMapAsync();
            PopulateQuestFilterOptions();
            await ApplySearchAndQuestFiltersAsync();
            await ApplyCustomPinsToMapAsync();
            await ApplyMarkerVisibility();
            RedrawLastMarker();
            await SyncOverlayToCurrentMapAsync();

            StatusText.Text = _currentMapConfig == null
                ? $"Loaded map: {mapFileName}. No config found."
                : $"Loaded map: {mapFileName}. Rotation={_currentMapConfig.Svg.CoordinateRotation}.";
        }

        private async System.Threading.Tasks.Task EnsureMapWebViewHostMappingAsync()
        {
            await MapWebView.EnsureCoreWebView2Async();
            WebViewSecurity.ApplyOnce(MapWebView.CoreWebView2, MapAssetHostName, ref _mapWebViewHardened);

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
            _currentMapPath = mapPath;
            await LoadSvgContentInWebView(
                File.ReadAllText(mapPath),
                Path.GetDirectoryName(mapPath) ?? _mapsFolder);
        }

        private async System.Threading.Tasks.Task LoadSvgContentInWebView(string svg, string assetBaseDirectory)
        {
            _webViewReady = false;

            string preparedSvg = PrepareSvgForWebView(svg, assetBaseDirectory);
            string html = MapHtmlBuilder.Build(preparedSvg, MapAssetHostName);
            _currentMapHtml = html;

            await EnsureMapWebViewHostMappingAsync();

            if (!_webMessageHooked)
            {
                MapWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                _webMessageHooked = true;
            }

            await WebViewSecurity.NavigateToStringAsync(MapWebView.CoreWebView2, html);

            _webViewReady = true;
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
                if (!document.RootElement.TryGetProperty("messageType", out JsonElement messageTypeElement))
                    return;

                string messageType = messageTypeElement.GetString() ?? string.Empty;

                if (string.Equals(messageType, "customPinsChanged", StringComparison.OrdinalIgnoreCase))
                {
                    var pinsMessage = JsonSerializer.Deserialize<CustomPinsChangedMessage>(
                        e.WebMessageAsJson,
                        WebMessageJsonOptions);

                    _customPins.Clear();
                    if (pinsMessage?.Pins != null)
                        _customPins.AddRange(pinsMessage.Pins);

                    SaveCustomPinsForCurrentMap();
                    _ = SyncCustomPinsToOverlayAsync();
                    return;
                }

                if (!string.Equals(messageType, "markerClicked", StringComparison.OrdinalIgnoreCase))
                    return;

                var marker = JsonSerializer.Deserialize<WebMarkerClickMessage>(
                    e.WebMessageAsJson,
                    WebMessageJsonOptions);

                if (marker == null)
                    return;

                SelectedMarkerNameText.Text = marker.Name;

                SelectedMarkerDetailsText.Text = string.Join(Environment.NewLine, BuildSelectedMarkerDetails(marker));

                if (string.Equals(marker.MarkerType, "quest", StringComparison.OrdinalIgnoreCase))
                {
                    string questDisplayName = !string.IsNullOrWhiteSpace(marker.ZoneName)
                        ? marker.ZoneName
                        : marker.Name;

                    _lastQuestTarkovDevUrl = !string.IsNullOrWhiteSpace(marker.QuestSlug)
                        ? TarkovDevLinks.BuildTaskUrlFromSlug(marker.QuestSlug)
                        : TarkovDevLinks.BuildTaskUrl(questDisplayName);
                    _lastQuestWikiUrl = TarkovDevLinks.BuildWikiUrl(questDisplayName);
                    OpenQuestTarkovDevButton.Content = "OPEN ON TARKOV.DEV";
                    OpenQuestWikiButton.Content = "OPEN ON WIKI";
                    QuestLinkPanel.Visibility = Visibility.Visible;
                }
                else if (string.Equals(marker.MarkerType, "extract", StringComparison.OrdinalIgnoreCase))
                {
                    _lastQuestTarkovDevUrl = FirstAllowedUrl(marker.TarkovDevUrl, TarkovDevLinks.BuildMapUrl(_currentMapDisplayName ?? ""));
                    _lastQuestWikiUrl = FirstAllowedUrl(
                        marker.WikiUrl,
                        TarkovDevLinks.BuildExtractWikiUrl(_currentMapDisplayName ?? "", marker.Name));
                    OpenQuestTarkovDevButton.Content = "OPEN MAP ON TARKOV.DEV";
                    OpenQuestWikiButton.Content = "OPEN EXTRACT ON WIKI";
                    QuestLinkPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    _lastQuestTarkovDevUrl = null;
                    _lastQuestWikiUrl = null;
                    QuestLinkPanel.Visibility = Visibility.Collapsed;
                }

                if (string.Equals(marker.MarkerType, "extract", StringComparison.OrdinalIgnoreCase))
                {
                    if (marker.LinkedSwitchIds is { Count: > 0 })
                        _ = HighlightLinkedSwitchesAsync(marker.LinkedSwitchIds);
                    else
                        _ = ClearLinkedSwitchHighlightsAsync();
                }
            }
            catch (Exception ex)
            {
                SelectedMarkerNameText.Text = "Marker read error";
                SelectedMarkerDetailsText.Text = ex.Message;
                _lastQuestTarkovDevUrl = null;
                _lastQuestWikiUrl = null;
                QuestLinkPanel.Visibility = Visibility.Collapsed;
            }
        }

        private string PrepareSvgForWebView(string svg, string assetBaseDirectory)
        {
            string baseDir = Directory.Exists(assetBaseDirectory)
                ? assetBaseDirectory
                : (Path.GetDirectoryName(assetBaseDirectory) ?? _mapsFolder);

            return Regex.Replace(
                svg,
                @"(?<attr>(?:xlink:)?href)\s*=\s*""(?!(?:data:|https?:|file:))(?<path>[^""]+)""",
                match =>
                {
                    string relativePath = match.Groups["path"].Value;
                    string fullPath = Path.GetFullPath(Path.Combine(baseDir, relativePath));

                    if (!File.Exists(fullPath))
                        return match.Value;

                    string relativeFromMaps = Path.GetRelativePath(_mapsFolder, fullPath).Replace('\\', '/');
                    string assetUrl = $"https://{MapAssetHostName}/{relativeFromMaps}";
                    return $"{match.Groups["attr"].Value}=\"{assetUrl}\"";
                },
                RegexOptions.IgnoreCase);
        }


        private void LoadLayersPanel(string mapPath)
        {
            LayersPanel.Children.Clear();
            _globalHotKeyManager?.UnregisterAll();

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
                    if (_suppressMapLevelRefresh)
                        return;

                    if (_currentMapLevelsConfig?.ExclusiveLayers == true)
                    {
                        _suppressMapLevelRefresh = true;
                        foreach (var other in LayersPanel.Children)
                        {
                            if (other is CheckBox otherBox && !IsBaseLayerCheckbox(otherBox))
                                otherBox.IsChecked = false;
                        }

                        _suppressMapLevelRefresh = false;
                    }

                    await ApplyMapLevelStateAsync();
                };
                baseCheckBox.Unchecked += async (_, _) =>
                {
                    if (!_suppressMapLevelRefresh)
                        await ApplyMapLevelStateAsync();
                };

                LayersPanel.Children.Add(baseCheckBox);
            }

            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                var hotkey = Hotkeys.NumpadKeys.ElementAt(i+1);

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
                _globalHotKeyManager?.Register(ModifierKeys.None, hotkey, () =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {                        
                        checkBox.IsChecked = !(checkBox.IsChecked ?? false);
                    });
                });

                checkBox.Checked += async (_, _) =>
                {
                    if (_suppressMapLevelRefresh)
                        return;

                    if (_currentMapLevelsConfig?.ExclusiveLayers == true)
                    {
                        _suppressMapLevelRefresh = true;
                        foreach (var other in LayersPanel.Children)
                        {
                            if (other is not CheckBox otherBox || ReferenceEquals(otherBox, checkBox))
                                continue;
                            if (IsBaseLayerCheckbox(otherBox))
                            {
                                otherBox.IsChecked = false;
                                continue;
                            }

                            otherBox.IsChecked = false;
                        }

                        _suppressMapLevelRefresh = false;
                    }

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

            foreach (var checkBox in LayersPanel.Children.OfType<CheckBox>())
            {
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

            if (config.ExclusiveLayers && activeLevelIds.Count > 0)
            {
                showBaseLayer = false;
                dimBase = false;
            }

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
                ExclusiveLayers = config.ExclusiveLayers,
                ActiveLevelIds = activeLevelIds,
                LevelExtents = config.Levels
                    .Where(level => !string.IsNullOrWhiteSpace(level.SvgLayer) &&
                                    level.MinHeight.HasValue &&
                                    level.MaxHeight.HasValue)
                    .Select(level => new MapLevelExtent
                    {
                        SvgLayer = level.SvgLayer!,
                        MinHeight = level.MinHeight!.Value,
                        MaxHeight = level.MaxHeight!.Value
                    })
                    .ToList()
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
            bool supportsBtr = CurrentMapSupportsBtr();
            bool supportsCultists = CurrentMapSupportsCultists();

            return new MarkerFilterState
            {
                PmcExtracts = ShowPmcExtractsCheckBox?.IsChecked == true,
                ScavExtracts = ShowScavExtractsCheckBox?.IsChecked == true,
                SharedExtracts = ShowSharedExtractsCheckBox?.IsChecked == true,
                Transits = ShowTransitsCheckBox?.IsChecked == true,
                PmcSpawns = ShowPmcSpawnsCheckBox?.IsChecked == true,
                ScavSpawns = ShowScavSpawnsCheckBox?.IsChecked == true,
                BossSpawns = ShowBossSpawnsCheckBox?.IsChecked == true,
                CultistSpawns = supportsCultists && ShowCultistSpawnsCheckBox?.IsChecked == true,
                Labels = ShowLabelsCheckBox?.IsChecked == true,
                QuestItems = ShowQuestItemsCheckBox?.IsChecked == true,
                QuestObjectives = ShowQuestObjectivesCheckBox?.IsChecked == true,
                ShowQuestNames = ShowQuestNamesCheckBox?.IsChecked == true,
                Hazards = ShowHazardsCheckBox?.IsChecked == true,
                HazardZones = ShowHazardZonesCheckBox?.IsChecked == true,
                Switches = ShowSwitchesCheckBox?.IsChecked == true,
                Valuables = ShowValuablesCheckBox?.IsChecked == true,
                BattlePassDocuments = ShowBattlePassDocumentsCheckBox?.IsChecked == true,
                Safes = ShowSafesCheckBox?.IsChecked == true,
                GroundCaches = ShowGroundCachesCheckBox?.IsChecked == true,
                Locks = ShowLocksCheckBox?.IsChecked == true,
                BtrStops = supportsBtr && ShowBtrStopsCheckBox?.IsChecked == true, 
                CustomPins = ShowCustomPinsCheckBox?.IsChecked == true
            };
        }

        private string? GetCurrentMapStorageKey()
        {
            if (!string.IsNullOrWhiteSpace(_currentMapDisplayName))
                return MapDataService.NormalizeMapName(_currentMapDisplayName);

            if (!string.IsNullOrWhiteSpace(_currentMapPath))
                return MapDataService.NormalizeMapName(Path.GetFileNameWithoutExtension(_currentMapPath));

            return null;
        }

        private void LoadCustomPinsForCurrentMap()
        {
            _customPins.Clear();

            string? mapKey = GetCurrentMapStorageKey();
            if (string.IsNullOrWhiteSpace(mapKey))
                return;

            if (_userSettings.CustomPinsByMap.TryGetValue(mapKey, out List<CustomPinEntry>? savedPins) &&
                savedPins != null)
            {
                _customPins.AddRange(savedPins);
            }
        }

        private void SaveCustomPinsForCurrentMap()
        {
            string? mapKey = GetCurrentMapStorageKey();
            if (string.IsNullOrWhiteSpace(mapKey))
                return;

            _userSettings.CustomPinsByMap[mapKey] = _customPins
                .Select(pin => new CustomPinEntry
                {
                    Id = pin.Id,
                    NormalizedX = pin.NormalizedX,
                    NormalizedY = pin.NormalizedY
                })
                .ToList();

              SaveUserSettings();
        }

        private async System.Threading.Tasks.Task ApplyCustomPinsToMapAsync()
        {
            if (!_webViewReady)
                return;

            string pinsJson = JsonSerializer.Serialize(_customPins);
            await MapWebView.ExecuteScriptAsync($"setCustomPins({pinsJson});");
            await ApplyMarkerVisibility();
            await SyncCustomPinsToOverlayAsync();
        }

        private static bool IsMarkerCheckBoxChecked(CheckBox? checkBox) =>
            checkBox?.IsChecked == true;

        private bool AreMarkerFilterControlsReady() =>
            ShowLabelsCheckBox != null &&
            ShowQuestItemsCheckBox != null &&
            ShowPmcExtractsCheckBox != null;

        private void ApplySaveMarkerFiltersSettingToUi()
        {
            if (SaveMarkerFiltersCheckBox == null)
                return;

            SaveMarkerFiltersCheckBox.IsChecked = _userSettings.SaveMarkerFilters;
        }

        private void ApplyAutoSelectFloorSettingToUi()
        {
            if (AutoSelectFloorCheckBox == null)
                return;

            AutoSelectFloorCheckBox.IsChecked = _userSettings.AutoSelectFloorFromPlayerHeight;
        }

        private void ApplyShowQuestNamesSettingToUi()
        {
            if (ShowQuestNamesCheckBox == null)
                return;

            ShowQuestNamesCheckBox.IsChecked = _userSettings.ShowQuestNames;
        }

        private void ApplyMarkerFiltersForSession()
        {
            if (!AreMarkerFilterControlsReady())
                return;

            MarkerFilterPreferences filters = _userSettings.SaveMarkerFilters
                ? _userSettings.MarkerFilters ?? new MarkerFilterPreferences()
                : new MarkerFilterPreferences();

            ApplyMarkerFilterPreferencesToUi(filters);
        }

        private void ApplyMarkerFilterPreferencesToUi(MarkerFilterPreferences filters)
        {
            _suppressMarkerFilterRefresh = true;

            if (ShowLabelsCheckBox != null) ShowLabelsCheckBox.IsChecked = filters.Labels;
            if (ShowQuestItemsCheckBox != null) ShowQuestItemsCheckBox.IsChecked = filters.QuestItems;
            if (ShowQuestObjectivesCheckBox != null) ShowQuestObjectivesCheckBox.IsChecked = filters.QuestObjectives;
            if (ShowPmcExtractsCheckBox != null) ShowPmcExtractsCheckBox.IsChecked = filters.PmcExtracts;
            if (ShowScavExtractsCheckBox != null) ShowScavExtractsCheckBox.IsChecked = filters.ScavExtracts;
            if (ShowSharedExtractsCheckBox != null) ShowSharedExtractsCheckBox.IsChecked = filters.SharedExtracts;
            if (ShowTransitsCheckBox != null) ShowTransitsCheckBox.IsChecked = filters.Transits;
            if (ShowHazardsCheckBox != null) ShowHazardsCheckBox.IsChecked = filters.Hazards;
            if (ShowHazardZonesCheckBox != null) ShowHazardZonesCheckBox.IsChecked = filters.HazardZones;
            if (ShowSwitchesCheckBox != null) ShowSwitchesCheckBox.IsChecked = filters.Switches;
            if (ShowValuablesCheckBox != null) ShowValuablesCheckBox.IsChecked = filters.Valuables;
            if (ShowBattlePassDocumentsCheckBox != null) ShowBattlePassDocumentsCheckBox.IsChecked = filters.BattlePassDocuments;
            if (ShowSafesCheckBox != null) ShowSafesCheckBox.IsChecked = filters.Safes;
            if (ShowGroundCachesCheckBox != null) ShowGroundCachesCheckBox.IsChecked = filters.GroundCaches;
            if (ShowLocksCheckBox != null) ShowLocksCheckBox.IsChecked = filters.Locks;
            if (ShowPmcSpawnsCheckBox != null) ShowPmcSpawnsCheckBox.IsChecked = filters.PmcSpawns;
            if (ShowScavSpawnsCheckBox != null) ShowScavSpawnsCheckBox.IsChecked = filters.ScavSpawns;
            if (ShowBossSpawnsCheckBox != null) ShowBossSpawnsCheckBox.IsChecked = filters.BossSpawns;
            if (ShowCultistSpawnsCheckBox != null) ShowCultistSpawnsCheckBox.IsChecked = filters.CultistSpawns;
            if (ShowBtrStopsCheckBox != null) ShowBtrStopsCheckBox.IsChecked = filters.BtrStops;
            if (ShowCustomPinsCheckBox != null) ShowCustomPinsCheckBox.IsChecked = filters.CustomPins;

            _suppressMarkerFilterRefresh = false;

            if (_webViewReady)
                _ = ApplyMarkerVisibility();
        }

        private MarkerFilterPreferences ReadMarkerFiltersFromUi()
        {
            MarkerFilterPreferences fallback = _userSettings.MarkerFilters ?? new MarkerFilterPreferences();

            if (!AreMarkerFilterControlsReady())
                return fallback;

            bool supportsBtr = CurrentMapSupportsBtr();
            bool supportsCultists = CurrentMapSupportsCultists();

            return new MarkerFilterPreferences
            {
                Labels = IsMarkerCheckBoxChecked(ShowLabelsCheckBox),
                QuestItems = IsMarkerCheckBoxChecked(ShowQuestItemsCheckBox),
                QuestObjectives = IsMarkerCheckBoxChecked(ShowQuestObjectivesCheckBox),
                PmcExtracts = IsMarkerCheckBoxChecked(ShowPmcExtractsCheckBox),
                ScavExtracts = IsMarkerCheckBoxChecked(ShowScavExtractsCheckBox),
                SharedExtracts = IsMarkerCheckBoxChecked(ShowSharedExtractsCheckBox),
                Transits = IsMarkerCheckBoxChecked(ShowTransitsCheckBox),
                Hazards = IsMarkerCheckBoxChecked(ShowHazardsCheckBox),
                HazardZones = IsMarkerCheckBoxChecked(ShowHazardZonesCheckBox),
                Switches = IsMarkerCheckBoxChecked(ShowSwitchesCheckBox),
                Valuables = IsMarkerCheckBoxChecked(ShowValuablesCheckBox),
                BattlePassDocuments = IsMarkerCheckBoxChecked(ShowBattlePassDocumentsCheckBox),
                Safes = IsMarkerCheckBoxChecked(ShowSafesCheckBox),
                GroundCaches = IsMarkerCheckBoxChecked(ShowGroundCachesCheckBox),
                Locks = IsMarkerCheckBoxChecked(ShowLocksCheckBox),
                PmcSpawns = IsMarkerCheckBoxChecked(ShowPmcSpawnsCheckBox),
                ScavSpawns = IsMarkerCheckBoxChecked(ShowScavSpawnsCheckBox),
                BossSpawns = IsMarkerCheckBoxChecked(ShowBossSpawnsCheckBox),
                CultistSpawns = supportsCultists
                    ? IsMarkerCheckBoxChecked(ShowCultistSpawnsCheckBox)
                    : fallback.CultistSpawns,
                BtrStops = supportsBtr
                    ? IsMarkerCheckBoxChecked(ShowBtrStopsCheckBox)
                    : fallback.BtrStops,
                CustomPins = IsMarkerCheckBoxChecked(ShowCustomPinsCheckBox)
            };
        }

        /// <summary>
        /// Re-applies saved BTR/cultist toggles after map-specific visibility changes (startup or map switch).
        /// </summary>
        private void ApplyMapSpecificMarkerFiltersFromSaved()
        {
            if (!_userSettings.SaveMarkerFilters)
                return;

            MarkerFilterPreferences filters = _userSettings.MarkerFilters ?? new MarkerFilterPreferences();

            _suppressMarkerFilterRefresh = true;

            if (CurrentMapSupportsBtr())
                ShowBtrStopsCheckBox.IsChecked = filters.BtrStops;

            if (CurrentMapSupportsCultists())
                ShowCultistSpawnsCheckBox.IsChecked = filters.CultistSpawns;

            _suppressMarkerFilterRefresh = false;
        }

        private async System.Threading.Tasks.Task SyncCustomPinsToOverlayAsync()
        {
            if (_overlayWindow == null)
                return;

            string pinsJson = JsonSerializer.Serialize(_customPins);
            await _overlayWindow.SetCustomPinsAsync(pinsJson);
        }
        
        private async System.Threading.Tasks.Task SyncQuestFiltersToOverlayAsync()
        {
            if (_overlayWindow == null)
                return;

            WebQuestFilterPayload questFilter = BuildQuestFilterPayload();
            string questFilterJson = JsonSerializer.Serialize(questFilter);
            await _overlayWindow.ApplyQuestFiltersAsync(questFilterJson);
        }

        private bool CurrentMapSupportsBtr()
        {
            if (_currentMapConfig == null || string.IsNullOrWhiteSpace(_currentMapDisplayName))
                return false;

            return MapDataService.MapSupportsBtr(MapDataService.NormalizeMapName(_currentMapDisplayName));
        }

        private bool CurrentMapSupportsCultists()
        {
            if (_currentMapConfig == null || string.IsNullOrWhiteSpace(_currentMapDisplayName))
                return false;

            return _mapData.MapHasCultistSpawns(MapDataService.NormalizeMapName(_currentMapDisplayName));
        }

        private void UpdateBtrControlsVisibility()
        {
            bool supportsBtr = CurrentMapSupportsBtr();
            var visibility = supportsBtr ? Visibility.Visible : Visibility.Collapsed;

            _suppressMarkerFilterRefresh = true;

            ShowBtrStopsCheckBox.Visibility = visibility;

            if (supportsBtr)
            {
                ShowBtrStopsCheckBox.IsChecked = _userSettings.SaveMarkerFilters
                    ? _userSettings.MarkerFilters.BtrStops
                    : ShowBtrStopsCheckBox.IsChecked == true;
            }
            else
            {
                ShowBtrStopsCheckBox.IsChecked = false;
            }

            _suppressMarkerFilterRefresh = false;
        }

        private void UpdateCultistControlsVisibility()
        {
            bool supportsCultists = CurrentMapSupportsCultists();
            var visibility = supportsCultists ? Visibility.Visible : Visibility.Collapsed;

            _suppressMarkerFilterRefresh = true;

            ShowCultistSpawnsCheckBox.Visibility = visibility;

            if (supportsCultists)
            {
                ShowCultistSpawnsCheckBox.IsChecked = _userSettings.SaveMarkerFilters
                    ? _userSettings.MarkerFilters.CultistSpawns
                    : ShowCultistSpawnsCheckBox.IsChecked == true;
            }
            else
            {
                ShowCultistSpawnsCheckBox.IsChecked = false;
            }

            _suppressMarkerFilterRefresh = false;
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
            ShowHazardZonesCheckBox.IsChecked = true;
            ShowSwitchesCheckBox.IsChecked = true;
            ShowValuablesCheckBox.IsChecked = true;
            ShowBattlePassDocumentsCheckBox.IsChecked = true;
            ShowSafesCheckBox.IsChecked = true;
            ShowGroundCachesCheckBox.IsChecked = true;
            ShowLocksCheckBox.IsChecked = true;
            ShowPmcSpawnsCheckBox.IsChecked = true;
            ShowScavSpawnsCheckBox.IsChecked = true;
            ShowBossSpawnsCheckBox.IsChecked = true;

            if (CurrentMapSupportsCultists())
            {
                ShowCultistSpawnsCheckBox.IsChecked = true;
            }

            if (CurrentMapSupportsBtr())
            {
                ShowBtrStopsCheckBox.IsChecked = true;
            }

            _suppressMarkerFilterRefresh = false;
            _ = ApplyMarkerVisibility();
              SaveUserSettings();
        }

        private void HideAllMarkers_Click(object sender, RoutedEventArgs e)
        {
            _suppressMarkerFilterRefresh = true;

            ShowLabelsCheckBox.IsChecked = false;
            ShowQuestItemsCheckBox.IsChecked = false;
            ShowQuestObjectivesCheckBox.IsChecked = false;
            ShowPmcExtractsCheckBox.IsChecked = false;
            ShowScavExtractsCheckBox.IsChecked = false;
            ShowSharedExtractsCheckBox.IsChecked = false;
            ShowTransitsCheckBox.IsChecked = false;
            ShowHazardsCheckBox.IsChecked = false;
            ShowHazardZonesCheckBox.IsChecked = false;
            ShowSwitchesCheckBox.IsChecked = false;
            ShowValuablesCheckBox.IsChecked = false;
            ShowBattlePassDocumentsCheckBox.IsChecked = false;
            ShowSafesCheckBox.IsChecked = false;
            ShowGroundCachesCheckBox.IsChecked = false;
            ShowLocksCheckBox.IsChecked = false;
            ShowPmcSpawnsCheckBox.IsChecked = false;
            ShowScavSpawnsCheckBox.IsChecked = false;
            ShowBossSpawnsCheckBox.IsChecked = false;
            ShowCultistSpawnsCheckBox.IsChecked = false;
            ShowBtrStopsCheckBox.IsChecked = false;
            ShowCustomPinsCheckBox.IsChecked = false;

            _suppressMarkerFilterRefresh = false;
            _ = ApplyMarkerVisibility();
              SaveUserSettings();
        }

        private async void MarkerFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _suppressMarkerFilterRefresh || !AreMarkerFilterControlsReady())
                return;

            await RunSafeAsync(async () =>
            {
                await ApplyMarkerVisibility();
                SaveUserSettings();
            }, "Marker filters");
        }

        private void SaveMarkerFilters_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || SaveMarkerFiltersCheckBox == null)
                return;

            _userSettings.SaveMarkerFilters = SaveMarkerFiltersCheckBox.IsChecked == true;

            if (_userSettings.SaveMarkerFilters)
                SyncMarkerFiltersFromUiIfReady();

              SaveUserSettings();
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
            await RunSafeAsync(async () =>
            {
                if (!_webViewReady)
                    return;

                await MapWebView.ExecuteScriptAsync("resetView();");
                RedrawLastMarker();
                await DrawMapMarkersForCurrentMapAsync();

                if (_overlayWindow != null)
                    await _overlayWindow.ResetViewAsync();
            }, "Reset view");
        }

        private async Task DrawMapMarkersForCurrentMapAsync()
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
                    ExtractCardDetails card = BuildExtractCard(extract, faction);

                    markers.Add(new WebMapMarker
                    {
                        Name = extract.Name,
                        MarkerType = "extract",
                        Faction = faction,
                        ExtractId = extract.Id,
                        CssClass = "extract-" + faction,
                        Tooltip = card.Tooltip,
                        ZoneName = "",
                        Categories = "",
                        Conditions = card.Summary,
                        Keys = card.RequiredItem,
                        Requirements = card.Requirements,
                        ExtractSwitches = card.Switches,
                        WikiUrl = TarkovDevLinks.BuildExtractWikiUrl(_currentMapDisplayName ?? "", extract.Name),
                        TarkovDevUrl = TarkovDevLinks.BuildMapUrl(_currentMapDisplayName ?? ""),
                        Position = $"X={extract.Position.X:0.##}, Y={extract.Position.Y:0.##}, Z={extract.Position.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY,
                        GameY = extract.Position.Y,
                        LinkedSwitchIds = extract.Switches
                            .Select(s => s.Id)
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .ToList()
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
                        NormalizedY = converted.normalizedY,
                        GameY = transit.Position.Y
                    });
                }
            }

            if (_mapData.SpawnsByMapName.TryGetValue(normalizedName, out var spawns))
            {
                Dictionary<string, List<BossSpawnMarker>> bossesByZone = BuildBossMetadataByZone(normalizedName);

                foreach (var spawn in spawns.Where(s => s.Position != null))
                {
                    if (IsBossSpawn(spawn))
                    {
                        string zoneName = spawn.ZoneName ?? string.Empty;

                        if (!bossesByZone.TryGetValue(zoneName, out var bossesAtZone) || bossesAtZone.Count == 0)
                        {
                            if (IsScavBotSpawn(spawn))
                            {
                                var convertedScav = ConvertGameToNormalizedMap(spawn.Position!.X, spawn.Position!.Z);
                                markers.Add(BuildSpawnMarker(spawn, convertedScav, "spawn-scav", "Scav Spawn", "spawn-scav"));
                            }

                            continue;
                        }

                        var converted = ConvertGameToNormalizedMap(spawn.Position!.X, spawn.Position!.Z);

                        var cultistBosses = bossesAtZone
                            .Where(boss => string.Equals(
                                boss.NormalizedName,
                                MapDataService.CultistPriestNormalizedName,
                                StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var otherBosses = bossesAtZone
                            .Where(boss => !string.Equals(
                                boss.NormalizedName,
                                MapDataService.CultistPriestNormalizedName,
                                StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (cultistBosses.Count > 0)
                            markers.Add(BuildCultistSpawnMarker(cultistBosses, converted, spawn));

                        if (otherBosses.Count > 0)
                            markers.Add(BuildBossSpawnMarker(otherBosses, converted, spawn));

                        continue;
                    }

                    var convertedSpawn = ConvertGameToNormalizedMap(spawn.Position!.X, spawn.Position!.Z);

                    string sides = spawn.Sides == null || spawn.Sides.Count == 0
                        ? ""
                        : string.Join(",", spawn.Sides).ToLowerInvariant();

                    bool isPmc = sides.Contains("pmc") || sides.Contains("all");
                    bool isScav = sides.Contains("scav");

                    if (isPmc && !isScav)
                        markers.Add(BuildSpawnMarker(spawn, convertedSpawn, "spawn-pmc", "PMC Spawn", "spawn-pmc"));
                    else if (isScav)
                        markers.Add(BuildSpawnMarker(spawn, convertedSpawn, "spawn-scav", "Scav Spawn", "spawn-scav"));
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
                        GameY = questMarker.Y,
                        QuestCategory = category,
                        QuestObjectiveType = questMarker.ObjectiveType,
                        QuestItem = questMarker.QuestItem,
                        QuestItemShortName = questMarker.ItemShortName,
                        QuestItemIconLink = questMarker.ItemIconLink,
                        QuestTrader = questMarker.Trader,
                        QuestMinPlayerLevel = questMarker.MinPlayerLevel,
                        QuestOptional = questMarker.Optional,
                        QuestSlug = ResolveQuestSlug(questMarker)
                    });
                }
            }


            if (_mapData.HazardsByMapName.TryGetValue(normalizedName, out var hazards))
            {
                foreach (var hazard in hazards.Where(h => h.Position != null))
                {
                    var converted = ConvertGameToNormalizedMap(hazard.Position!.X, hazard.Position.Z);
                    string hazardName = string.IsNullOrWhiteSpace(hazard.Name) ? "Hazard" : hazard.Name;
                    bool hideLabel = IsLandmineHazard(hazardName, hazard.HazardType);

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
                        NormalizedY = converted.normalizedY,
                        GameY = hazard.Position.Y,
                        HideLabel = hideLabel,
                        Outline = BuildNormalizedOutline(hazard.Outline)
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
                        NormalizedY = converted.normalizedY,
                        GameY = mapSwitch.Position.Y,
                        SwitchId = mapSwitch.Id
                    });
                }
            }

            if (_mapData.TrackedLootByMapName.TryGetValue(normalizedName, out var lootPoints))
            {
                foreach (var loot in lootPoints.Where(p => p.Position != null))
                {
                    var converted = ConvertGameToNormalizedMap(loot.Position!.X, loot.Position.Z);
                    string kind = (loot.Kind ?? "").Trim().ToLowerInvariant();
                    string markerType = kind switch
                    {
                        "valuables" => "loot-valuables",
                        "battle-pass" => "loot-battle-pass",
                        "safe" => "loot-safe",
                        "ground-cache" => "loot-ground-cache",
                        "lock" => "loot-lock",
                        _ => ""
                    };
                    if (string.IsNullOrWhiteSpace(markerType))
                        continue;

                    string tooltip = loot.Name;
                    if (kind == "lock")
                    {
                        tooltip = string.IsNullOrWhiteSpace(loot.KeyName)
                            ? "No key required"
                            : "Key required\n" + loot.KeyName;
                        if (!string.IsNullOrWhiteSpace(loot.LockType))
                            tooltip += "\n" + loot.LockType;
                        if (loot.NeedsPower)
                            tooltip += "\nRequires power";
                    }
                    else if (kind == "safe")
                    {
                        tooltip = loot.Name;
                        if (!string.IsNullOrWhiteSpace(loot.KeyName))
                            tooltip += "\nKey required\n" + loot.KeyName;
                        if (loot.NeedsPower)
                            tooltip += "\nRequires power";
                    }
                    else if (kind == "valuables")
                    {
                        tooltip = "Valuables";
                        if (loot.Items is { Count: > 0 })
                            tooltip += "\n" + string.Join("\n", loot.Items);
                    }
                    else if (kind == "battle-pass")
                    {
                        tooltip = "Battle Pass documents";
                        if (loot.Items is { Count: > 0 })
                            tooltip += "\n" + string.Join("\n", loot.Items);
                    }
                    else
                    {
                        if (loot.Items is { Count: > 0 })
                            tooltip += "\n" + string.Join(", ", loot.Items.Take(8));
                        if (!string.IsNullOrWhiteSpace(loot.KeyName))
                            tooltip += "\nKey: " + loot.KeyName;
                        if (loot.NeedsPower)
                            tooltip += "\nRequires power";
                    }

                    markers.Add(new WebMapMarker
                    {
                        Name = loot.Name,
                        MarkerType = markerType,
                        Faction = "",
                        CssClass = markerType,
                        Tooltip = tooltip,
                        ZoneName = loot.KeyName ?? "",
                        Categories = kind,
                        Conditions = loot.LockType ?? "",
                        Keys = loot.KeyName ?? "",
                        Position = $"X={loot.Position.X:0.##}, Y={loot.Position.Y:0.##}, Z={loot.Position.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY,
                        GameY = loot.Position.Y,
                        HideLabel = true
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
                        GameY = GetLabelGameY(label),
                        LabelRotation = label.Rotation,
                        LabelSize = GetLabelFontSize(label.Size)
                    });
                }
            }

            if (_mapData.BtrByMapName.TryGetValue(normalizedName, out var btrConfig))
            {
                foreach (var stop in btrConfig.Stops)
                {
                    var converted = ConvertGameToNormalizedMap(stop.X, stop.Z);

                    markers.Add(new WebMapMarker
                    {
                        Name = stop.Name,
                        MarkerType = "btr-stop",
                        Faction = "",
                        CssClass = "btr-stop",
                        Tooltip = $"BTR Stop: {stop.Name}",
                        ZoneName = "",
                        Categories = "BTR Stop",
                        Conditions = "",
                        Position = $"X={stop.X:0.##}, Y={stop.Y:0.##}, Z={stop.Z:0.##}",
                        NormalizedX = converted.normalizedX,
                        NormalizedY = converted.normalizedY,
                        GameY = stop.Y
                    });
                }
            }

            string json = JsonSerializer.Serialize(markers);
            _lastMapMarkersJson = json;

            await MapWebView.ExecuteScriptAsync($"addMapMarkers({json});");

            if (_overlayWindow != null)
                await _overlayWindow.SetMapMarkersAsync(json);

            SetParserStatus($"LOADED {markers.Count} MARKERS");
        }

        private void ApplyAutoFloorFromPlayerHeight(double gameY)
        {
            if (!_userSettings.AutoSelectFloorFromPlayerHeight)
                return;

            if (_currentMapLevelsConfig == null || _currentMapLevelsConfig.Levels.Count == 0)
                return;

            if (LayersPanel.Children.Count == 0)
                return;

            string? matchingLayer = null;

            // Prefer upper floors (higher minHeight) when bands are adjacent; underground layers use low maxHeight.
            var orderedLevels = _currentMapLevelsConfig.Levels
                .Where(level => !string.IsNullOrWhiteSpace(level.SvgLayer)
                                && level.MinHeight.HasValue
                                && level.MaxHeight.HasValue)
                .OrderByDescending(level => level.MinHeight!.Value)
                .ThenBy(level => level.MaxHeight!.Value);

            foreach (var level in orderedLevels)
            {
                if (gameY >= level.MinHeight!.Value && gameY < level.MaxHeight!.Value)
                {
                    matchingLayer = level.SvgLayer;
                    break;
                }
            }

            bool exclusive = _currentMapLevelsConfig.ExclusiveLayers;
            bool showBase = matchingLayer == null;

            _suppressMapLevelRefresh = true;

            foreach (var child in LayersPanel.Children)
            {
                if (child is not CheckBox checkBox)
                    continue;

                if (IsBaseLayerCheckbox(checkBox))
                {
                    // Exclusive floor maps (Icebreaker): show base only when no overlay height matches.
                    // Other maps keep the base layer visible while auto-toggling overlays.
                    checkBox.IsChecked = exclusive ? showBase : true;
                    continue;
                }

                checkBox.IsChecked = checkBox.Tag is string svgLayer &&
                                     !string.IsNullOrWhiteSpace(matchingLayer) &&
                                     string.Equals(svgLayer, matchingLayer, StringComparison.OrdinalIgnoreCase);
            }

            _suppressMapLevelRefresh = false;
            _ = ApplyMapLevelStateAsync();
        }

        private void PopulateQuestFilterOptions()
        {
            if (QuestNameFilterComboBox == null || QuestTraderFilterComboBox == null)
                return;

            _suppressQuestFilterRefresh = true;

            try
            {
                HashSet<string> currentQuests = QuestNameFilterComboBox.Items
                    .OfType<QuestFilterItem>()
                    .Where(static item => item.IsSelected && !string.IsNullOrWhiteSpace(item.Name))
                    .Select(static item => item.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                string? currentTrader = (QuestTraderFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

                QuestNameFilterComboBox.Items.Clear();
                QuestTraderFilterComboBox.Items.Clear();
                QuestTraderFilterComboBox.Items.Add(new ComboBoxItem { Content = "All", Tag = "all" });

                if (string.IsNullOrWhiteSpace(_currentMapDisplayName))
                {
                    QuestTraderFilterComboBox.SelectedIndex = 0;
                    SetQuestFilterSummaryText([]);
                    return;
                }

                string normalizedName = MapDataService.NormalizeMapName(_currentMapDisplayName);

                var questNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var traders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var traderQuests = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (_mapData.QuestMarkersByMapName.TryGetValue(normalizedName, out List<QuestMarker>? questMarkers) &&
                    questMarkers != null)
                {
                    foreach (QuestMarker questMarker in questMarkers)
                    {
                        if (!string.IsNullOrWhiteSpace(questMarker.Quest))
                        {
                            string questName = questMarker.Quest.Trim();
                            questNames.Add(questName);
                            traderQuests[questName] = questMarker.Trader?.Trim() ?? string.Empty;
                        }

                        if (!string.IsNullOrWhiteSpace(questMarker.Trader))
                            traders.Add(questMarker.Trader.Trim());
                    }
                }

                foreach (string questName in questNames)
                {
                    QuestNameFilterComboBox.Items.Add(new QuestFilterItem
                    {
                        Name = questName,
                        Trader = traderQuests.TryGetValue(questName, out string? value) ? value : string.Empty,
                        IsSelected = currentQuests.Contains(questName)
                    });
                }

                foreach (string trader in traders)
                    QuestTraderFilterComboBox.Items.Add(new ComboBoxItem { Content = trader, Tag = trader });

                bool traderRestored = false;
                if (!string.IsNullOrWhiteSpace(currentTrader))
                {
                    foreach (ComboBoxItem item in QuestTraderFilterComboBox.Items.OfType<ComboBoxItem>())
                    {
                        if (!string.Equals(item.Content?.ToString(), currentTrader, StringComparison.OrdinalIgnoreCase))
                            continue;

                        QuestTraderFilterComboBox.SelectedItem = item;
                        traderRestored = true;
                        break;
                    }
                }

                if (!traderRestored)
                    QuestTraderFilterComboBox.SelectedIndex = 0;

                SetQuestFilterSummaryText(
                    QuestNameFilterComboBox.Items
                        .OfType<QuestFilterItem>()
                        .Where(static item => item.IsSelected)
                        .Select(static item => item.Name)
                        .ToList());
            }
            finally
            {
                _suppressQuestFilterRefresh = false;
            }
        }

        private WebQuestFilterPayload BuildQuestFilterPayload()
        {
            string trader = "all";

            if (QuestTraderFilterComboBox?.SelectedItem is ComboBoxItem traderItem)
            {
                trader = traderItem.Tag?.ToString()
                    ?? traderItem.Content?.ToString()
                    ?? "all";
            }

            List<string> selectedQuests = QuestNameFilterComboBox?.Items
                .OfType<QuestFilterItem>()
                .Where(static item => item.IsSelected && !string.IsNullOrWhiteSpace(item.Name))
                .Select(static item => item.Name)
                .ToList() ?? new List<string>();

            return new WebQuestFilterPayload
            {
                QuestNames = selectedQuests,
                Trader = trader
            };
        }

        private async System.Threading.Tasks.Task ApplySearchAndQuestFiltersAsync()
        {
            if (!_webViewReady)
                return;

            string searchQuery = MarkerSearchTextBox?.Text ?? string.Empty;
            string searchJson = JsonSerializer.Serialize(searchQuery);
            WebQuestFilterPayload questFilter = BuildQuestFilterPayload();
            string questFilterJson = JsonSerializer.Serialize(questFilter);

            await MapWebView.ExecuteScriptAsync(
                $"applyMarkerSearch({searchJson}); applyQuestFilters({questFilterJson});");

            if (_overlayWindow != null)
            {
                await _overlayWindow.ApplyMarkerSearchAsync(searchJson);
                await _overlayWindow.ApplyQuestFiltersAsync(questFilterJson);
            }
        }

        private async System.Threading.Tasks.Task ApplyShowQuestNamesAsync()
        {
            if (!_webViewReady)
                return;

            bool showQuestNames = ShowQuestNamesCheckBox?.IsChecked == true;
            string visibility = showQuestNames ? "true" : "false";
            await MapWebView.ExecuteScriptAsync($"setQuestNamesVisibility({visibility});");

            if (_overlayWindow != null)
                await _overlayWindow.ApplyShowQuestNamesAsync(showQuestNames);
        }

        private async void MarkerSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
                return;

            await RunSafeAsync(ApplySearchAndQuestFiltersAsync, "Marker search");
        }

        private async void QuestFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _suppressQuestFilterRefresh || QuestNameFilterComboBox == null)
                return;

            // 1. Prevent WPF from trying to render a single item in the closed header
            QuestNameFilterComboBox.SelectedItem = null;

            // 2. Read the checked status directly from the ComboBox items
            var selectedQuests = QuestNameFilterComboBox.Items
                .OfType<QuestFilterItem>()
                .Where(q => q.IsSelected)
                .Select(q => q.Name)
                .ToList();

            // 3. Format the summary string
            SetQuestFilterSummaryText(selectedQuests);

            await RunSafeAsync(ApplySearchAndQuestFiltersAsync, "Quest filters");
        }

        private void SetQuestFilterSummaryText(List<string> selectedQuests)
        {
            string summaryText;
            if (selectedQuests.Count == 0)
            {
                summaryText = "All Quests";
            }
            else if (selectedQuests.Count == 1)
            {
                summaryText = selectedQuests[0];
            }
            else if (selectedQuests.Count <= 3)
            {
                summaryText = string.Join(", ", selectedQuests);
            }
            else
            {
                summaryText = $"{selectedQuests.Count} Quests Selected";
            }

            // Store in Tag for the XAML TextBlock overlay
            QuestNameFilterComboBox.SelectedItem = null;
            QuestNameFilterComboBox.Tag = summaryText;
        }

        private void AutoSelectFloorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || AutoSelectFloorCheckBox == null)
                return;

            try
            {
                _userSettings.AutoSelectFloorFromPlayerHeight = AutoSelectFloorCheckBox.IsChecked == true;
                SaveUserSettings();

                if (_lastGameY.HasValue)
                    ApplyAutoFloorFromPlayerHeight(_lastGameY.Value);
            }
            catch (Exception ex)
            {
                SetParserStatus($"FLOOR AUTO-SELECT FAILED: {ex.Message}", isError: true);
            }
        }

        private async void ClearSearchFilters_Click(object sender, RoutedEventArgs e)
        {
            await RunSafeAsync(async () =>
            {
                _suppressQuestFilterRefresh = true;

                if (MarkerSearchTextBox != null)
                    MarkerSearchTextBox.Text = string.Empty;

                if (QuestNameFilterComboBox != null)
                {
                    foreach (var item in QuestNameFilterComboBox.Items.OfType<QuestFilterItem>())
                    {
                        item.IsSelected = false;
                    }
                    QuestNameFilterComboBox.SelectedIndex = -1;
                    SetQuestFilterSummaryText([]);
                }

                if (QuestTraderFilterComboBox != null)
                    QuestTraderFilterComboBox.SelectedIndex = 0;

                _suppressQuestFilterRefresh = false;

                await ApplySearchAndQuestFiltersAsync();
            }, "Clear search filters");
        }

        private async void ShowQuestNamesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || ShowQuestNamesCheckBox == null)
                return;

            _userSettings.ShowQuestNames = ShowQuestNamesCheckBox.IsChecked == true;
            SaveUserSettings();

            await ApplyShowQuestNamesAsync(); 
        }

        private static bool IsLandmineHazard(string hazardName, string? hazardType)
        {
            if (string.Equals(hazardType, "minefield", StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(hazardName, "Landmine", StringComparison.OrdinalIgnoreCase);
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
                return 8;

            return Math.Max(7, Math.Min(13, size / 8.0));
        }

        private static double? GetLabelGameY(MapLabel label)
        {
            if (label.Bottom.HasValue && label.Top.HasValue)
                return (label.Bottom.Value + label.Top.Value) / 2.0;

            return label.Bottom ?? label.Top;
        }

        private sealed class ExtractCardDetails
        {
            public string RequiredItem { get; init; } = "";
            public List<string> Switches { get; init; } = new();
            public List<string> Requirements { get; init; } = new();
            public string Summary { get; init; } = "None";
            public string Tooltip { get; init; } = "";
        }

        private ExtractCardDetails BuildExtractCard(ExtractInfo extract, string faction)
        {
            string factionLabel = string.IsNullOrWhiteSpace(faction)
                ? "SHARED"
                : faction.ToUpperInvariant();

            string requiredItem = FormatRequiredItem(extract.TransferItem);
            var switches = (extract.Switches ?? new List<ExtractSwitch>())
                .Select(sw => string.IsNullOrWhiteSpace(sw.Name) ? sw.Id : sw.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var requirements = new List<string>();
            if (!string.IsNullOrWhiteSpace(extract.Conditions))
                requirements.Add(extract.Conditions.Trim());

            foreach (string requirement in extract.Requirements ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(requirement))
                    continue;
                if (IsRedundantExtractRequirement(requirement, requiredItem, switches))
                    continue;
                requirements.Add(requirement.Trim());
            }

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(requiredItem))
                summaryParts.Add("Item: " + requiredItem);
            if (switches.Count > 0)
                summaryParts.Add("Switch: " + string.Join(", ", switches));
            summaryParts.AddRange(requirements);

            string summary = summaryParts.Count == 0 ? "None" : string.Join("; ", summaryParts.Distinct());

            var tooltipLines = new List<string>
            {
                $"{extract.Name} ({factionLabel} extract)",
                "Faction: " + factionLabel,
                "Required item: " + (string.IsNullOrWhiteSpace(requiredItem) ? "None" : requiredItem)
            };
            tooltipLines.Add("Switches: " + (switches.Count == 0 ? "None" : string.Join(", ", switches)));
            tooltipLines.Add("Requirements: " + (requirements.Count == 0 ? "None" : string.Join("; ", requirements)));

            return new ExtractCardDetails
            {
                RequiredItem = requiredItem,
                Switches = switches,
                Requirements = requirements,
                Summary = summary,
                Tooltip = string.Join('\n', tooltipLines)
            };
        }

        private static string FormatRequiredItem(ExtractTransferItem? transferItem)
        {
            string? name = transferItem?.Item?.Name;
            if (string.IsNullOrWhiteSpace(name))
                return "";

            double count = transferItem!.Count > 0 ? transferItem.Count : transferItem.Quantity;
            return count > 1 ? $"{name} ×{count:0}" : name;
        }

        private static bool IsRedundantExtractRequirement(
            string requirement,
            string requiredItem,
            List<string> switches)
        {
            if (!string.IsNullOrWhiteSpace(requiredItem) &&
                requirement.Contains(requiredItem, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return switches.Any(name =>
                requirement.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> BuildSelectedMarkerDetails(WebMarkerClickMessage marker)
        {
            var details = new List<string>();
            bool isExtract = string.Equals(marker.MarkerType, "extract", StringComparison.OrdinalIgnoreCase);

            if (isExtract)
            {
                details.Add("Faction: " + FormatFactionLabel(marker.Faction));
                details.Add("Required item / key: " +
                    (string.IsNullOrWhiteSpace(marker.Keys) ? "None" : marker.Keys));
                details.Add("Switches: " +
                    (marker.ExtractSwitches is { Count: > 0 }
                        ? string.Join(", ", marker.ExtractSwitches)
                        : "None"));
                details.Add("Requirements: " +
                    (marker.Requirements is { Count: > 0 }
                        ? string.Join("; ", marker.Requirements)
                        : "None"));
            }
            else
            {
                details.Add($"Type: {marker.MarkerType}");

                if (!string.IsNullOrWhiteSpace(marker.Faction))
                    details.Add($"Faction/Side: {marker.Faction}");

                if (!string.IsNullOrWhiteSpace(marker.ZoneName))
                    details.Add($"Zone: {marker.ZoneName}");

                if (!string.IsNullOrWhiteSpace(marker.Categories))
                    details.Add($"Categories: {marker.Categories}");

                if (!string.IsNullOrWhiteSpace(marker.Conditions))
                    details.Add($"Conditions: {marker.Conditions}");
            }

            if (!string.IsNullOrWhiteSpace(marker.Position))
                details.Add($"Position: {marker.Position}");

            if (isExtract && marker.LinkedSwitchIds is { Count: > 0 })
                details.Add("Linked switches highlighted on map");

            return details;
        }

        private static string FormatFactionLabel(string faction)
        {
            string normalized = (faction ?? "").Trim().ToLowerInvariant();
            return normalized switch
            {
                "pmc" => "PMC",
                "scav" => "Scav",
                "shared" => "Shared",
                _ => string.IsNullOrWhiteSpace(faction) ? "Shared" : faction.ToUpperInvariant()
            };
        }

        private string? FirstAllowedUrl(params string?[] urls)
        {
            foreach (string? url in urls)
            {
                if (WebViewSecurity.IsAllowedHttpsUrl(url, AllowedExternalBrowserHosts))
                    return url;
            }

            return null;
        }

        private Dictionary<string, List<BossSpawnMarker>> BuildBossMetadataByZone(string normalizedMapName)
        {
            var lookup = new Dictionary<string, List<BossSpawnMarker>>(StringComparer.OrdinalIgnoreCase);

            if (!_mapData.BossSpawnMarkersByMapName.TryGetValue(normalizedMapName, out var bossMeta))
                return lookup;

            foreach (BossSpawnMarker boss in bossMeta)
            {
                if (string.IsNullOrWhiteSpace(boss.ZoneName))
                    continue;

                if (!lookup.TryGetValue(boss.ZoneName, out List<BossSpawnMarker>? bossesAtZone))
                {
                    bossesAtZone = new List<BossSpawnMarker>();
                    lookup[boss.ZoneName] = bossesAtZone;
                }

                if (bossesAtZone.Any(existing =>
                        string.Equals(existing.BossName, boss.BossName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                bossesAtZone.Add(boss);
            }

            return lookup;
        }

        private WebMapMarker BuildCultistSpawnMarker(
            List<BossSpawnMarker> cultistsAtLocation,
            (double normalizedX, double normalizedY) converted,
            SpawnInfo spawn)
        {
            var primary = cultistsAtLocation[0];
            string locations = string.Join(", ", cultistsAtLocation.Select(b => b.LocationName).Distinct());

            string chanceText = cultistsAtLocation.Count == 1 && primary.SpawnChance > 0
                ? $"\nSpawn chance: {primary.SpawnChance:P0}"
                : "";

            return new WebMapMarker
            {
                Name = "Cultist Priest",
                MarkerType = "spawn-cultist",
                Faction = "",
                CssClass = "spawn-cultist",
                Tooltip = $"Cultist Priest\n{locations}\nZone: {spawn.ZoneName}{chanceText}",
                ZoneName = spawn.ZoneName ?? primary.ZoneName,
                Categories = "cultist",
                Conditions = "",
                Position = $"X={spawn.Position!.X:0.##}, Y={spawn.Position.Y:0.##}, Z={spawn.Position.Z:0.##}",
                NormalizedX = converted.normalizedX,
                NormalizedY = converted.normalizedY,
                GameY = spawn.Position.Y
            };
        }

        private WebMapMarker BuildBossSpawnMarker(
            List<BossSpawnMarker> bossesAtLocation,
            (double normalizedX, double normalizedY) converted,
            SpawnInfo spawn)
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
                Tooltip = $"{bossNames}\n{locations}\nZone: {spawn.ZoneName}{chanceText}",
                ZoneName = spawn.ZoneName ?? primary.ZoneName,
                Categories = "boss",
                Conditions = "",
                Position = $"X={spawn.Position!.X:0.##}, Y={spawn.Position.Y:0.##}, Z={spawn.Position.Z:0.##}",
                NormalizedX = converted.normalizedX,
                NormalizedY = converted.normalizedY,
                GameY = spawn.Position.Y
            };
        }

        private static bool IsBossSpawn(SpawnInfo spawn) =>
            spawn.Categories?.Any(category =>
                string.Equals(category, "boss", StringComparison.OrdinalIgnoreCase)) == true;

        private static bool IsScavBotSpawn(SpawnInfo spawn)
        {
            bool isScav = spawn.Sides?.Any(side =>
                string.Equals(side, "scav", StringComparison.OrdinalIgnoreCase)) == true;

            if (!isScav)
                return false;

            return spawn.Categories?.Any(category =>
                string.Equals(category, "bot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "all", StringComparison.OrdinalIgnoreCase)) == true;
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
                NormalizedY = converted.normalizedY,
                GameY = spawn.Position.Y
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

        private void UpdateScreenshotMonitoringStatus()
        {
            _screenshotWatcher?.Dispose();
            _screenshotWatcher = null;

            if (!_userSettings.ScreenshotParsingEnabled)
            {
                SetParserStatus("PARSING DISABLED");
                return;
            }

            StartScreenshotMonitoring();
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
            if (!_userSettings.ScreenshotParsingEnabled)
                return;

            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(500);

                var file = new FileInfo(e.FullPath);

                if (!file.Exists)
                    return;

                if (file.CreationTime <= _lastProcessedTime)
                    return;

                _lastProcessedTime = file.CreationTime;

                await ProcessScreenshotAsync(file.FullName);
            });
        }

        private void ReadLatestScreenshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_userSettings.ScreenshotParsingEnabled)
                {
                    MessageBox.Show(
                        "Screenshot parsing is disabled. Enable it in Settings to use Read Latest Screenshot.",
                        "Parsing Disabled",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

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
                _ = ProcessScreenshotAsync(newestFile.FullName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async System.Threading.Tasks.Task ProcessScreenshotAsync(string filePath)
        {
            if (!_userSettings.ScreenshotParsingEnabled)
                return;

            ParseScreenshotFilename(Path.GetFileName(filePath));

            try
            {
                (int configuredWidth, int configuredHeight) = GetConfiguredGameResolution();
                ScreenshotExfilParseResult parseResult = await _screenshotExfilParser.ParseAsync(
                    filePath,
                    configuredWidth,
                    configuredHeight);

                UpdateDetectedResolutionUi(parseResult, configuredWidth, configuredHeight);

                if (!parseResult.PanelDetected)
                    return;

                if (_currentMapConfig == null || string.IsNullOrWhiteSpace(_currentMapDisplayName))
                {
                    SetParserStatus("EXFIL PANEL FOUND - SELECT MAP", isError: true);
                    return;
                }

                string normalizedMapName = MapDataService.NormalizeMapName(_currentMapDisplayName);
                RaidExfilMatchResult matchResult = RaidExfilMatcher.Match(
                    normalizedMapName,
                    parseResult.ExfilNames,
                    parseResult.TransitNames,
                    _mapData,
                    parseResult.RawOcrText);

                if (matchResult.ExtractMatches.Count == 0 && matchResult.TransitNames.Count == 0)
                {
                    SetParserStatus("EXFIL PANEL FOUND - NO MATCHES", isError: true);
                    UpdateRaidExfilUi();
                    return;
                }

                _raidExfilHighlights.Activate(matchResult.ExtractMatches, matchResult.TransitNames);
                UpdateRaidExfilUi();
                await ApplyRaidExfilHighlightsAsync();

                SetParserStatus(
                    $"RAID EXFILS: {matchResult.ExtractMatches.Count} EXT / {matchResult.TransitNames.Count} TRANSIT " +
                    $"(OCR {parseResult.ExfilNames.Count}/{parseResult.TransitNames.Count})");
            }
            catch (Exception ex)
            {
                SetParserStatus($"EXFIL SCAN FAILED: {ex.Message}", isError: true);
            }
        }

        private void UpdateDetectedResolutionUi(
            ScreenshotExfilParseResult parseResult,
            int configuredWidth,
            int configuredHeight)
        {
            if (parseResult.ImageWidth <= 0 || parseResult.ImageHeight <= 0)
            {
                DetectedResolutionText.Text = "\u2014";
                return;
            }

            string detected = $"{parseResult.ImageWidth} x {parseResult.ImageHeight}";
            if (!string.IsNullOrWhiteSpace(parseResult.ResolutionProfile))
                detected += $" ({parseResult.ResolutionProfile})";

            if (configuredWidth > 0 && configuredHeight > 0)
                detected += $" | TUNING {configuredWidth}x{configuredHeight}";

            DetectedResolutionText.Text = detected;
        }

        private void ClearRaidExfilHighlightsForMapChange()
        {
            _raidExfilHighlights.Clear();
            UpdateRaidExfilUi();
            _ = ApplyRaidExfilHighlightsAsync();
        }

        private void UpdateRaidExfilUi()
        {
            if (!_raidExfilHighlights.Active)
            {
                RaidExfilStatusText.Text = "NONE DETECTED";
                RaidExfilStatusText.Foreground = (Brush)FindResource("TacticalTextMutedBrush");
                RaidExfilExtractsText.Text = "\u2014";
                RaidExfilTransitsText.Text = "\u2014";
                return;
            }

            RaidExfilStatusText.Text = "ACTIVE FOR THIS MAP";
            RaidExfilStatusText.Foreground = (Brush)FindResource("TacticalTerminalGreenBrush");
            RaidExfilExtractsText.Text = _raidExfilHighlights.ExtractMatches.Count == 0
                ? "\u2014"
                : string.Join(Environment.NewLine, _raidExfilHighlights.ExtractDisplayNames);
            RaidExfilTransitsText.Text = _raidExfilHighlights.TransitNames.Count == 0
                ? "\u2014"
                : string.Join(Environment.NewLine, _raidExfilHighlights.TransitNames);
        }

        private async System.Threading.Tasks.Task ApplyRaidExfilHighlightsAsync()
        {
            string payloadJson = JsonSerializer.Serialize(_raidExfilHighlights.ToPayload());
            _lastRaidExfilHighlightsJson = payloadJson;

            if (_webViewReady)
                await MapWebView.ExecuteScriptAsync($"setRaidExfilHighlights({payloadJson});");

            if (_overlayWindow != null)
                await _overlayWindow.ApplyRaidExfilHighlightsAsync(payloadJson);
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

            _lastGameX = gameX;
            _lastGameY = gameY;
            _lastGameZ = gameZ;
            _lastDirection = direction;

            double mapDirection = ApplyMapDirectionOffset(direction);

            GameXText.Text = gameX.ToString("0.00", CultureInfo.InvariantCulture);
            GameYText.Text = gameY.ToString("0.00", CultureInfo.InvariantCulture);
            GameZText.Text = gameZ.ToString("0.00", CultureInfo.InvariantCulture);
            DirectionText.Text = mapDirection.ToString("0.00", CultureInfo.InvariantCulture) + "\u00B0";

            SetParserStatus($"TRACKING {DateTime.Now:T}");

            StatusText.Text = $"X={gameX:0.00} Y={gameY:0.00} Z={gameZ:0.00} DIR={mapDirection:0.00}";

            DrawPlayerMarkerFromGameCoordinates(gameX, gameZ, direction, _userSettings.OverlayCenterOnPlayer);
            ApplyAutoFloorFromPlayerHeight(gameY);
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
            // Screenshot yaw is Unity heading (0 = +Z). map-view.js adds 180° when rotating the
            // chevron, which matches maps whose SVG uses coordinateRotation 180 (Customs, Woods,
            // etc.). Factory is 90 and Labs/Labyrinth are 270, so rotate heading by
            // (180 - coordinateRotation) to match the same 2D rotation applied to X/Z.
            int coordinateRotation = _currentMapConfig?.Svg?.CoordinateRotation ?? 180;
            direction += 180 - coordinateRotation;

            while (direction > 180) direction -= 360;
            while (direction < -180) direction += 360;

            return direction;
        }

        private void DrawPlayerMarkerFromGameCoordinates(
            double gameX,
            double gameZ,
            double directionDegrees,
            bool centerOverlayOnPlayer = false)
        {
            var converted = ConvertGameToNormalizedMap(gameX, gameZ);

            _ = SetPlayerMarkerInWebViewAsync(
                converted.normalizedX,
                converted.normalizedY,
                ApplyMapDirectionOffset(directionDegrees),
                centerOverlayOnPlayer);
        }

        private async Task SetPlayerMarkerInWebViewAsync(
            double normalizedX,
            double normalizedY,
            double directionDegrees,
            bool centerOverlayOnPlayer = false)
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
            {
                await _overlayWindow.SetPlayerMarkerAsync(
                    normalizedX,
                    normalizedY,
                    directionDegrees,
                    centerOverlayOnPlayer);
            }
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

        private async void OpenOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSafeAsync(async () =>
            {
                if (_overlayWindow == null)
                {
                    _overlayWindow = new OverlayWindow(_userSettings.OverlaySettings);
                    _overlayWindow.Closed += (_, _) => _overlayWindow = null;
                    _overlayWindow.Show();
                }
                else
                {
                    _overlayWindow.Activate();
                }

                await SyncOverlayToCurrentMapAsync();
            }, "Open overlay");
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

            if (!string.IsNullOrWhiteSpace(_lastRaidExfilHighlightsJson))
                await _overlayWindow.ApplyRaidExfilHighlightsAsync(_lastRaidExfilHighlightsJson);

            if (_lastPlayerMarker != null)
            {
                await _overlayWindow.SetPlayerMarkerAsync(
                    _lastPlayerMarker.Value.NormalizedX,
                    _lastPlayerMarker.Value.NormalizedY,
                    _lastPlayerMarker.Value.DirectionDegrees);
            }

            await SyncOverlayLayersAndFiltersAsync();
            await SyncCustomPinsToOverlayAsync();
            await SyncQuestFiltersToOverlayAsync();
        }

        private static string ResolveQuestSlug(QuestMarker questMarker)
        {
            if (!string.IsNullOrWhiteSpace(questMarker.QuestSlug))
                return questMarker.QuestSlug;

            return TarkovDevLinks.ToTaskSlug(questMarker.Quest);
        }

        private async Task HighlightLinkedSwitchesAsync(IReadOnlyList<string> switchIds)
        {
            if (switchIds == null || switchIds.Count == 0)
                return;

            string idsJson = JsonSerializer.Serialize(switchIds);

            if (_webViewReady)
                await MapWebView.ExecuteScriptAsync($"highlightLinkedSwitches({idsJson});");

            if (_overlayWindow != null)
                await _overlayWindow.HighlightLinkedSwitchesAsync(idsJson);
        }

        private async Task ClearLinkedSwitchHighlightsAsync()
        {
            if (_webViewReady)
                await MapWebView.ExecuteScriptAsync("clearLinkedSwitchHighlights();");

            if (_overlayWindow != null)
                await _overlayWindow.ClearLinkedSwitchHighlightsAsync();
        }

        private static async Task RunSafeAsync(Func<Task> action, string operationName)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{operationName} failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenQuestTarkovDevButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrlInBrowser(_lastQuestTarkovDevUrl);
        }

        private void OpenQuestWikiButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrlInBrowser(_lastQuestWikiUrl);
        }

        private static void OpenUrlInBrowser(string? url)
        {
            if (!WebViewSecurity.IsAllowedHttpsUrl(url, AllowedExternalBrowserHosts) || url == null)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open link:\n{ex.Message}", "Open Link Failed");
            }
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
            await ApplyRaidExfilHighlightsAsync(); 
        }

        protected override void OnClosed(EventArgs e)
        {
            _overlayWindow?.Close();
            _overlayWindow = null;

            _globalHotKeyManager?.Dispose();
            _globalHotKeyManager = null;

            base.OnClosed(e);
        }
    }
}
