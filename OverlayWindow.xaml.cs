using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TarkovTracker
{
    public partial class OverlayWindow : Window
    {
        private bool _webViewReady = false;
        private string? _pendingMarkersJson;
        private readonly Dictionary<string, bool> _pendingLayerVisibility = new();
        private OverlayMarkerVisibility _pendingMarkerVisibility = new();
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

        public async Task LoadMapHtmlAsync(string html)
        {
            _webViewReady = false;

            await OverlayMapView.EnsureCoreWebView2Async();

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

            foreach (var layer in _pendingLayerVisibility)
                await SetLayerVisibilityAsync(layer.Key, layer.Value);

            await ApplyMarkerVisibilityAsync(_pendingMarkerVisibility);

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
            OverlayRootBorder.Background = new SolidColorBrush(Color.FromArgb(35, 30, 30, 30));

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
            await ApplyMarkerVisibilityAsync(_pendingMarkerVisibility);
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

        public async Task SetLayerVisibilityAsync(string layerId, bool visible)
        {
            if (string.IsNullOrWhiteSpace(layerId))
                return;

            _pendingLayerVisibility[layerId] = visible;

            if (!_webViewReady)
                return;

            string idJson = JsonSerializer.Serialize(layerId);
            string visibleJson = visible ? "true" : "false";

            await OverlayMapView.ExecuteScriptAsync($"setLayerVisibility({idJson}, {visibleJson});");
        }

        public async Task ApplyMarkerVisibilityAsync(
            bool pmcExtracts,
            bool scavExtracts,
            bool sharedExtracts,
            bool transits,
            bool pmcSpawns,
            bool scavSpawns,
            bool bossSpawns,
            bool labels,
            bool questMarkers)
        {
            _pendingMarkerVisibility = new OverlayMarkerVisibility
            {
                PmcExtracts = pmcExtracts,
                ScavExtracts = scavExtracts,
                SharedExtracts = sharedExtracts,
                Transits = transits,
                PmcSpawns = pmcSpawns,
                ScavSpawns = scavSpawns,
                BossSpawns = bossSpawns,
                Labels = labels,
                QuestMarkers = questMarkers
            };

            await ApplyMarkerVisibilityAsync(_pendingMarkerVisibility);
        }

        private async Task ApplyMarkerVisibilityAsync(OverlayMarkerVisibility visibility)
        {
            if (!_webViewReady)
                return;

            await OverlayMapView.ExecuteScriptAsync($"setExtractFactionVisibility('pmc', {(visibility.PmcExtracts ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setExtractFactionVisibility('scav', {(visibility.ScavExtracts ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setExtractFactionVisibility('shared', {(visibility.SharedExtracts ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setTransitVisibility({(visibility.Transits ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setSpawnVisibility('spawn-pmc', {(visibility.PmcSpawns ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setSpawnVisibility('spawn-scav', {(visibility.ScavSpawns ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setSpawnVisibility('spawn-boss', {(visibility.BossSpawns ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setLabelVisibility({(visibility.Labels ? "true" : "false")});");
            await OverlayMapView.ExecuteScriptAsync($"setQuestVisibility({(visibility.QuestMarkers ? "true" : "false")});");
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
    }

    public class OverlayMarkerVisibility
    {
        public bool PmcExtracts { get; set; } = true;
        public bool ScavExtracts { get; set; } = true;
        public bool SharedExtracts { get; set; } = true;
        public bool Transits { get; set; } = true;
        public bool PmcSpawns { get; set; } = false;
        public bool ScavSpawns { get; set; } = false;
        public bool BossSpawns { get; set; } = false;
        public bool Labels { get; set; } = true;
        public bool QuestMarkers { get; set; } = false;
    }
}
