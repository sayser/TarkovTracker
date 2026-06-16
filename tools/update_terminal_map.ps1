$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configDir = Join-Path (Split-Path $toolsDir -Parent) 'Config'
$toolsDataDir = Join-Path $toolsDir 'data'

function Update-JsonMapSection {
    param(
        [string]$FilePath,
        [string]$MapName,
        [string]$PropertyName,
        [object]$Data
    )

    $root = Get-Content $FilePath -Raw | ConvertFrom-Json
    $updated = $false
    foreach ($map in $root.data.maps) {
        if ($map.name -eq $MapName) {
            $map.$PropertyName = $Data
            $updated = $true
            break
        }
    }
    if (-not $updated) {
        $root.data.maps += [pscustomobject]@{
            name = $MapName
            $PropertyName = $Data
        }
    }
    $root | ConvertTo-Json -Depth 20 | Set-Content $FilePath -Encoding utf8
}

$query = @'
{
  maps {
    name
    normalizedName
    tarkovDataId
    description
    enemies
    raidDuration
    spawns { zoneName sides categories position { x y z } }
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
$terminal = ((Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body $body).data.maps |
    Where-Object { $_.normalizedName -eq 'terminal' } | Select-Object -First 1)

Write-Output "API spawns: $($terminal.spawns.Count)"
Write-Output "API extracts: $($terminal.extracts.Count)"
Write-Output "API transits: $($terminal.transits.Count)"

# Convert spawns to local format
$spawns = @()
foreach ($s in $terminal.spawns) {
    $spawns += [ordered]@{
        zoneName   = $s.zoneName
        sides      = @($s.sides)
        categories = @($s.categories)
        position   = [ordered]@{
            x = [double]$s.position.x
            y = [double]$s.position.y
            z = [double]$s.position.z
        }
    }
}

# Convert extracts
$extracts = @()
foreach ($e in $terminal.extracts) {
    $entry = [ordered]@{
        id       = $e.id
        name     = $e.name
        faction  = $e.faction
        switches = @()
        position = [ordered]@{
            x = [double]$e.position.x
            y = [double]$e.position.y
            z = [double]$e.position.z
        }
    }
    if ($e.switches) {
        foreach ($sw in $e.switches) {
            $entry.switches += [ordered]@{ name = $sw.name }
        }
    }
    if ($e.transferItem) {
        $entry.transferItem = [ordered]@{
            item  = [ordered]@{ name = $e.transferItem.item.name }
            count = $e.transferItem.count
        }
    } else {
        $entry.transferItem = $null
    }
    $extracts += $entry
}

# Convert transits
$transits = @()
foreach ($t in @($terminal.transits)) {
    if (-not $t) { continue }
    $transits += [ordered]@{
        id          = $t.id
        description = $t.description
        conditions  = $t.conditions
        position    = [ordered]@{
            x = [double]$t.position.x
            y = [double]$t.position.y
            z = [double]$t.position.z
        }
    }
}

Update-JsonMapSection -FilePath (Join-Path $configDir 'tarkov_spawns_raw.json') -MapName 'Terminal' -PropertyName 'spawns' -Data $spawns
Update-JsonMapSection -FilePath (Join-Path $configDir 'tarkov_extracts_raw.json') -MapName 'Terminal' -PropertyName 'extracts' -Data $extracts
Update-JsonMapSection -FilePath (Join-Path $configDir 'tarkov_transits_raw.json') -MapName 'Terminal' -PropertyName 'transits' -Data $transits

# Save metadata for maps.json update
[ordered]@{
    tdevId       = $terminal.tarkovDataId
    description  = $terminal.description
    enemies      = @($terminal.enemies)
    raidDuration = $terminal.raidDuration
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $toolsDataDir 'terminal_map_meta.json') -Encoding utf8

Write-Output "Updated Terminal spawns/extracts/transits in config JSON files."
