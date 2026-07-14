[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
} else {
    $ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
}

$vendorRelativeRoot = 'Assets\Invector-3rdPersonController'
$vendorRoot = Join-Path $ProjectRoot $vendorRelativeRoot
$migrationGeneratedRoot = Join-Path $ProjectRoot 'Assets\Generated\InvectorMigration'
$migrationLabScene = Join-Path $ProjectRoot 'Assets\Scenes\InvectorMigrationLab.unity'
if (-not (Test-Path -LiteralPath $vendorRoot -PathType Container)) {
    throw "Invector asset root not found: $vendorRoot"
}

function Get-NormalizedRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = $ProjectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the project root: $full"
    }
    return $full.Substring($root.Length + 1).Replace('\', '/')
}

function Read-RequiredLines {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $ProjectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file not found: $RelativePath"
    }
    return ,([IO.File]::ReadAllLines($path))
}

function Read-RequiredText {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $ProjectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file not found: $RelativePath"
    }
    return [IO.File]::ReadAllText($path)
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertFrom-SimpleYamlScalar {
    param([AllowEmptyString()][string]$Value)

    if ($null -eq $Value) { return $null }
    $trimmed = $Value.Trim()
    if ($trimmed.Length -ge 2) {
        if (($trimmed[0] -eq '"' -and $trimmed[$trimmed.Length - 1] -eq '"') -or
            ($trimmed[0] -eq "'" -and $trimmed[$trimmed.Length - 1] -eq "'")) {
            return $trimmed.Substring(1, $trimmed.Length - 2)
        }
    }
    return $trimmed
}

function Get-UnityStringList {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$Section,
        [string]$NextSection
    )

    $values = @()
    $inSection = $false
    foreach ($line in $Lines) {
        if (-not $inSection) {
            if ($line -match ('^  ' + [regex]::Escape($Section) + ':\s*(.*)$')) {
                $inSection = $true
                if ($Matches[1].Trim() -eq '[]') { return @() }
            }
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($NextSection) -and
            $line -match ('^  ' + [regex]::Escape($NextSection) + ':')) {
            break
        }
        if ($line -match '^  [A-Za-z_][A-Za-z0-9_]*:' -and $line -notmatch '^  -') {
            break
        }
        if ($line -match '^  -\s?(.*)$') {
            $values += [string](ConvertFrom-SimpleYamlScalar $Matches[1])
        }
    }
    return @($values)
}

function Get-SortingLayers {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $result = @()
    $current = $null
    $inSection = $false
    foreach ($line in $Lines) {
        if (-not $inSection) {
            if ($line -match '^  m_SortingLayers:') { $inSection = $true }
            continue
        }
        if ($line -match '^  m_RenderingLayers:') { break }
        if ($line -match '^  - name:\s*(.*)$') {
            if ($null -ne $current) { $result += [pscustomobject]$current }
            $current = [ordered]@{
                name = [string](ConvertFrom-SimpleYamlScalar $Matches[1])
                uniqueIdRaw = $null
                lockedRaw = $null
            }
        } elseif ($null -ne $current -and $line -match '^    uniqueID:\s*(.*)$') {
            $current.uniqueIdRaw = $Matches[1].Trim()
        } elseif ($null -ne $current -and $line -match '^    locked:\s*(.*)$') {
            $current.lockedRaw = $Matches[1].Trim()
        }
    }
    if ($null -ne $current) { $result += [pscustomobject]$current }
    return @($result)
}

function Get-FlatYamlScalars {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $result = @()
    $stack = @()
    foreach ($line in $Lines) {
        if ($line -notmatch '^(?<indent> *)(?<key>[A-Za-z_][A-Za-z0-9_]*):(?:\s*(?<value>.*))?$') {
            continue
        }
        $indent = $Matches['indent'].Length
        $key = $Matches['key']
        $value = $Matches['value']

        while ($stack.Count -gt 0 -and $stack[$stack.Count - 1].indent -ge $indent) {
            if ($stack.Count -eq 1) { $stack = @() }
            else { $stack = @($stack[0..($stack.Count - 2)]) }
        }

        if ($indent -eq 0) {
            $stack = @([pscustomobject]@{ indent = $indent; key = $key })
            continue
        }

        $parents = @($stack | Select-Object -Skip 1 | ForEach-Object { $_.key })
        $pathParts = @($parents) + @($key)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $result += [pscustomobject][ordered]@{
                path = ($pathParts -join '.')
                rawValue = $value.Trim()
            }
        } else {
            $stack += [pscustomobject]@{ indent = $indent; key = $key }
        }
    }
    return @($result)
}

