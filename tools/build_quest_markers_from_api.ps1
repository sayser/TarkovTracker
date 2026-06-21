$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configDir = Join-Path (Split-Path $toolsDir -Parent) 'Config'
$toolsDataDir = Join-Path $toolsDir 'data'
$outputPath = Join-Path $configDir 'tarkov_quest_markers.json'

function Normalize-MapName([string]$name) {
    return -join ($name.ToLowerInvariant().ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })
}

function Resolve-QuestMapKey([string]$normalizedName, [string]$displayName) {
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
            if ($k -eq 'lab') { return 'thelab' }
            return $k
        }
    }
}

$appMapKeys = @(
    'factory', 'customs', 'woods', 'shoreline', 'interchange', 'thelab',
    'reserve', 'lighthouse', 'streetsoftarkov', 'groundzero', 'terminal', 'thelabyrinth'
)

$query = @'
query {
  tasks {
    name
    normalizedName
    trader { name }
    minPlayerLevel
    objectives {
      __typename
      type
      description
      optional
      ... on TaskObjectiveBasic { zones { map { normalizedName name } position { x y z } } }
      ... on TaskObjectiveItem {
        item { name shortName iconLink }
        zones { map { normalizedName name } position { x y z } }
      }
      ... on TaskObjectiveQuestItem {
        questItem { name shortName iconLink }
        zones { map { normalizedName name } position { x y z } }
        possibleLocations { map { normalizedName name } positions { x y z } }
      }
      ... on TaskObjectiveMark {
        markerItem { name shortName iconLink }
        zones { map { normalizedName name } position { x y z } }
      }
      ... on TaskObjectiveUseItem { zones { map { normalizedName name } position { x y z } } }
    }
  }
}
'@

$body = @{ query = $query } | ConvertTo-Json
$response = Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body $body
if ($response.errors) {
    $response.errors | ConvertTo-Json -Depth 5
    exit 1
}

$byMap = @{}

foreach ($task in $response.data.tasks) {
    $trader = if ($task.trader.name) { [string]$task.trader.name } else { '' }
    $level = [int]$task.minPlayerLevel
    $questSlug = if ($task.normalizedName) { [string]$task.normalizedName } else { '' }

    foreach ($obj in $task.objectives) {
        $objectiveType = [string]$obj.__typename
        $category = if ($objectiveType -match 'Item|QuestItem') { 'item' } else { 'objective' }

        $itemName = ''
        $itemShort = ''
        $itemIcon = ''
        if ($obj.item) {
            $itemName = [string]$obj.item.name
            $itemShort = [string]$obj.item.shortName
            $itemIcon = [string]$obj.item.iconLink
        }
        elseif ($obj.questItem) {
            $itemName = [string]$obj.questItem.name
            $itemShort = [string]$obj.questItem.shortName
            $itemIcon = [string]$obj.questItem.iconLink
        }
        elseif ($obj.markerItem) {
            $itemName = [string]$obj.markerItem.name
            $itemShort = [string]$obj.markerItem.shortName
            $itemIcon = [string]$obj.markerItem.iconLink
        }

        $locations = New-Object System.Collections.Generic.List[object]
        if ($obj.zones) {
            foreach ($z in $obj.zones) { $locations.Add($z) }
        }
        if ($obj.possibleLocations) {
            foreach ($pl in $obj.possibleLocations) {
                if (-not $pl.positions) { continue }
                foreach ($pos in $pl.positions) {
                    $locations.Add([pscustomobject]@{
                        map      = $pl.map
                        position = $pos
                    })
                }
            }
        }

        foreach ($loc in $locations) {
            if ($null -eq $loc.position) { continue }
            $x = [double]$loc.position.x
            $z = [double]$loc.position.z
            if ($x -eq 0 -and $z -eq 0) { continue }

            $mapKey = Resolve-QuestMapKey $loc.map.normalizedName $loc.map.name
            if ($appMapKeys -notcontains $mapKey) { continue }

            $y = if ($null -ne $loc.position.y) { [double]$loc.position.y } else { 0.0 }
            $marker = [ordered]@{
                quest          = $task.name
                questSlug      = $questSlug
                objectiveType  = $objectiveType
                category       = $category
                iconType       = $category
                description    = [string]$obj.description
                questItem      = $itemName
                itemShortName  = $itemShort
                itemIconLink   = $itemIcon
                trader         = $trader
                minPlayerLevel = $level
                optional       = [bool]$obj.optional
                x              = $x
                y              = $y
                z              = $z
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

Write-Output 'Quest markers built from tarkov.dev API:'
foreach ($key in ($sorted.Keys | Sort-Object)) {
    Write-Output ("  {0}: {1}" -f $key, $sorted[$key].Count)
}
Write-Output ("Total: {0}" -f (($sorted.Values | ForEach-Object { $_.Count }) | Measure-Object -Sum).Sum)
