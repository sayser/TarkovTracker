using System.IO;

namespace TarkovTracker.Services;

public static class MapHtmlBuilder
{
    private static string? _cachedCss;
    private static string? _cachedJs;

    public static string Build(string svg, string mapAssetHostName)
    {
        string css = LoadAsset("map-view.css").Replace("{{MAP_ASSET_HOST}}", mapAssetHostName);
        string js = LoadAsset("map-view.js");

        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
{css}
</style>
</head>

<body>
<div id='stage'>
    <div id='content'>
        {svg}
        <svg id='hazardOutlineLayer' xmlns='http://www.w3.org/2000/svg' style='position:absolute;left:0;top:0;pointer-events:none;z-index:8800;'></svg>
        <div id='markerLayer'></div>
        <div id='playerMarker'>
            <div id='playerMarkerInner'></div>
        </div>
    </div>
</div>

<script>
{js}
</script>
</body>
</html>";
    }

    private static string LoadAsset(string fileName)
    {
        if (fileName == "map-view.css" && _cachedCss != null)
            return _cachedCss;

        if (fileName == "map-view.js" && _cachedJs != null)
            return _cachedJs;

        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
        string content = File.ReadAllText(path);

        if (fileName == "map-view.css")
            _cachedCss = content;
        else if (fileName == "map-view.js")
            _cachedJs = content;

        return content;
    }
}
