$interactiveDir = Join-Path $PSScriptRoot "..\Maps\interactive"
New-Item -ItemType Directory -Force -Path $interactiveDir | Out-Null

$btrStopUrl = "https://cdn.jsdelivr.net/gh/the-hideout/tarkov-dev@main/public/maps/interactive/btr_stop.png"
$btrStopPath = Join-Path $interactiveDir "btr_stop.png"

try {
    Invoke-WebRequest -Uri $btrStopUrl -OutFile $btrStopPath -UseBasicParsing -TimeoutSec 60
    Write-Host "Downloaded btr_stop.png"
} catch {
    Write-Warning "Failed to download btr_stop.png: $($_.Exception.Message)"
}

$btrRoutePath = Join-Path $interactiveDir "btr_route.png"
if (-not (Test-Path $btrRoutePath)) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 24, 24
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 230, 197, 71)), 2.5
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash

    $points = @(
        [System.Drawing.Point]::new(2, 20),
        [System.Drawing.Point]::new(8, 12),
        [System.Drawing.Point]::new(14, 16),
        [System.Drawing.Point]::new(22, 4)
    )
    $graphics.DrawLines($pen, $points)
    $bitmap.Save($btrRoutePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Host "Created btr_route.png"
}