function Get-LegacyInputAxes {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $axes = @()
    $currentFields = $null
    foreach ($line in $Lines) {
        if ($line -match '^  - serializedVersion:\s*(.*)$') {
            if ($null -ne $currentFields) {
                $axes += New-LegacyInputAxis -Index $axes.Count -Fields $currentFields
            }
            $currentFields = @([pscustomobject][ordered]@{ name = 'serializedVersion'; rawValue = $Matches[1].Trim() })
            continue
        }
        if ($null -ne $currentFields -and $line -match '^    ([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$') {
            $currentFields += [pscustomobject][ordered]@{
                name = $Matches[1]
                rawValue = $Matches[2].Trim()
            }
        }
    }
    if ($null -ne $currentFields) {
        $axes += New-LegacyInputAxis -Index $axes.Count -Fields $currentFields
    }
    return @($axes)
}

function New-LegacyInputAxis {
    param(
        [int]$Index,
        [object[]]$Fields
    )

    $nameField = @($Fields | Where-Object { $_.name -eq 'm_Name' } | Select-Object -First 1)
    $typeField = @($Fields | Where-Object { $_.name -eq 'type' } | Select-Object -First 1)
    $typeRaw = if ($typeField.Count -gt 0) { $typeField[0].rawValue } else { $null }
    $typeSemantics = @{
        '0' = 'KeyOrMouseButton'
        '1' = 'MouseMovement'
        '2' = 'JoystickAxis'
    }
    $typeName = if ($null -ne $typeRaw -and $typeSemantics.ContainsKey($typeRaw)) {
        $typeSemantics[$typeRaw]
    } else {
        'Unknown'
    }

    return [pscustomobject][ordered]@{
        index = $Index
        name = if ($nameField.Count -gt 0) { [string](ConvertFrom-SimpleYamlScalar $nameField[0].rawValue) } else { $null }
        typeRaw = $typeRaw
        typeSemantic = $typeName
        fields = @($Fields)
    }
}

function Get-InputActionAssets {
    $assetsRoot = Join-Path $ProjectRoot 'Assets'
    $files = @(Get-ChildItem -LiteralPath $assetsRoot -Recurse -File -Filter '*.inputactions' |
        Sort-Object { Get-NormalizedRelativePath $_.FullName })
    $result = @()
    foreach ($file in $files) {
        $relativePath = Get-NormalizedRelativePath $file.FullName
        $entry = [ordered]@{
            path = $relativePath
            sha256 = Get-Sha256 $file.FullName
            maps = @()
            controlSchemes = @()
            parseError = $null
        }
        try {
            $document = [IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
            $maps = @()
            foreach ($map in @($document.maps)) {
                $maps += [pscustomobject][ordered]@{
                    name = $map.name
                    id = $map.id
                    actions = @($map.actions | ForEach-Object { $_.name })
                    bindingCount = @($map.bindings).Count
                }
            }
            $entry.maps = @($maps)
            $entry.controlSchemes = @($document.controlSchemes | ForEach-Object { $_.name })
        } catch {
            $entry.parseError = $_.Exception.Message
        }
        $result += [pscustomobject]$entry
    }
    return @($result)
}

function Test-UnityYamlFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $reader = New-Object IO.StreamReader($Path, $true)
    try {
        for ($i = 0; $i -lt 12 -and -not $reader.EndOfStream; $i++) {
            $line = $reader.ReadLine()
            if ($line -match '^%YAML\s' -or $line -match '^--- !u!') { return $true }
        }
        return $false
    } finally {
        $reader.Dispose()
    }
}

