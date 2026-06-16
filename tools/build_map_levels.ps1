$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configDir = Join-Path (Split-Path $toolsDir -Parent) 'Config'
$toolsDataDir = Join-Path $toolsDir 'data'
$mapsJson = Invoke-RestMethod -Uri 'https://raw.githubusercontent.com/the-hideout/tarkov-dev/main/src/data/maps.json'

$appMapKeys = @{
    'factory' = 'factory'
    'customs' = 'customs'
    'woods' = 'woods'
    'shoreline' = 'shoreline'
    'interchange' = 'interchange'
    'lab' = 'the-lab'
    'the-lab' = 'the-lab'
    'reserve' = 'reserve'
    'lighthouse' = 'lighthouse'
    'streetsoftarkov' = 'streets-of-tarkov'
    'streets-of-tarkov' = 'streets-of-tarkov'
    'groundzero' = 'ground-zero'
    'ground-zero' = 'ground-zero'
    'terminal' = 'terminal'
    'labyrinth' = 'the-labyrinth'
    'the-labyrinth' = 'the-labyrinth'
}

$output = [ordered]@{}

foreach ($entry in $mapsJson) {
    $norm = $entry.normalizedName
    $interactive = $entry.maps | Where-Object { $_.projection -eq 'interactive' } | Select-Object -First 1
    if (-not $interactive) { continue }

    $appKey = $null
    foreach ($kv in $appMapKeys.GetEnumerator()) {
        if ($kv.Value -eq $norm) { $appKey = $kv.Key; break }
    }
    if (-not $appKey) { continue }
    if ($appKey -eq 'the-lab') { $appKey = 'lab' }
    if ($appKey -eq 'streets-of-tarkov') { $appKey = 'streetsoftarkov' }
    if ($appKey -eq 'ground-zero') { $appKey = 'groundzero' }
    if ($appKey -eq 'the-labyrinth') { $appKey = 'labyrinth' }

    $levels = @()
    if ($interactive.layers) {
        foreach ($layer in $interactive.layers) {
            if ([string]::IsNullOrWhiteSpace($layer.svgLayer)) { continue }
            $levels += [ordered]@{
                name           = $layer.name
                svgLayer       = $layer.svgLayer
                defaultVisible = [bool]$layer.show
            }
        }
    }

    $output[$appKey] = [ordered]@{
        defaultSvgLayer = $interactive.svgLayer
        levels          = $levels
    }

    Write-Output "$appKey : default=$($interactive.svgLayer), levels=$($levels.Count)"
}

$output | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $configDir 'map_levels.json') -Encoding utf8
Write-Output 'Written map_levels.json'
