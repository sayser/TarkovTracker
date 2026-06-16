$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configDir = Join-Path (Split-Path $toolsDir -Parent) 'Config'
$toolsDataDir = Join-Path $toolsDir 'data'

function Normalize-MapName([string]$name) {
    return -join ($name.ToLowerInvariant().ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })
}

function Resolve-QuestMapKey([string]$mapDisplayName) {
    $k = Normalize-MapName $mapDisplayName
    switch ($k) {
        'lab' { return 'thelab' }
        'nightfactory' { return 'factory' }
        'groundzero21' { return 'groundzero' }
        'thelabyrinth' { return 'thelabyrinth' }
        default { return $k }
    }
}

function Resolve-DevMapKey([string]$normalizedName, [string]$displayName) {
    switch ($normalizedName) {
        'factory' { return 'factory' }
        'night-factory' { return 'factory' }
        'the-lab' { return 'thelab' }
        'streets-of-tarkov' { return 'streetsoftarkov' }
        'ground-zero' { return 'groundzero' }
        'ground-zero-21+' { return 'groundzero' }
        'the-labyrinth' { return 'thelabyrinth' }
        default {
            $k = Normalize-MapName ($normalizedName -replace '-', '')
            return $k
        }
    }
}

$query = @'
query {
  tasks {
    name
    objectives {
      __typename
      description
      optional
      maps { normalizedName name }
      ... on TaskObjectiveBasic { zones { map { normalizedName name } position { x y z } } }
      ... on TaskObjectiveItem { zones { map { normalizedName name } position { x y z } } }
      ... on TaskObjectiveQuestItem {
        zones { map { normalizedName name } position { x y z } }
        possibleLocations { map { normalizedName name } positions { x y z } }
      }
      ... on TaskObjectiveMark { zones { map { normalizedName name } position { x y z } } }
      ... on TaskObjectiveUseItem { zones { map { normalizedName name } position { x y z } } }
    }
  }
}
'@

$body = @{ query = $query } | ConvertTo-Json
$response = Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body $body

if ($response.errors) {
    Write-Output 'GraphQL errors:'
    $response.errors | ConvertTo-Json -Depth 5
    exit 1
}

$devCounts = @{}
$devKeys = @{}

foreach ($task in $response.data.tasks) {
    foreach ($obj in $task.objectives) {
        $locations = @()
        if ($obj.zones) { $locations += $obj.zones }
        if ($obj.possibleLocations) {
            foreach ($pl in $obj.possibleLocations) {
                if (-not $pl.positions) { continue }
                foreach ($pos in $pl.positions) {
                    $locations += [pscustomobject]@{ map = $pl.map; position = $pos }
                }
            }
        }

        foreach ($loc in $locations) {
            if ($null -eq $loc.position) { continue }
            $x = $loc.position.x
            $z = $loc.position.z
            if ($null -eq $x -or $null -eq $z) { continue }
            if ($x -eq 0 -and $z -eq 0) { continue }

            $mapNorm = $loc.map.normalizedName
            $mapName = $loc.map.name
            $k = Resolve-DevMapKey $mapNorm $mapName
            if (-not $devCounts.ContainsKey($k)) { $devCounts[$k] = 0 }
            $devCounts[$k]++
            $devKeys[$k] = $true
        }
    }
}

$localPath = Join-Path $configDir 'tarkov_quest_markers.json'
$local = Get-Content $localPath -Raw | ConvertFrom-Json
$localCounts = @{}
foreach ($prop in $local.PSObject.Properties) {
    $localCounts[$prop.Name] = @($prop.Value).Count
}

$appMaps = @('factory','customs','woods','shoreline','interchange','thelab','reserve','lighthouse','streetsoftarkov','groundzero','terminal','thelabyrinth')
Write-Output '=== Quest marker counts: local vs tarkov.dev ==='
Write-Output ''
foreach ($key in ($appMaps | Sort-Object)) {
    $localCount = if ($localCounts.ContainsKey($key)) { $localCounts[$key] } else { 0 }
    $devCount = if ($devCounts.ContainsKey($key)) { $devCounts[$key] } else { 0 }
    $delta = $localCount - $devCount
    $status = if ($localCount -eq $devCount) { 'OK' } elseif ($localCount -gt $devCount) { 'LOCAL+' } else { 'MISSING' }
    Write-Output ("{0,-18} local={1,3}  tarkov.dev={2,3}  delta={3,3}  [{4}]" -f $key, $localCount, $devCount, $delta, $status)
}

Write-Output ''
Write-Output '=== Extra maps on tarkov.dev (not in app) ==='
foreach ($key in ($devCounts.Keys | Sort-Object)) {
    if ($appMaps -notcontains $key) {
        Write-Output ("  {0}: {1}" -f $key, $devCounts[$key])
    }
}
