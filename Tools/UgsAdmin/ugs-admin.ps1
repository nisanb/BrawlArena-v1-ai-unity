<#
.SYNOPSIS
  Remote admin for Crownfall player accounts via the UGS CLI.

  Reads/writes the single Cloud Save item `player_state` (a JSON snapshot of the
  player's account) for any player id. See README.md for one-time setup.

.EXAMPLE
  ./ugs-admin.ps1 get   -PlayerId <id>
  ./ugs-admin.ps1 set   -PlayerId <id> -Field meta.gems  -Value 500
  ./ugs-admin.ps1 grant -PlayerId <id> -Field meta.coins -Value 1000
  ./ugs-admin.ps1 reset -PlayerId <id>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('get', 'set', 'grant', 'reset')]
    [string]$Command,

    [Parameter(Mandatory = $true)]
    [string]$PlayerId,

    [string]$Field,
    [int]$Value
)

$ErrorActionPreference = 'Stop'
$Key = 'player_state'
$Ugs = Join-Path $HOME '.local/bin/ugs.exe'
if (-not (Test-Path $Ugs)) { throw "UGS CLI not found at $Ugs — see README.md setup." }

# Integer keys vs string keys, mirrored from CrownfallCloud.cs.
$StringKeys = @('meta.playerName', 'meta.lastGift', 'quests.day')

function Load-Snapshot {
    $raw = & $Ugs cloud-save player-data get $Key --player-id $PlayerId 2>$null
    if (-not $raw) { return $null }
    # The CLI returns a JSON map { key: { value: "<json string>" } }
    try {
        $outer = $raw | ConvertFrom-Json
        $inner = $outer.$Key.value
        if (-not $inner) { $inner = $outer.$Key }
        return $inner | ConvertFrom-Json
    }
    catch { return $null }
}

function Save-Snapshot($snap) {
    $snap.rev = [int]$snap.rev + 1
    $json = $snap | ConvertTo-Json -Depth 8 -Compress
    # Store the snapshot JSON as the string value of the player_state key.
    $payload = @{ $Key = $json } | ConvertTo-Json -Depth 8 -Compress
    $tmp = New-TemporaryFile
    Set-Content -Path $tmp -Value $payload -Encoding utf8
    & $Ugs cloud-save player-data save $Key --player-id $PlayerId --file $tmp
    Remove-Item $tmp -Force
    Write-Host "Saved. New rev = $($snap.rev)" -ForegroundColor Green
}

function Get-KV($snap, $field) {
    $isStr = $StringKeys -contains $field
    $list = if ($isStr) { $snap.strs } else { $snap.ints }
    foreach ($kv in $list) { if ($kv.k -eq $field) { return $kv } }
    return $null
}

function Set-KV($snap, $field, $val) {
    $isStr = $StringKeys -contains $field
    if ($isStr) {
        if (-not $snap.strs) { $snap | Add-Member strs @() -Force }
        $kv = Get-KV $snap $field
        if ($kv) { $kv.v = "$val" } else { $snap.strs += [pscustomobject]@{ k = $field; v = "$val" } }
    }
    else {
        if (-not $snap.ints) { $snap | Add-Member ints @() -Force }
        $kv = Get-KV $snap $field
        if ($kv) { $kv.v = [int]$val } else { $snap.ints += [pscustomobject]@{ k = $field; v = [int]$val } }
    }
}

switch ($Command) {
    'get' {
        $snap = Load-Snapshot
        if (-not $snap) { Write-Host "No player_state for $PlayerId" -ForegroundColor Yellow; break }
        Write-Host "rev=$($snap.rev)  playerId=$($snap.playerId)" -ForegroundColor Cyan
        Write-Host "--- ints ---"; $snap.ints | Format-Table k, v -AutoSize
        Write-Host "--- strings ---"; $snap.strs | Format-Table k, v -AutoSize
    }
    'set' {
        if (-not $Field) { throw "set requires -Field and -Value" }
        $snap = Load-Snapshot; if (-not $snap) { throw "No player_state for $PlayerId" }
        Set-KV $snap $Field $Value
        Save-Snapshot $snap
        Write-Host "$Field = $Value" -ForegroundColor Green
    }
    'grant' {
        if (-not $Field) { throw "grant requires -Field and -Value" }
        $snap = Load-Snapshot; if (-not $snap) { throw "No player_state for $PlayerId" }
        $kv = Get-KV $snap $Field
        $cur = if ($kv) { [int]$kv.v } else { 0 }
        Set-KV $snap $Field ($cur + $Value)
        Save-Snapshot $snap
        Write-Host "$Field : $cur -> $($cur + $Value)" -ForegroundColor Green
    }
    'reset' {
        # Minimal first-run defaults matching CrownfallMeta.Ensure().
        $snap = [pscustomobject]@{
            rev      = 0
            utc      = 0
            playerId = $PlayerId
            ints     = @(
                [pscustomobject]@{ k = 'meta.gems'; v = 30 },
                [pscustomobject]@{ k = 'meta.coins'; v = 120 },
                [pscustomobject]@{ k = 'meta.trophies'; v = 0 },
                [pscustomobject]@{ k = 'meta.level'; v = 1 },
                [pscustomobject]@{ k = 'meta.xp'; v = 0 },
                [pscustomobject]@{ k = 'meta.selectedClass'; v = 0 },
                [pscustomobject]@{ k = 'meta.sigilsOwned'; v = 0 },
                [pscustomobject]@{ k = 'meta.hasProfile'; v = 0 }
            )
            strs     = @()
        }
        Save-Snapshot $snap
        Write-Host "Reset $PlayerId to first-run defaults." -ForegroundColor Green
    }
}
