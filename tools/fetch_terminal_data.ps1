$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configDir = Join-Path (Split-Path $toolsDir -Parent) 'Config'
$toolsDataDir = Join-Path $toolsDir 'data'

$query = @'
{
  maps {
    name
    normalizedName
    tarkovDataId
    description
    enemies
    raidDuration
    extracts {
      id
      name
      faction
      position { x y z }
      switches { name }
      transferItem { item { name } count }
    }
    transits {
      id
      description
      position { x y z }
      conditions
    }
  }
}
'@

$body = @{ query = $query } | ConvertTo-Json
$maps = (Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body $body).data.maps
$terminal = $maps | Where-Object { $_.normalizedName -eq 'terminal' } | Select-Object -First 1

Write-Output "Terminal extracts: $($terminal.extracts.Count)"
Write-Output "Terminal transits: $($terminal.transits.Count)"
Write-Output "Enemies: $($terminal.enemies -join ', ')"
$terminal.extracts | ForEach-Object { Write-Output "  $($_.name) [$($_.faction)]" }

# Quest markers for terminal
$q = @'
query {
  tasks {
    name
    trader { name }
    minPlayerLevel
    objectives {
      __typename
      description
      optional
      ... on TaskObjectiveBasic { zones { map { normalizedName } position { x y z } } }
      ... on TaskObjectiveItem { item { name shortName iconLink } zones { map { normalizedName } position { x y z } } }
      ... on TaskObjectiveQuestItem {
        questItem { name shortName iconLink }
        zones { map { normalizedName } position { x y z } }
        possibleLocations { map { normalizedName } positions { x y z } }
      }
      ... on TaskObjectiveMark { markerItem { name shortName iconLink } zones { map { normalizedName } position { x y z } } }
      ... on TaskObjectiveUseItem { zones { map { normalizedName } position { x y z } } }
    }
  }
}
'@

$tasks = (Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body (@{query=$q}|ConvertTo-Json)).data.tasks
$questMarkers = 0
foreach ($task in $tasks) {
  foreach ($obj in $task.objectives) {
    $locs = @()
    if ($obj.zones) { $locs += $obj.zones }
    if ($obj.possibleLocations) {
      foreach ($pl in $obj.possibleLocations) {
        if ($pl.positions) {
          foreach ($p in $pl.positions) {
            $locs += [pscustomobject]@{ map = $pl.map; position = $p }
          }
        }
      }
    }
    foreach ($loc in $locs) {
      if ($loc.map.normalizedName -ne 'terminal') { continue }
      if ($null -eq $loc.position) { continue }
      if ($loc.position.x -eq 0 -and $loc.position.z -eq 0) { continue }
      $questMarkers++
      Write-Output "Quest: $($task.name) - $($obj.description)"
    }
  }
}
Write-Output "Total terminal quest markers: $questMarkers"

# Save terminal map metadata snippet
$terminal | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $toolsDataDir 'terminal_api_snapshot.json') -Encoding utf8