function Get-CurrentLayerSemantic {
    param(
        [long]$Index,
        [AllowEmptyString()]
        [string[]]$LayerNames
    )

    if ($Index -lt 0 -or $Index -gt 31) { return $null }
    $name = $LayerNames[[int]$Index]
    if ([string]::IsNullOrWhiteSpace($name)) { return $null }
    return $name
}

function Get-MaskSemanticKind {
    param(
        [Parameter(Mandatory = $true)][string]$FieldName,
        [Parameter(Mandatory = $true)][string]$Encoding
    )

    $name = $FieldName.ToLowerInvariant()
    if ($name -match 'renderinglayer|sortinglayer|navmesh|uvchannel|lightmap|shadow|probe|influence') {
        return 'OtherLayerDomain'
    }
    if ($name -eq 'm_cullingmask') { return 'PhysicsLayerMask' }
    if ($name -match 'mask|layer|ground|obstacle|target|damage|hit|collision|interact|detect|raycast|cover|ignore|block|trigger|walk|surface|environment|enemy|friend|ally|ragdoll') {
        return 'LayerMaskCandidate'
    }
    return 'UnclassifiedBitfield'
}

function Get-NormalizedUInt32 {
    param([Parameter(Mandatory = $true)][string]$RawValue)

    [long]$number = 0
    if (-not [long]::TryParse($RawValue, [ref]$number)) { return $null }
    if ($number -lt -2147483648L -or $number -gt 4294967295L) { return $null }
    if ($number -lt 0) { $number += 4294967296L }
    return $number
}

function Get-DecodedLayerBits {
    param(
        [long]$NormalizedValue,
        [AllowEmptyString()]
        [string[]]$LayerNames
    )

    $result = @()
    for ($index = 0; $index -lt 32; $index++) {
        $bit = [long]1 -shl $index
        if (($NormalizedValue -band $bit) -ne 0) {
            $result += [pscustomobject][ordered]@{
                index = $index
                configuredName = Get-CurrentLayerSemantic -Index $index -LayerNames $LayerNames
            }
        }
    }
    return @($result)
}

function Get-DocumentRanges {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $headers = @()
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^--- !u!(?<classId>-?\d+) &(?<objectId>-?\d+)') {
            $headers += [pscustomobject]@{
                start = $i
                classId = $Matches['classId']
                objectId = $Matches['objectId']
            }
        }
    }
    $documents = @()
    for ($i = 0; $i -lt $headers.Count; $i++) {
        $start = $headers[$i].start
        $end = if ($i + 1 -lt $headers.Count) { $headers[$i + 1].start - 1 } else { $Lines.Count - 1 }
        $yamlType = $null
        for ($j = $start + 1; $j -le $end; $j++) {
            if ($Lines[$j] -match '^([A-Za-z_][A-Za-z0-9_]*):\s*$') {
                $yamlType = $Matches[1]
                break
            }
        }
        $documents += [pscustomobject]@{
            start = $start
            end = $end
            classId = $headers[$i].classId
            objectId = $headers[$i].objectId
            yamlType = $yamlType
        }
    }
    return @($documents)
}

function Get-DocumentName {
    param(
        [AllowEmptyString()]
        [string[]]$Lines,
        [object]$Document
    )

    for ($i = $Document.start + 1; $i -le $Document.end; $i++) {
        if ($Lines[$i] -match '^  m_Name:\s*(.*)$') {
            return [string](ConvertFrom-SimpleYamlScalar $Matches[1])
        }
    }
    return $null
}

function Get-DocumentGameObjectId {
    param(
        [AllowEmptyString()]
        [string[]]$Lines,
        [object]$Document
    )

    if ($Document.yamlType -eq 'GameObject') { return $Document.objectId }
    for ($i = $Document.start + 1; $i -le $Document.end; $i++) {
        if ($Lines[$i] -match '^  m_GameObject:\s*\{fileID:\s*(-?\d+)') {
            return $Matches[1]
        }
    }
    return $null
}

