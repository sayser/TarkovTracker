using Microsoft.Web.WebView2.Core;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TarkovTracker.Services;

namespace TarkovTracker
{
    public partial class OverlayWindow : Window
    {
        private bool _webViewReady = false;
        private bool _mapAssetHostMapped = false;
        private bool _webViewHardened = false;
        private string? _mapsFolder;
        private string? _pendingMarkersJson;
        private string? _pendingRaidExfilHighlightsJson;
        private string? _pendingMapLevelStateJson;
        private string? _pendingMarkerFiltersJson;
        private string? _pendingCustomPinsJson;
        private string? _pendingQuestFilterJson;
        private string? _pendingMarkerSearchJson;
        private bool? _pendingShowQuestNames;
        private (double NormalizedX, double NormalizedY, double DirectionDegrees)? _pendingPlayerMarker;
        private bool _suppressOpacitySliderRefresh;
        private bool _overlayBackgroundHidden;
        private readonly OverlaySettings _overlaySettings;

        public OverlayWindow(OverlaySettings overlaySettings)
        {
            InitializeComponent();

            _overlaySettings = overlaySettings;

            Width = _overlaySettings.OverlayWidth;
            Height = _overlaySettings.OverlayHeight;

            if (_overlaySettings.OverlayLeft > 0 && _overlaySettings.OverlayTop > 0)
            {
                 Left = _overlaySettings.OverlayLeft;
                Top = _overlaySettings.OverlayTop;   
            }            

            ApplyDefaultOpacityPercent(overlaySettings.OverlayDefaultOpacityPercent);

            Loaded += async (_, _) =>
            {
                await ApplyOverlayOpacityAsync();
            };

            SizeChanged += OverlayWindow_SizeChanged;
            LocationChanged += OverlayWindow_LocationChanged;
        }

        private void OverlayWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                return;

            _overlaySettings.OverlayLeft = Left;
            _overlaySettings.OverlayTop = Top;
        }

        private void OverlayWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Don't update settings if the window is minimized
            if (WindowState == WindowState.Minimized)
                return;

