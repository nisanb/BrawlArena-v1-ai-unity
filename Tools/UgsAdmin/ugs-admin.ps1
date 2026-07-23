<#
.SYNOPSIS
  Remote admin for Crownfall player accounts via the UGS CLI.

  Reads/writes the single Cloud Save item `player_state` (a JSON snapshot of a
  player's account) for any player id. See README.md for one-time setup. In
  short: `ugs login` (uses your Unity Hub account) then set the project/env
  config once.

.EXAMPLE
  ./ugs-admin.ps1 players                                   # list all player ids
  ./ugs-admin.ps1 get   -PlayerId <id>
  ./ugs-admin.ps1 set   -PlayerId <id> -Field meta.gems  -Value 500
  ./ugs-admin.ps1 grant -PlayerId <id> -Field meta.coins -Value 1000
  ./ugs-admin.ps1 reset -PlayerId <id>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('get', 'set', 'grant', 'reset', 'players')]
    [string]$Command,

    [string]$PlayerId,
    [string]$Field,
    [int]$Value
)

$ErrorActionPreference = 'Stop'
$Key = 'player_state'
$UgsExe = Join-Path $HOME '.local\bin\ugs.exe'
if (-not (Test-Path $UgsExe)) { throw "UGS CLI not found at $UgsExe. See README.md setup." }

# String-valued keys vs int-valued keys, mirrored from CrownfallCloud.cs.
$StringKeys = @('meta.playerName', 'meta.lastGift', 'quests.day')

# Call the UGS CLI. The CLI logs status to stderr, which PowerShell 5.1 would
# otherwise promote to a terminating error under ErrorActionPreference=Stop. We
# drop to Continue and null stderr for the duration of the call, then gate on the
# real signal: the process exit code.
function Invoke-Ugs {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CliArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & $UgsExe @CliArgs 2>$null }
    finally { $ErrorActionPreference = $prev }
    if ($LASTEXITCODE -ne 0) { throw "ugs $($CliArgs -join ' ') failed (exit $LASTEXITCODE)" }
    return $out
}

function Require-PlayerId {
    if (-not $PlayerId) { throw "$Command requires -PlayerId. Run 'players' to list ids." }
}

function Load-Snapshot {
    $raw = Invoke-Ugs cloud-save data player get --player-id $PlayerId --keys $Key -j
    if (-not $raw) { return $null }
    $outer = ($raw | Out-String) | ConvertFrom-Json
    $item = $outer.Items | Where-Object { $_.key -eq $Key } | Select-Object -First 1
    if (-not $item) { return $null }
    # The value is stored as a structured JSON object, so ConvertFrom-Json on the
    # outer payload has already parsed it — return it directly.
    return $item.value
}

function Save-Snapshot($snap) {
    # Bump rev so this write wins the next pull on the player's device.
    $snap.rev = [int]$snap.rev + 1
    $json = $snap | ConvertTo-Json -Compress -Depth 12
    # Escape embedded quotes so CommandLineToArgvW delivers literal quotes to the
    # CLI (otherwise PowerShell/Win32 strips them and the JSON arrives malformed).
    $escaped = $json.Replace('"', '\"')
    Invoke-Ugs cloud-save data player set --player-id $PlayerId --key $Key --value $escaped | Out-Null
    Write-Host "Saved. New rev = $($snap.rev)" -ForegroundColor Green
}

function Get-KV($snap, $field) {
    $list = if ($StringKeys -contains $field) { $snap.strs } else { $snap.ints }
    return ($list | Where-Object { $_.k -eq $field } | Select-Object -First 1)
}

function Set-KV($snap, $field, $val) {
    $isStr = $StringKeys -contains $field
    $kv = Get-KV $snap $field
    if ($kv) {
        $kv.v = if ($isStr) { "$val" } else { [int]$val }
        return
    }
    # New key: rebuild the target array with the entry appended.
    $entry = [pscustomobject]@{ k = $field; v = $(if ($isStr) { "$val" } else { [int]$val }) }
    if ($isStr) { $snap.strs = @($snap.strs) + $entry }
    else { $snap.ints = @($snap.ints) + $entry }
}

switch ($Command) {
    'players' {
        $raw = Invoke-Ugs player list -j
        (($raw | Out-String) | ConvertFrom-Json).Players.results | ForEach-Object {
            Write-Host ("{0}   created {1}" -f $_.id, $_.createdAt)
        }
    }
    'get' {
        Require-PlayerId
        $snap = Load-Snapshot
        if (-not $snap) { Write-Host "No player_state for $PlayerId" -ForegroundColor Yellow; break }
        Write-Host "rev=$($snap.rev)  playerId=$($snap.playerId)" -ForegroundColor Cyan
        Write-Host "--- ints ---";    $snap.ints | Format-Table k, v -AutoSize
        Write-Host "--- strings ---"; $snap.strs | Format-Table k, v -AutoSize
    }
    'set' {
        Require-PlayerId
        if (-not $Field) { throw "set requires -Field and -Value" }
        $snap = Load-Snapshot; if (-not $snap) { throw "No player_state for $PlayerId" }
        Set-KV $snap $Field $Value
        Save-Snapshot $snap
        Write-Host "$Field = $Value" -ForegroundColor Green
    }
    'grant' {
        Require-PlayerId
        if (-not $Field) { throw "grant requires -Field and -Value" }
        $snap = Load-Snapshot; if (-not $snap) { throw "No player_state for $PlayerId" }
        $kv = Get-KV $snap $Field
        $cur = if ($kv) { [int]$kv.v } else { 0 }
        Set-KV $snap $Field ($cur + $Value)
        Save-Snapshot $snap
        Write-Host "$Field : $cur -> $($cur + $Value)" -ForegroundColor Green
    }
    'reset' {
        Require-PlayerId
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