function Get-ParentYamlField {
    param(
        [AllowEmptyString()]
        [string[]]$Lines,
        [int]$DocumentStart,
        [int]$LineIndex,
        [int]$ChildIndent
    )

    for ($i = $LineIndex - 1; $i -gt $DocumentStart; $i--) {
        if ($Lines[$i] -match '^(?<indent> *)(?:-\s*)?(?<key>[A-Za-z_][A-Za-z0-9_]*):(?:\s*.*)?$') {
            if ($Matches['indent'].Length -lt $ChildIndent) { return $Matches['key'] }
        }
    }
    return '<unknown>'
}

function Find-OverrideValue {
    param(
        [AllowEmptyString()]
        [string[]]$Lines,
        [int]$PropertyLine,
        [int]$DocumentEnd
    )

    for ($i = $PropertyLine + 1; $i -le [Math]::Min($DocumentEnd, $PropertyLine + 6); $i++) {
        if ($Lines[$i] -match '^\s+- target:' -or $Lines[$i] -match '^\s+propertyPath:') { break }
        if ($Lines[$i] -match '^\s+value:\s*(-?\d+)\s*$') {
            return [pscustomobject]@{ value = $Matches[1]; line = $i }
        }
    }
    return $null
}

function New-MaskFinding {
    param(
        [string]$Path,
        [int]$Line,
        [object]$Document,
        [string]$OwnerName,
        [string]$FieldName,
        [string]$Encoding,
        [string]$RawValue,
        [AllowEmptyString()]
        [string[]]$LayerNames
    )

    $semanticKind = Get-MaskSemanticKind -FieldName $FieldName -Encoding $Encoding
    $normalized = Get-NormalizedUInt32 -RawValue $RawValue
    return [pscustomobject][ordered]@{
        path = $Path
        line = $Line
        yamlClassId = $Document.classId
        yamlObjectId = $Document.objectId
        yamlType = $Document.yamlType
        ownerName = $OwnerName
        field = $FieldName
        encoding = $Encoding
        rawSerializedValue = $RawValue
        normalizedUInt32 = if ($null -ne $normalized) { $normalized.ToString() } else { $null }
        semanticKind = $semanticKind
    }
}