            // Save new dimensions
            _overlaySettings.OverlayWidth = e.NewSize.Width;
            _overlaySettings.OverlayHeight = e.NewSize.Height;
        }

        public void ApplyDefaultOpacityPercent(double percent)
        {
            percent = Math.Clamp(percent, 20, 100);

            _suppressOpacitySliderRefresh = true;
            OpacitySlider.Value = percent;
            _suppressOpacitySliderRefresh = false;

            _ = ApplyOverlayOpacityAsync();
        }

        public void ConfigureMapAssetHost(string mapsFolder)
        {
            if (string.IsNullOrWhiteSpace(mapsFolder))
                return;

            _mapsFolder = mapsFolder;
        }

        private async Task EnsureMapAssetHostMappingAsync()
        {
            await OverlayMapView.EnsureCoreWebView2Async();
            WebViewSecurity.ApplyOnce(OverlayMapView.CoreWebView2, MainWindow.MapAssetHostName, ref _webViewHardened);

            if (_mapAssetHostMapped || string.IsNullOrWhiteSpace(_mapsFolder))
                return;

            OverlayMapView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                MainWindow.MapAssetHostName,
                _mapsFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            _mapAssetHostMapped = true;
        }

        public async Task LoadMapHtmlAsync(string html)
        {
            _webViewReady = false;

            await EnsureMapAssetHostMappingAsync();

            // Make the WebView itself transparent.
            OverlayMapView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            await WebViewSecurity.NavigateToStringAsync(
                OverlayMapView.CoreWebView2,
                MakeOverlayHtml(html));

            _webViewReady = true;

            await PrepareOverlayDocumentAsync();
            await ApplyOverlayOpacityAsync();

            if (!string.IsNullOrWhiteSpace(_pendingMarkersJson))
                await SetMapMarkersAsync(_pendingMarkersJson);

            if (!string.IsNullOrWhiteSpace(_pendingRaidExfilHighlightsJson))
                await ApplyRaidExfilHighlightsAsync(_pendingRaidExfilHighlightsJson);

            if (!string.IsNullOrWhiteSpace(_pendingMapLevelStateJson))
                await ApplyMapLevelStateAsync(_pendingMapLevelStateJson);

            if (!string.IsNullOrWhiteSpace(_pendingMarkerFiltersJson))
                await ApplyMarkerFiltersAsync(_pendingMarkerFiltersJson);

            if (!string.IsNullOrWhiteSpace(_pendingCustomPinsJson))
                await SetCustomPinsAsync(_pendingCustomPinsJson);

            if (!string.IsNullOrWhiteSpace(_pendingQuestFilterJson))
                await ApplyQuestFiltersAsync(_pendingQuestFilterJson);

            if (_pendingMarkerSearchJson != null)
                await ApplyMarkerSearchAsync(_pendingMarkerSearchJson);

            if (_pendingShowQuestNames.HasValue)
                await ApplyShowQuestNamesAsync(_pendingShowQuestNames.Value);

            if (_pendingPlayerMarker != null)
            {
                await SetPlayerMarkerAsync(
                    _pendingPlayerMarker.Value.NormalizedX,
                    _pendingPlayerMarker.Value.NormalizedY,
                    _pendingPlayerMarker.Value.DirectionDegrees);
            }
        }

        private string MakeOverlayHtml(string html)
        {
            // The main app HTML uses a dark background. The overlay needs a transparent page,
            // otherwise the overlay window looks like a solid black square.
            return html
                .Replace("background: #252526;", "background: transparent;")
                .Replace("background:#252526;", "background:transparent;");
        }

        private async Task PrepareOverlayDocumentAsync()
        {
            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync(@"
                (function() {
                    document.documentElement.style.background = 'transparent';
                    document.body.style.background = 'transparent';

                    const stage = document.getElementById('stage');
                    if (stage) {
                        stage.style.background = 'transparent';
                    }

                    const content = document.getElementById('content');
                    if (content) {
                        content.style.background = 'transparent';
                    }

                    window.setOverlayMapOpacity = function(value) {
                        const content = document.getElementById('content');
                        if (content) {
                            content.style.opacity = value;
                        }

                        const markerLayer = document.getElementById('markerLayer');
                        if (markerLayer) {
                            markerLayer.style.pointerEvents = 'none';
                        }
                    };

                    const overlayMarkerLayer = document.getElementById('markerLayer');
                    if (overlayMarkerLayer) {
                        overlayMarkerLayer.style.pointerEvents = 'none';
                    }
                })();
            ");
        }

        private async Task ApplyOverlayOpacityAsync()
        {
            // Convert 20..100 percentage down to 0.20..1.00 fraction for the WPF math
            double normalizedFraction = _overlaySettings.OverlayDefaultOpacityPercent / 100.0;

            // Compress the range so background panel stays readable (between 45% and 92% opacity)
            double panelOpacity = Math.Max(0.45, Math.Min(0.92, normalizedFraction * 0.85 + 0.15));

            // Apply ARGB background color to WPF border
            OverlayRootBorder.Background = new SolidColorBrush(Color.FromArgb(
                (byte)(panelOpacity * 255), 0x1A, 0x1D, 0x18));

            if (!_webViewReady)
                return;

            string value = normalizedFraction.ToString(CultureInfo.InvariantCulture);        
            await OverlayMapView.ExecuteScriptAsync($"if (window.setOverlayMapOpacity) window.setOverlayMapOpacity({value});");
        }

        public async Task SetMapMarkersAsync(string markersJson)
        {
            _pendingMarkersJson = markersJson;

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"addMapMarkers({markersJson});");

            if (!string.IsNullOrWhiteSpace(_pendingMarkerFiltersJson))
                await ApplyMarkerFiltersAsync(_pendingMarkerFiltersJson);

            if (!string.IsNullOrWhiteSpace(_pendingQuestFilterJson))
                await ApplyQuestFiltersAsync(_pendingQuestFilterJson);

            if (_pendingMarkerSearchJson != null)
                await ApplyMarkerSearchAsync(_pendingMarkerSearchJson);

            if (_pendingShowQuestNames.HasValue)
                await ApplyShowQuestNamesAsync(_pendingShowQuestNames.Value);

            if (!string.IsNullOrWhiteSpace(_pendingRaidExfilHighlightsJson))
                await ApplyRaidExfilHighlightsAsync(_pendingRaidExfilHighlightsJson);
        }

        public async Task ApplyRaidExfilHighlightsAsync(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            _pendingRaidExfilHighlightsJson = payloadJson;

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"setRaidExfilHighlights({payloadJson});");
        }

        public async Task SetPlayerMarkerAsync(
            double normalizedX,
            double normalizedY,
            double directionDegrees,
            bool centerOnPlayer = false)
        {
            _pendingPlayerMarker = (normalizedX, normalizedY, directionDegrees);

            if (!_webViewReady)
                return;

            string centerArg = centerOnPlayer ? "true" : "false";
            await OverlayMapView.ExecuteScriptAsync(
                $"setPlayerMarkerNormalized({normalizedX.ToString(CultureInfo.InvariantCulture)}, " +
                $"{normalizedY.ToString(CultureInfo.InvariantCulture)}, " +
                $"{directionDegrees.ToString(CultureInfo.InvariantCulture)}, {centerArg});");
        }

        public async Task ApplyMapLevelStateAsync(string stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson))
                return;

            _pendingMapLevelStateJson = stateJson;

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"applyMapLevelState({stateJson});");
        }

        public async Task ApplyMarkerFiltersAsync(string filtersJson)
        {
            if (string.IsNullOrWhiteSpace(filtersJson))
                return;

            _pendingMarkerFiltersJson = filtersJson;

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"applyMarkerFilters({filtersJson});");
        }

        public async Task ApplyMarkerSearchAsync(string searchJson)
        {
            _pendingMarkerSearchJson = string.IsNullOrWhiteSpace(searchJson) ? "\"\"" : searchJson;

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"applyMarkerSearch({_pendingMarkerSearchJson});");
        }

        public async Task ApplyQuestFiltersAsync(string questFilterJson)
        {
            if (string.IsNullOrWhiteSpace(questFilterJson))
                return;

            _pendingQuestFilterJson = questFilterJson;

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"applyQuestFilters({questFilterJson});");
        }

        public async Task ApplyShowQuestNamesAsync(bool showQuestNames)
        {
            _pendingShowQuestNames = showQuestNames;

            if (!_webViewReady)
                return;

            string visibility = showQuestNames ? "true" : "false";
            await OverlayMapView.ExecuteScriptAsync($"setQuestNamesVisibility({visibility});");
        }

        public async Task SetCustomPinsAsync(string pinsJson)
        {
            if (string.IsNullOrWhiteSpace(pinsJson))
                pinsJson = "[]";

            _pendingCustomPinsJson = pinsJson;

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"setCustomPins({pinsJson});");
        }

        public async Task ResetViewAsync()
        {
            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync("resetView();");
        }

        public async Task HighlightLinkedSwitchesAsync(string switchIdsJson)
        {
            if (string.IsNullOrWhiteSpace(switchIdsJson) || !_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"highlightLinkedSwitches({switchIdsJson});");
        }

        public async Task ClearLinkedSwitchHighlightsAsync()
        {
            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync("clearLinkedSwitchHighlights();");
        }

        private async void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressOpacitySliderRefresh || _overlaySettings == null)
                return;

            try
            {
                // Clamps the slider value between 20.0 and 100.0
                _overlaySettings.OverlayDefaultOpacityPercent = Math.Round(Math.Max(20.0, Math.Min(100.0, e.NewValue)));
                await ApplyOverlayOpacityAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Overlay opacity update failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void HideButton_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayBackgroundHidden)
            {
                _overlayBackgroundHidden = false;
                OverlayRootBorder.Opacity = 1.0;
            }
            else
            {
                _overlayBackgroundHidden = true;
                OverlayRootBorder.Opacity = 0.2;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void ResizeEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (sender is not FrameworkElement { Tag: string directionName })
                return;

            if (Enum.TryParse(directionName, out ResizeDirection direction))
                ResizeWindow(direction);
        }

        private void ResizeWindow(ResizeDirection direction)
        {
            SendMessage(new WindowInteropHelper(this).Handle, WM_SYSCOMMAND, (IntPtr)direction, IntPtr.Zero);
        }

        private enum ResizeDirection
        {
            Left = 61441,
            Right = 61442,
            Top = 61443,
            TopLeft = 61444,
            TopRight = 61445,
            Bottom = 61446,
            BottomLeft = 61447,
            BottomRight = 61448
        }

        private const uint WM_SYSCOMMAND = 0x0112;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
