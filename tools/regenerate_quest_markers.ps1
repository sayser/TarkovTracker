$ErrorActionPreference = 'Stop'
# NOTE: For full parity with tarkov.dev maps (includes possibleLocations for quest items),
# run build_quest_markers_from_api.ps1 instead. This script only uses zone data from tasks_raw.
$configDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$tasksPath = Join-Path $toolsDataDir 'tarkov_tasks_raw.json'
$outputPath = Join-Path $configDir 'tarkov_quest_markers.json'

function Normalize-MapName([string]$name) {
    return -join ($name.ToLowerInvariant().ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })
}

function Resolve-QuestMapKey([string]$mapDisplayName) {
    $k = Normalize-MapName $mapDisplayName
    switch ($k) {
        'lab' { return 'thelab' }
        'nightfactory' { return 'factory' }
        'groundzero21' { return 'groundzero' }
        default { return $k }
    }
}

# App maps from maps.json locale names
$appMapKeys = @(
    'factory', 'customs', 'woods', 'shoreline', 'interchange', 'thelab',
    'reserve', 'lighthouse', 'streetsoftarkov', 'groundzero', 'terminal', 'thelabyrinth'
)

$tasks = Get-Content $tasksPath -Raw | ConvertFrom-Json
$byMap = @{}

foreach ($task in $tasks) {
    foreach ($obj in $task.objectives) {
        if (-not $obj.zones) { continue }
        foreach ($zone in $obj.zones) {
            if ([string]::IsNullOrWhiteSpace($zone.map)) { continue }
            if ($null -eq $zone.x -or $null -eq $zone.z) { continue }
            if ($zone.x -eq 0 -and $zone.z -eq 0) { continue }

            $mapKey = Resolve-QuestMapKey $zone.map
            if ($appMapKeys -notcontains $mapKey) { continue }

            $category = if ($obj.category -eq 'item') { 'item' } else { 'objective' }
            $marker = [ordered]@{
                quest          = $task.name
                objectiveType  = $obj.objectiveType
                category       = $category
                iconType       = $category
                description    = $obj.description
                questItem      = if ($obj.itemName) { [string]$obj.itemName } else { '' }
                itemShortName  = if ($obj.itemShortName) { [string]$obj.itemShortName } else { '' }
                itemIconLink   = if ($obj.itemIconLink) { [string]$obj.itemIconLink } else { '' }
                trader         = $task.trader
                minPlayerLevel = [int]$task.minPlayerLevel
                optional       = [bool]$obj.optional
                x              = [double]$zone.x
                y              = if ($null -ne $zone.y) { [double]$zone.y } else { 0.0 }
                z              = [double]$zone.z
            }

            if (-not $byMap.ContainsKey($mapKey)) {
                $byMap[$mapKey] = New-Object System.Collections.Generic.List[object]
            }
            $byMap[$mapKey].Add($marker)
        }
    }
}

$sorted = [ordered]@{}
foreach ($key in ($byMap.Keys | Sort-Object)) {
    $sorted[$key] = @($byMap[$key] | Sort-Object quest, description, x, z)
}

$sorted | ConvertTo-Json -Depth 6 | Set-Content $outputPath -Encoding utf8

Write-Output 'Quest markers regenerated (night factory + GZ 21+ merged into parent maps):'
foreach ($key in ($sorted.Keys | Sort-Object)) {
    Write-Output ("  {0}: {1}" -f $key, $sorted[$key].Count)
}
Write-Output ("Total: {0}" -f (($sorted.Values | ForEach-Object { $_.Count }) | Measure-Object -Sum).Sum)