function Get-InvectorYamlInventory {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$LayerNames
    )

    $extensions = @('.prefab', '.unity', '.asset')
    $fileCandidates = New-Object 'System.Collections.Generic.List[System.IO.FileInfo]'
    foreach ($scanRoot in @($vendorRoot, $migrationGeneratedRoot)) {
        if (-not (Test-Path -LiteralPath $scanRoot -PathType Container)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $scanRoot -Recurse -File)) {
            if ($extensions -contains $file.Extension.ToLowerInvariant()) {
                [void]$fileCandidates.Add($file)
            }
        }
    }
    if (Test-Path -LiteralPath $migrationLabScene -PathType Leaf) {
        [void]$fileCandidates.Add((Get-Item -LiteralPath $migrationLabScene))
    }
    $files = @($fileCandidates |
        Sort-Object { Get-NormalizedRelativePath $_.FullName } -Unique)
    $yamlFiles = New-Object 'System.Collections.Generic.List[string]'
    $nonYamlFiles = New-Object 'System.Collections.Generic.List[string]'
    $gameObjectLayers = New-Object 'System.Collections.Generic.List[object]'
    $maskFindings = New-Object 'System.Collections.Generic.List[object]'

    foreach ($file in $files) {
        $relativePath = Get-NormalizedRelativePath $file.FullName
        if (-not (Test-UnityYamlFile -Path $file.FullName)) {
            [void]$nonYamlFiles.Add($relativePath)
            continue
        }
        [void]$yamlFiles.Add($relativePath)
        $lines = [IO.File]::ReadAllLines($file.FullName)
        $documents = @(Get-DocumentRanges -Lines $lines)
        $gameObjectNames = @{}
        foreach ($document in $documents) {
            if ($document.yamlType -eq 'GameObject') {
                $gameObjectNames[$document.objectId] = Get-DocumentName -Lines $lines -Document $document
            }
        }

        foreach ($document in $documents) {
            $documentName = Get-DocumentName -Lines $lines -Document $document
            $gameObjectId = Get-DocumentGameObjectId -Lines $lines -Document $document
            $ownerName = $documentName
            if ($null -ne $gameObjectId -and $gameObjectNames.ContainsKey([string]$gameObjectId)) {
                $ownerName = $gameObjectNames[[string]$gameObjectId]
            }

            for ($lineIndex = $document.start + 1; $lineIndex -le $document.end; $lineIndex++) {
                $line = $lines[$lineIndex]

                if ($document.yamlType -eq 'GameObject' -and $line -match '^  m_Layer:\s*(-?\d+)\s*$') {
                    [long]$rawLayer = [long]$Matches[1]
                    [void]$gameObjectLayers.Add([pscustomobject][ordered]@{
                        path = $relativePath
                        line = $lineIndex + 1
                        yamlObjectId = $document.objectId
                        gameObjectName = $documentName
                        sourceEncoding = 'GameObject.m_Layer'
                        rawLayer = $rawLayer
                        currentProjectLayerName = Get-CurrentLayerSemantic -Index $rawLayer -LayerNames $LayerNames
                    })
                    continue
                }

                if ($line -match '^\s*propertyPath:\s*(.*?)\s*$') {
                    $propertyPath = [string](ConvertFrom-SimpleYamlScalar $Matches[1])
                    if ($propertyPath -match '(?i)mask|layer') {
                        $override = Find-OverrideValue -Lines $lines -PropertyLine $lineIndex -DocumentEnd $document.end
                        if ($null -ne $override) {
                            if ($propertyPath -eq 'm_Layer') {
                                [long]$rawLayer = [long]$override.value
                                [void]$gameObjectLayers.Add([pscustomobject][ordered]@{
                                    path = $relativePath
                                    line = $override.line + 1
                                    yamlObjectId = $document.objectId
                                    gameObjectName = $null
                                    sourceEncoding = 'PrefabModification.propertyPath'
                                    rawLayer = $rawLayer
                                    currentProjectLayerName = Get-CurrentLayerSemantic -Index $rawLayer -LayerNames $LayerNames
                                })
                            } else {
                                [void]$maskFindings.Add((New-MaskFinding -Path $relativePath -Line ($override.line + 1) -Document $document -OwnerName $ownerName -FieldName $propertyPath -Encoding 'PrefabModification' -RawValue $override.value -LayerNames $LayerNames))
                            }
                        }
                    }
                    continue
                }

                if ($line -match '^(?<indent> *)(?<field>[A-Za-z_][A-Za-z0-9_]*):\s*\{[^}]*\bm_Bits:\s*(?<value>-?\d+)') {
                    $fieldName = $Matches['field']
                    $rawValue = $Matches['value']
                    [void]$maskFindings.Add((New-MaskFinding -Path $relativePath -Line ($lineIndex + 1) -Document $document -OwnerName $ownerName -FieldName $fieldName -Encoding 'InlineMBits' -RawValue $rawValue -LayerNames $LayerNames))
                    continue
                }

                if ($line -match '^(?<indent> *)m_Bits:\s*(?<value>-?\d+)\s*$') {
                    $childIndent = $Matches['indent'].Length
                    $rawValue = $Matches['value']
                    $fieldName = Get-ParentYamlField -Lines $lines -DocumentStart $document.start -LineIndex $lineIndex -ChildIndent $childIndent
                    [void]$maskFindings.Add((New-MaskFinding -Path $relativePath -Line ($lineIndex + 1) -Document $document -OwnerName $ownerName -FieldName $fieldName -Encoding 'NestedMBits' -RawValue $rawValue -LayerNames $LayerNames))
                    continue
                }

                if ($line -match '^ *(?<field>[A-Za-z_][A-Za-z0-9_]*):\s*(?<value>-?\d+)\s*$') {
                    $fieldName = $Matches['field']
                    $rawValue = $Matches['value']
                    if ($fieldName -ne 'm_Layer' -and $fieldName -ne 'm_Bits' -and $fieldName -match '(?i)mask') {
                        [void]$maskFindings.Add((New-MaskFinding -Path $relativePath -Line ($lineIndex + 1) -Document $document -OwnerName $ownerName -FieldName $fieldName -Encoding 'Scalar' -RawValue $rawValue -LayerNames $LayerNames))
                    }
                }
            }
        }
    }

    $gameObjectLayers = @($gameObjectLayers | Sort-Object path, line, sourceEncoding)
    $maskFindings = @($maskFindings | Sort-Object path, line, field, encoding)
    return [pscustomobject][ordered]@{
        candidateFileCount = $files.Count
        yamlFileCount = $yamlFiles.Count
        skippedNonYamlFileCount = $nonYamlFiles.Count
        yamlFiles = @($yamlFiles)
        skippedNonYamlFiles = @($nonYamlFiles)
        gameObjectLayers = @($gameObjectLayers)
        layerMaskLikeFields = @($maskFindings)
    }
}

