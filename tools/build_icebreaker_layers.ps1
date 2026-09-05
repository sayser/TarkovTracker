# Stitch tarkov.dev Icebreaker floor tiles into PNGs + layered SVG for SayserTarkovTracker.
# Source: https://assets.tarkov.dev/maps/icebreaker/<layer>/{z}/{x}/{y}.png

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$outDir = Join-Path $root "Maps\icebreaker"
$svgPath = Join-Path $root "Maps\Icebreaker.svg"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$zoom = 4
$tileSize = 256
$grid = [math]::Pow(2, $zoom)  # 16
$pixelSize = [int]($grid * $tileSize)  # 4096
$baseUrl = "https://assets.tarkov.dev/maps/icebreaker"

$layers = @(
    @{ id = "Control_Room";            folder = "00_control_room";        name = "Control Room" },
    @{ id = "Engine_Room";             folder = "01_engine_room";         name = "Engine Room" },
    @{ id = "Engine_Room_Upper";       folder = "02_engine_room_upper";   name = "Engine Room Upper" },
    @{ id = "Fuel_Pumps_Lower";        folder = "03_fuel_pumps_lower";    name = "Fuel Pumps Lower" },
    @{ id = "Fuel_Pumps";              folder = "04_fuel_pumps";          name = "Fuel Pumps" },
    @{ id = "Storage_Security";        folder = "05_storage_ecurity";     name = "Storage Security" },
    @{ id = "Infirmary";               folder = "06_infirmary";           name = "Infirmary" },
    @{ id = "Helipad";                 folder = "07_helipad";             name = "Helipad" },
    @{ id = "Gym_Canteen";             folder = "08_gym-canteen";         name = "Gym Canteen" },
    @{ id = "Accommodation_Lower";     folder = "09_accommodation_lower"; name = "Accommodation Lower" },
    @{ id = "Accommodation_Mid";       folder = "10_accommodation_mid";   name = "Accommodation Mid" },
    @{ id = "Accommodation_Upper";     folder = "11_accommodation_upper"; name = "Accommodation Upper" },
    @{ id = "Officers_Deck";           folder = "12_officers_deck";       name = "Officers Deck" },
    @{ id = "Stairs_Blocked";          folder = "13_stairs_blocked";      name = "Stairs Blocked" },
    @{ id = "Bridge";                  folder = "14_bridge";              name = "Bridge" },
    @{ id = "Bridge_Roof";             folder = "15_bridge_roof";         name = "Bridge Roof" }
)

$wc = New-Object System.Net.WebClient
$wc.Headers.Add("User-Agent", "SayserTarkovTracker/2.8.1")

foreach ($layer in $layers) {
    $pngPath = Join-Path $outDir "$($layer.folder).png"
    Write-Host "Stitching $($layer.name) ($($layer.folder)) ..."

    $bmp = New-Object System.Drawing.Bitmap $pixelSize, $pixelSize
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 24, 24, 24))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

    $drawn = 0
    for ($x = 0; $x -lt $grid; $x++) {
        for ($y = 0; $y -lt $grid; $y++) {
            $url = "$baseUrl/$($layer.folder)/$zoom/$x/$y.png"
            $tmp = Join-Path $env:TEMP "ib_tile_$x`_$y.png"
            try {
                $wc.DownloadFile($url, $tmp)
                $len = (Get-Item $tmp).Length
                if ($len -le 200) { continue }
                $tile = [System.Drawing.Image]::FromFile($tmp)
                try {
                    $g.DrawImage($tile, $x * $tileSize, $y * $tileSize, $tileSize, $tileSize)
                    $drawn++
                } finally {
                    $tile.Dispose()
                }
            } catch {
                # missing tile
            } finally {
                Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            }
        }
    }

    $g.Dispose()
    $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $sizeMb = [math]::Round((Get-Item $pngPath).Length / 1MB, 2)
    Write-Host "  -> $pngPath ($drawn tiles, ${sizeMb} MB)"
}

$wc.Dispose()

# Build layered SVG (Infirmary is default/base like tarkov.dev)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" xml:space="preserve" id="svg1" viewBox="0 0 4096 4096">')
foreach ($layer in $layers) {
    $href = "icebreaker/$($layer.folder).png"
    [void]$sb.AppendLine("  <g id=`"$($layer.id)`">")
    [void]$sb.AppendLine("    <image href=`"$href`" x=`"0`" y=`"0`" width=`"4096`" height=`"4096`" preserveAspectRatio=`"none`"/>")
    [void]$sb.AppendLine("  </g>")
}
[void]$sb.AppendLine("</svg>")
[System.IO.File]::WriteAllText($svgPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $svgPath"
Write-Host "Done."
