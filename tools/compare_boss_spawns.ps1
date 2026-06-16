$ErrorActionPreference = 'Stop'
$query = '{ maps { name bosses { boss { name } spawnLocations { spawnKey name chance } } } }'
$body = @{ query = $query } | ConvertTo-Json
$response = Invoke-RestMethod -Uri 'https://api.tarkov.dev/graphql' -Method Post -ContentType 'application/json' -Body $body
if ($response.errors) { $response.errors | ConvertTo-Json; exit 1 }
foreach ($map in $response.data.maps) {
    $total = 0
    foreach ($b in $map.bosses) { if ($b.spawnLocations) { $total += $b.spawnLocations.Count } }
    if ($total -gt 0) {
        Write-Output "$($map.name): $total boss spawn locations"
        foreach ($b in $map.bosses) {
            if ($b.spawnLocations -and $b.spawnLocations.Count -gt 0) {
                Write-Output "  $($b.boss.name): $($b.spawnLocations.Count)"
                foreach ($loc in $b.spawnLocations) {
                    Write-Output "    $($loc.spawnKey) / $($loc.name) ($($loc.chance)%)"
                }
            }
        }
    }
}