function Get-CollisionMatrixInventory {
    param(
        [AllowNull()][string]$RawHex,
        [AllowEmptyString()]
        [string[]]$LayerNames
    )

    if ([string]::IsNullOrWhiteSpace($RawHex)) {
        return [pscustomobject][ordered]@{
            rawHex = $RawHex
            encoding = 'Unity serialized bytes; expected 32 little-endian UInt32 rows'
            decodeStatus = 'Missing'
            rows = @()
        }
    }
    $hex = $RawHex.Trim()
    if ($hex.Length -ne 256 -or $hex -notmatch '^[0-9a-fA-F]{256}$') {
        return [pscustomobject][ordered]@{
            rawHex = $hex
            encoding = 'Unity serialized bytes; expected 32 little-endian UInt32 rows'
            decodeStatus = 'UnexpectedFormat'
            rows = @()
        }
    }

    $rows = @()
    for ($rowIndex = 0; $rowIndex -lt 32; $rowIndex++) {
        $chunk = $hex.Substring($rowIndex * 8, 8)
        $bytes = @()
        for ($byteIndex = 0; $byteIndex -lt 4; $byteIndex++) {
            $bytes += [Convert]::ToByte($chunk.Substring($byteIndex * 2, 2), 16)
        }
        [long]$mask = [long]$bytes[0] + ([long]$bytes[1] * 256L) + ([long]$bytes[2] * 65536L) + ([long]$bytes[3] * 16777216L)
        $enabled = @(Get-DecodedLayerBits -NormalizedValue $mask -LayerNames $LayerNames)
        $rows += [pscustomobject][ordered]@{
            layerIndex = $rowIndex
            configuredLayerName = Get-CurrentLayerSemantic -Index $rowIndex -LayerNames $LayerNames
            rawHexLittleEndian = $chunk.ToLowerInvariant()
            rawUInt32 = $mask.ToString()
            enabledCurrentProjectLayers = @($enabled)
        }
    }
    return [pscustomobject][ordered]@{
        rawHex = $hex.ToLowerInvariant()
        encoding = '32 little-endian UInt32 rows; row index and bit index use current Unity layer indices'
        decodeStatus = 'Decoded'
        rows = @($rows)
    }
}

function Get-PhysicsInventory {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$LayerNames,
        [switch]$Optional
    )

    $path = Join-Path $ProjectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        if ($Optional) { return $null }
        throw "Required physics settings file not found: $RelativePath"
    }
    $lines = [IO.File]::ReadAllLines($path)
    $scalars = @(Get-FlatYamlScalars -Lines $lines)
    $matrixEntry = @($scalars | Where-Object { $_.path -eq 'm_LayerCollisionMatrix' } | Select-Object -First 1)
    $rawMatrix = if ($matrixEntry.Count -gt 0) { $matrixEntry[0].rawValue } else { $null }
    return [pscustomobject][ordered]@{
        path = $RelativePath.Replace('\', '/')
        sha256 = Get-Sha256 $path
        rawScalarSettings = @($scalars)
        layerCollisionMatrix = Get-CollisionMatrixInventory -RawHex $rawMatrix -LayerNames $LayerNames
    }
}

