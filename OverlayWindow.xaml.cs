using Microsoft.Web.WebView2.Core;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TarkovTracker
{
    public partial class OverlayWindow : Window
    {
        private bool _webViewReady = false;
        private bool _mapAssetHostMapped = false;
        private string? _mapsFolder;
        private string? _pendingMarkersJson;
        private string? _pendingMapLevelStateJson;
        private string? _pendingMarkerFiltersJson;
        private (double NormalizedX, double NormalizedY, double DirectionDegrees)? _pendingPlayerMarker;
        private double _overlayOpacity = 0.80;

        public OverlayWindow()
        {
            InitializeComponent();

            Loaded += async (_, _) =>
            {
                await ApplyOverlayOpacityAsync();
            };
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

            var tcs = new TaskCompletionSource<bool>();

            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                OverlayMapView.NavigationCompleted -= Handler;
                tcs.TrySetResult(true);
            }

            OverlayMapView.NavigationCompleted += Handler;
            OverlayMapView.NavigateToString(MakeOverlayHtml(html));

            await tcs.Task;

            _webViewReady = true;

            await PrepareOverlayDocumentAsync();
            await ApplyOverlayOpacityAsync();

            if (!string.IsNullOrWhiteSpace(_pendingMarkersJson))
                await SetMapMarkersAsync(_pendingMarkersJson);

            if (!string.IsNullOrWhiteSpace(_pendingMapLevelStateJson))
                await ApplyMapLevelStateAsync(_pendingMapLevelStateJson);

            if (!string.IsNullOrWhiteSpace(_pendingMarkerFiltersJson))
                await ApplyMarkerFiltersAsync(_pendingMarkerFiltersJson);

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
                        const svg = document.querySelector('#content svg');
                        if (svg) {
                            svg.style.opacity = value;
                        }

                        // Keep labels/markers/player full-strength so they stay readable.
                        const markerLayer = document.getElementById('markerLayer');
                        if (markerLayer) {
                            markerLayer.style.opacity = '1';
                        }

                        const playerMarker = document.getElementById('playerMarker');
                        if (playerMarker) {
                            playerMarker.style.opacity = '1';
                        }
                    };
                })();
            ");
        }

        private async Task ApplyOverlayOpacityAsync()
        {
            double panelOpacity = Math.Max(0.45, Math.Min(0.92, _overlayOpacity * 0.85 + 0.15));
            OverlayRootBorder.Background = new SolidColorBrush(Color.FromArgb(
                (byte)(panelOpacity * 255), 0x1A, 0x1D, 0x18));

            if (!_webViewReady)
                return;

            string value = _overlayOpacity.ToString(CultureInfo.InvariantCulture);
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
        }

        public async Task SetPlayerMarkerAsync(double normalizedX, double normalizedY, double directionDegrees)
        {
            _pendingPlayerMarker = (normalizedX, normalizedY, directionDegrees);

            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync(
                $"setPlayerMarkerNormalized({normalizedX.ToString(CultureInfo.InvariantCulture)}, " +
                $"{normalizedY.ToString(CultureInfo.InvariantCulture)}, " +
                $"{directionDegrees.ToString(CultureInfo.InvariantCulture)});");
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

        public async Task ResetViewAsync()
        {
            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync("resetView();");
        }

        private async void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _overlayOpacity = Math.Max(0.05, Math.Min(1.0, e.NewValue / 100.0));
            await ApplyOverlayOpacityAsync();
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
