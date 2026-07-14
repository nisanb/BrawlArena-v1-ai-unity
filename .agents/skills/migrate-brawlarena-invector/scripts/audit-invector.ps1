[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [switch]$FailOnBlockers
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
} else {
    $ProjectRoot = Resolve-Path $ProjectRoot
}

function Read-TextFile {
    param([string]$RelativePath)

    $path = Join-Path $ProjectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $RelativePath"
    }
    return Get-Content -LiteralPath $path -Raw
}

function Get-MatchValue {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Fallback = '<not found>'
    )

    $match = [regex]::Match($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline)
    if ($match.Success) { return $match.Groups[1].Value.Trim() }
    return $Fallback
}

$projectVersion = Read-TextFile 'ProjectSettings\ProjectVersion.txt'
$projectSettings = Read-TextFile 'ProjectSettings\ProjectSettings.asset'
$tagManager = Read-TextFile 'ProjectSettings\TagManager.asset'
$inputMeta = Read-TextFile 'Assets\Invector-3rdPersonController\Shooter\Scripts\Shooter\vShooterMeleeInput.cs.meta'
$thirdPersonInput = Read-TextFile 'Assets\Invector-3rdPersonController\Basic Locomotion\Scripts\CharacterController\vThirdPersonInput.cs'
$shooterManager = Read-TextFile 'Assets\Invector-3rdPersonController\Shooter\Scripts\Shooter\vShooterManager.cs'
$invectorIcon = Read-TextFile 'Assets\Invector-3rdPersonController\Basic Locomotion\Scripts\Generic\Editor\vInvectorIcon.cs'

$unityVersion = Get-MatchValue $projectVersion '^m_EditorVersion:\s*(.+)$'
$inputHandling = Get-MatchValue $projectSettings '^\s*activeInputHandler:\s*(\d+)\s*$'
$packageVersion = Get-MatchValue $inputMeta 'packageVersion:\s*([^\r\n}]+)'

$inputModes = @{
    '0' = 'Legacy Input Manager'
    '1' = 'Input System Package (New)'
    '2' = 'Both'
}
$inputModeName = $inputModes[$inputHandling]
if (-not $inputModeName) { $inputModeName = 'Unknown' }

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets\Invector-3rdPersonController') -Recurse -Filter '*.cs' -File)
$prefabs = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets\Invector-3rdPersonController') -Recurse -Filter '*.prefab' -File)
$controllers = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets\Invector-3rdPersonController') -Recurse -Filter '*.controller' -File)
$manuals = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets\Invector-3rdPersonController') -Recurse -Filter '*.pdf' -File)

$badOverloadPattern = 'FindObjectsByType<[^>]+>\s*\(\s*FindObjectsInactive\.Exclude\s*\)'
$thirdPersonBlocker = [regex]::IsMatch($thirdPersonInput, $badOverloadPattern)
$shooterBlocker = [regex]::IsMatch($shooterManager, $badOverloadPattern)
$hierarchyIconBlocker = $invectorIcon.Contains('hierarchyWindowItemByEntityIdOnGUI')

$layersBlock = [regex]::Match(
    $tagManager,
    '(?ms)^[ \t]*layers:[ \t]*\r?\n(?<body>.*?)(?=^[ \t]*m_SortingLayers:)'
)
if (-not $layersBlock.Success) {
    throw 'Could not parse the layers block in ProjectSettings\TagManager.asset.'
}
$layerLines = [regex]::Matches(
    $layersBlock.Groups['body'].Value,
    '(?m)^[ \t]*-[ \t]*(.*?)[ \t]*\r?$'
)
$layers = @()
for ($i = 0; $i -lt [Math]::Min(32, $layerLines.Count); $i++) {
    $layers += [pscustomobject]@{
        Index = $i
        Name = $layerLines[$i].Groups[1].Value.Trim()
    }
}

Write-Output '# BrawlArena Invector audit'
Write-Output ''
Write-Output "Project root: $ProjectRoot"
Write-Output "Unity: $unityVersion"
Write-Output "Invector package: $packageVersion"
Write-Output "Active input handling: $inputHandling ($inputModeName)"
Write-Output "Package inventory: $($sourceFiles.Count) C# files, $($prefabs.Count) prefabs, $($controllers.Count) Animator controllers, $($manuals.Count) PDF manuals"
Write-Output ''
Write-Output '## Project layers 8-31'
foreach ($layer in $layers | Where-Object { $_.Index -ge 8 }) {
    $name = if ([string]::IsNullOrWhiteSpace($layer.Name)) { '<empty>' } else { $layer.Name }
    Write-Output ("{0,2}: {1}" -f $layer.Index, $name)
}
Write-Output ''
Write-Output '## Known Unity 6000.3 source compatibility findings'
Write-Output "vThirdPersonInput one-argument FindObjectsByType pattern present: $thirdPersonBlocker"
Write-Output "vShooterManager one-argument FindObjectsByType pattern present: $shooterBlocker"
Write-Output "vInvectorIcon unavailable hierarchy callback pattern present: $hierarchyIconBlocker"
Write-Output ''
Write-Output 'This script is read-only and pattern-based; it does not prove Unity compile state. Confirm a domain reload and the live Unity console after every source change.'

if ($FailOnBlockers -and ($thirdPersonBlocker -or $shooterBlocker -or $hierarchyIconBlocker)) {
    throw 'Known Invector Unity 6000.3 compile blockers are still present.'
}