$tagManagerPath = Join-Path $ProjectRoot 'ProjectSettings\TagManager.asset'
$tagManagerLines = Read-RequiredLines 'ProjectSettings\TagManager.asset'
$tags = @(Get-UnityStringList -Lines $tagManagerLines -Section 'tags' -NextSection 'layers')
$layerNames = @(Get-UnityStringList -Lines $tagManagerLines -Section 'layers' -NextSection 'm_SortingLayers')
if ($layerNames.Count -ne 32) {
    throw "Expected 32 Unity layers in ProjectSettings/TagManager.asset; found $($layerNames.Count)."
}
$layers = @()
for ($index = 0; $index -lt 32; $index++) {
    $layers += [pscustomobject][ordered]@{
        index = $index
        configuredName = if ([string]::IsNullOrWhiteSpace($layerNames[$index])) { $null } else { $layerNames[$index] }
        isConfigured = -not [string]::IsNullOrWhiteSpace($layerNames[$index])
    }
}
$sortingLayers = @(Get-SortingLayers -Lines $tagManagerLines)
$renderingLayers = @(Get-UnityStringList -Lines $tagManagerLines -Section 'm_RenderingLayers')

$projectSettingsText = Read-RequiredText 'ProjectSettings\ProjectSettings.asset'
$activeInputHandlerRaw = $null
if ($projectSettingsText -match '(?m)^\s*activeInputHandler:\s*(\d+)\s*$') {
    $activeInputHandlerRaw = $Matches[1]
}
$inputHandlingNames = @{
    '0' = 'LegacyInputManager'
    '1' = 'InputSystemPackageNew'
    '2' = 'Both'
}
$activeInputHandlerSemantic = if ($null -ne $activeInputHandlerRaw -and $inputHandlingNames.ContainsKey($activeInputHandlerRaw)) {
    $inputHandlingNames[$activeInputHandlerRaw]
} else {
    'Unknown'
}
$inputManagerPath = Join-Path $ProjectRoot 'ProjectSettings\InputManager.asset'
$inputManagerLines = Read-RequiredLines 'ProjectSettings\InputManager.asset'
$legacyAxes = @(Get-LegacyInputAxes -Lines $inputManagerLines)

$invectorYaml = Get-InvectorYamlInventory -LayerNames $layerNames
$layerCounts = @()
foreach ($group in @($invectorYaml.gameObjectLayers | Group-Object rawLayer | Sort-Object { [long]$_.Name })) {
    $rawLayer = [long]$group.Name
    $layerCounts += [pscustomobject][ordered]@{
        rawLayer = $rawLayer
        currentProjectLayerName = Get-CurrentLayerSemantic -Index $rawLayer -LayerNames $layerNames
        count = $group.Count
        directAssignmentCount = @($group.Group | Where-Object { $_.sourceEncoding -eq 'GameObject.m_Layer' }).Count
        prefabOverrideCount = @($group.Group | Where-Object { $_.sourceEncoding -eq 'PrefabModification.propertyPath' }).Count
    }
}
$maskKindCounts = @()
foreach ($group in @($invectorYaml.layerMaskLikeFields | Group-Object semanticKind | Sort-Object Name)) {
    $maskKindCounts += [pscustomobject][ordered]@{ semanticKind = $group.Name; count = $group.Count }
}
$maskValueSemantics = @()
$decodableMaskValues = @($invectorYaml.layerMaskLikeFields |
    Where-Object {
        $null -ne $_.normalizedUInt32 -and
        ($_.semanticKind -eq 'PhysicsLayerMask' -or $_.semanticKind -eq 'LayerMaskCandidate')
    } |
    Group-Object normalizedUInt32 |
    Sort-Object { [uint64]$_.Name })
foreach ($group in $decodableMaskValues) {
    [long]$normalized = [long]$group.Name
    $maskValueSemantics += [pscustomobject][ordered]@{
        normalizedUInt32 = $normalized.ToString()
        setBitsDecodedAgainstCurrentProjectLayers = @(Get-DecodedLayerBits -NormalizedValue $normalized -LayerNames $layerNames)
    }
}

$report = [pscustomobject][ordered]@{
    schemaVersion = 1
    projectRoot = [IO.Path]::GetFullPath($ProjectRoot).Replace('\', '/')
    determinism = [pscustomobject][ordered]@{
        generatedTimestampOmitted = $true
        ordering = 'Paths are project-relative; findings are sorted by path then line with stable secondary keys.'
    }
    interpretation = [pscustomobject][ordered]@{
        rawValues = 'rawLayer, rawSerializedValue, rawScalarSettings, and collision-matrix hex are serialized source values.'
        decodedSemantics = 'Configured names are decoded only through the current ProjectSettings/TagManager.asset and do not establish Invector upstream intent.'
        candidates = 'LayerMaskCandidate is name/encoding-based and requires script or Inspector verification. OtherLayerDomain and UnclassifiedBitfield are deliberately not decoded as physics layers.'
        maskValueLookup = 'Detailed findings retain raw values. Decoded physics-layer semantics are interned once in invectorYaml.maskValueSemantics and keyed by normalizedUInt32.'
    }
    tagsAndLayers = [pscustomobject][ordered]@{
        sourcePath = 'ProjectSettings/TagManager.asset'
        sha256 = Get-Sha256 $tagManagerPath
        tags = @($tags)
        layers = @($layers)
        sortingLayers = @($sortingLayers)
        renderingLayers = @($renderingLayers)
    }
    input = [pscustomobject][ordered]@{
        activeInputHandler = [pscustomobject][ordered]@{
            sourcePath = 'ProjectSettings/ProjectSettings.asset'
            rawValue = $activeInputHandlerRaw
            semantic = $activeInputHandlerSemantic
        }
        legacyInputManager = [pscustomobject][ordered]@{
            sourcePath = 'ProjectSettings/InputManager.asset'
            sha256 = Get-Sha256 $inputManagerPath
            axisCount = $legacyAxes.Count
            axes = @($legacyAxes)
        }
        inputActionAssets = @(Get-InputActionAssets)
    }
    physics = [pscustomobject][ordered]@{
        physics3D = Get-PhysicsInventory -RelativePath 'ProjectSettings\DynamicsManager.asset' -LayerNames $layerNames
        physics2D = Get-PhysicsInventory -RelativePath 'ProjectSettings\Physics2DSettings.asset' -LayerNames $layerNames -Optional
    }
    invectorYaml = [pscustomobject][ordered]@{
        root = $vendorRelativeRoot.Replace('\', '/')
        candidateFileCount = $invectorYaml.candidateFileCount
        yamlFileCount = $invectorYaml.yamlFileCount
        skippedNonYamlFileCount = $invectorYaml.skippedNonYamlFileCount
        yamlFiles = @($invectorYaml.yamlFiles)
        skippedNonYamlFiles = @($invectorYaml.skippedNonYamlFiles)
        summary = [pscustomobject][ordered]@{
            gameObjectLayerAssignmentCount = @($invectorYaml.gameObjectLayers).Count
            gameObjectLayerCounts = @($layerCounts)
            layerMaskLikeFieldCount = @($invectorYaml.layerMaskLikeFields).Count
            layerMaskSemanticKindCounts = @($maskKindCounts)
        }
        maskValueSemantics = @($maskValueSemantics)
        gameObjectLayers = @($invectorYaml.gameObjectLayers)
        layerMaskLikeFields = @($invectorYaml.layerMaskLikeFields)
    }
}

$json = $report | ConvertTo-Json -Depth 20 -Compress
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Write-Output $json
    return
}

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $ProjectRoot $OutputPath))
}
$vendorPrefix = [IO.Path]::GetFullPath($vendorRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($resolvedOutput.StartsWith($vendorPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath must not be inside Assets/Invector-3rdPersonController.'
}
$outputParent = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
    throw "Output directory does not exist: $outputParent"
}
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($resolvedOutput, $json + [Environment]::NewLine, $utf8NoBom)
