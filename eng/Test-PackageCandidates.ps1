param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [string] $CedarSharpPackagePath,

    [string] $RepositoryRoot,

    [string] $ReportPath,

    [switch] $RequireCedarSharpPackage
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

if (!$RepositoryRoot) {
    $RepositoryRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '.'
}

$stableId = 'DogwoodSharp'
$stableVersion = '0.1.0-preview.1'
$experimentalId = 'DogwoodSharp.CedarSharp'
$experimentalVersion = '0.1.0-experimental.1'
$tfms = @('net8.0', 'net10.0')

$nativeImages = [ordered]@{
    'win-x64' = 'dogwoodsharp_bridge.dll'
    'linux-x64' = 'libdogwoodsharp_bridge.so'
    'osx-arm64' = 'libdogwoodsharp_bridge.dylib'
}

$nativeTargets = [ordered]@{
    'win-x64' = 'x86_64-pc-windows-msvc'
    'linux-x64' = 'x86_64-unknown-linux-gnu'
    'osx-arm64' = 'aarch64-apple-darwin'
}

$cedarNativeImages = [ordered]@{
    'win-x64' = 'cedarsharp_native.dll'
    'linux-x64' = 'libcedarsharp_native.so'
    'osx-arm64' = 'libcedarsharp_native.dylib'
}

$cedarManifestProperties = @(
    'abiVersion',
    'bridgeVersion',
    'bridgeSourceSha256',
    'features',
    'lockSha256',
    'rid',
    'rustVersion',
    'sdkVersion',
    'sha256',
    'sourceCommit',
    'target'
)

$nativeManifestProperties = @(
    'abiVersion',
    'bridgeVersion',
    'cargoLockSha256',
    'cedarFeatures',
    'cedarVersion',
    'dogwoodFeatures',
    'dogwoodRevision',
    'dogwoodVersion',
    'file',
    'manifestVersion',
    'rid',
    'rustVersion',
    'sha256',
    'target'
)

function Get-LowerSha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-CanonicalCargoLockSha256([string] $Path) {
    $text = [System.IO.File]::ReadAllText($Path, (New-Object System.Text.UTF8Encoding($false, $true)))
    $canonical = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($hasher.ComputeHash(
            (New-Object System.Text.UTF8Encoding($false)).GetBytes($canonical)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-EntryBytes($Entry) {
    return [byte[]] $Entry.Bytes
}

function Get-EntrySha256($Entry) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash((Get-EntryBytes $Entry)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-EntryText($Entry) {
    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    try {
        return $utf8.GetString((Get-EntryBytes $Entry))
    }
    catch {
        throw "A package entry is not valid UTF-8: $($Entry.FullName)"
    }
}

function ConvertFrom-StrictJson([string] $Text, [string] $Description) {
    $memberMatches = [regex]::Matches(
        $Text,
        '(?m)"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:')
    $memberNames = @($memberMatches | ForEach-Object { $_.Groups['name'].Value })
    $duplicates = @($memberNames | Group-Object -CaseSensitive | Where-Object Count -ne 1)
    if ($duplicates.Count -ne 0) {
        throw "$Description contains duplicate or malformed member names."
    }

    try {
        return $Text | ConvertFrom-Json
    }
    catch {
        throw "$Description is not valid JSON."
    }
}

function Get-ArchiveEntries([string] $Path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @()
        foreach ($entry in @($archive.Entries | Where-Object { !$_.FullName.EndsWith('/') })) {
            $stream = $entry.Open()
            try {
                $memory = New-Object System.IO.MemoryStream
                try {
                    $stream.CopyTo($memory)
                    [byte[]] $bytes = $memory.ToArray()
                    $entries += [pscustomobject]@{
                        FullName = $entry.FullName
                        Bytes = $bytes
                    }
                }
                finally {
                    $memory.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }
        }
        return $entries
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-EntryMatchesFile($Entry, [string] $SourcePath, [string] $Description) {
    if (!(Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "The $Description source file is missing: $SourcePath"
    }

    $entryBytes = Get-EntryBytes $Entry
    $sourceBytes = [System.IO.File]::ReadAllBytes($SourcePath)
    if ($entryBytes.Length -ne $sourceBytes.Length) {
        throw "The packaged $Description differs from its repository source file."
    }

    for ($index = 0; $index -lt $entryBytes.Length; $index++) {
        if ($entryBytes[$index] -ne $sourceBytes[$index]) {
            throw "The packaged $Description differs from its repository source file."
        }
    }
}

function Get-NuspecMetadata([string] $NuspecText) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    try {
        # Strip a leading UTF-8 BOM if present: XmlDocument rejects a BOM
        # character in a string that also carries an XML encoding declaration.
        $xmlText = $NuspecText -replace '^\uFEFF', ''
        $xml.LoadXml($xmlText)
    }
    catch {
        throw 'The package nuspec is not valid XML.'
    }

    $idNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='id']")
    $versionNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']")
    if ($null -eq $idNode -or $null -eq $versionNode) {
        throw 'The package nuspec has no exact id or version.'
    }

    $dependencies = @()
    $groups = @($xml.SelectNodes("//*[local-name()='metadata']/*[local-name()='dependencies']/*[local-name()='group']"))
    foreach ($group in $groups) {
        $targetFramework = $group.GetAttribute('targetFramework')
        foreach ($dependency in @($group.SelectNodes("./*[local-name()='dependency']"))) {
            $dependencies += [pscustomobject]@{
                targetFramework = $targetFramework
                id = $dependency.GetAttribute('id')
                version = $dependency.GetAttribute('version')
            }
        }
    }

    return [pscustomobject]@{
        id = $idNode.InnerText
        version = $versionNode.InnerText
        dependencies = $dependencies
    }
}

function Assert-ExactEntrySet([string[]] $Actual, [string[]] $Allowed, [string] $Description) {
    $actualSorted = @($Actual | Sort-Object -CaseSensitive)
    $allowedSorted = @($Allowed | Sort-Object -CaseSensitive)
    if (($actualSorted -join "`n") -cne ($allowedSorted -join "`n")) {
        $unexpected = @($actualSorted | Where-Object { $_ -notin $allowedSorted })
        $missing = @($allowedSorted | Where-Object { $_ -notin $actualSorted })
        throw "$Description. Unexpected: $($unexpected -join ', '). Missing: $($missing -join ', ')."
    }
}

function Assert-NativeManifest(
    [string] $ManifestText,
    [string] $ImageHash,
    [string] $Rid,
    [string] $ExpectedFile,
    [string] $ExpectedCargoLockHash) {
    $manifest = ConvertFrom-StrictJson $ManifestText "The packaged native manifest for $Rid"
    Assert-ExactEntrySet @($manifest.PSObject.Properties | ForEach-Object Name) $nativeManifestProperties `
        "The packaged native manifest for $Rid has unexpected properties"

    if ([int64] $manifest.manifestVersion -ne 1) { throw "The packaged manifest has the wrong manifestVersion for $Rid." }
    if ([int64] $manifest.abiVersion -ne 1) { throw "The packaged manifest has the wrong abiVersion for $Rid." }
    if ([string] $manifest.rid -cne $Rid) { throw "The packaged manifest has the wrong RID for $Rid." }
    if ([string] $manifest.file -cne $ExpectedFile) { throw "The packaged manifest has the wrong image name for $Rid." }
    if ([string] $manifest.target -cne $nativeTargets[$Rid]) { throw "The packaged manifest has the wrong target for $Rid." }
    if ([string] $manifest.bridgeVersion -cne '0.1.0') { throw "The packaged manifest has the wrong bridgeVersion for $Rid." }
    if ([string] $manifest.dogwoodRevision -cne '929f49e7305534ac61ef06cf8e47269c596dc851') { throw "The packaged manifest has the wrong Dogwood revision for $Rid." }
    if ([string] $manifest.dogwoodVersion -cne '1.1.2') { throw "The packaged manifest has the wrong Dogwood version for $Rid." }
    if ([string] $manifest.cedarVersion -cne '4.11.0') { throw "The packaged manifest has the wrong Cedar version for $Rid." }
    if ([string] $manifest.rustVersion -cne '1.99.1') { throw "The packaged manifest has the wrong Rust version for $Rid." }
    if (@($manifest.dogwoodFeatures).Count -ne 0) { throw "The packaged manifest has an unexpected Dogwood feature set for $Rid." }
    if ((@($manifest.cedarFeatures) -join ',') -cne 'datetime,decimal,ipaddr') { throw "The packaged manifest has an unexpected Cedar feature set for $Rid." }

    if ([string] $manifest.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw "The packaged manifest has an invalid image hash for $Rid." }
    if ([string] $manifest.sha256 -cne $ImageHash) { throw "The packaged image is stale relative to its manifest for $Rid." }
    if ([string] $manifest.cargoLockSha256 -cnotmatch '^[0-9a-f]{64}$') { throw "The packaged manifest has an invalid Cargo.lock hash for $Rid." }
    if ([string] $manifest.cargoLockSha256 -cne $ExpectedCargoLockHash) { throw "The packaged manifest is stale relative to the pinned Cargo.lock for $Rid." }
}

function Assert-CedarNativeManifest(
    [string] $ManifestText,
    [string] $ImageHash,
    [string] $Rid,
    [string] $ExpectedFile) {
    $manifest = ConvertFrom-StrictJson $ManifestText "The CedarSharp native manifest for $Rid"
    Assert-ExactEntrySet @($manifest.PSObject.Properties | ForEach-Object Name) $cedarManifestProperties `
        "The CedarSharp native manifest for $Rid has unexpected properties"

    if ([int64] $manifest.abiVersion -ne 1) { throw "The CedarSharp manifest has the wrong ABI version for $Rid." }
    if ([string] $manifest.sdkVersion -cne '4.13.0') { throw "The CedarSharp manifest has the wrong SDK version for $Rid." }
    if ([string] $manifest.bridgeVersion -cne '0.1.0') { throw "The CedarSharp manifest has the wrong bridge version for $Rid." }
    if ([string] $manifest.rustVersion -cne '1.94.0') { throw "The CedarSharp manifest has the wrong Rust version for $Rid." }
    if ([string] $manifest.rid -cne $Rid) { throw "The CedarSharp manifest has the wrong RID for $Rid." }
    if ([string] $manifest.target -cne $nativeTargets[$Rid]) { throw "The CedarSharp manifest has the wrong target for $Rid." }
    if ((@($manifest.features) -join ',') -cne 'datetime,decimal,ipaddr') { throw "The CedarSharp manifest has an unexpected feature set for $Rid." }
    foreach ($hashName in @('sha256', 'lockSha256', 'bridgeSourceSha256')) {
        if ([string] $manifest.$hashName -cnotmatch '^[0-9a-f]{64}$') {
            throw "The CedarSharp manifest has an invalid $hashName for $Rid."
        }
    }
    if ([string] $manifest.sourceCommit -cnotmatch '^[0-9a-f]{40}$') {
        throw "The CedarSharp manifest has an invalid source commit for $Rid."
    }
    if ([string] $manifest.sha256 -cne $ImageHash) {
        throw "The CedarSharp native image is stale relative to its manifest for $Rid."
    }
}

$packageDirectoryPath = [System.IO.Path]::GetFullPath($PackageDirectory)
if (!(Test-Path -LiteralPath $packageDirectoryPath -PathType Container)) {
    throw "The package candidate directory is missing: $packageDirectoryPath"
}

$allPackageDirectoryFiles = @(Get-ChildItem -LiteralPath $packageDirectoryPath -File)
$packageFiles = @($allPackageDirectoryFiles | Where-Object { $_.Extension -ceq '.nupkg' })
$stableFileName = "$stableId.$stableVersion.nupkg"
$experimentalFileName = "$experimentalId.$experimentalVersion.nupkg"
$stablePackage = @($packageFiles | Where-Object Name -CEQ $stableFileName)
$experimentalPackage = @($packageFiles | Where-Object Name -CEQ $experimentalFileName)
if ($stablePackage.Count -ne 1) { throw "Expected exactly one $stableFileName candidate." }
if ($experimentalPackage.Count -ne 1) { throw "Expected exactly one $experimentalFileName candidate." }
if ($packageFiles.Count -ne 2) {
    throw "The candidate directory contains unexpected nupkg files: $(@($packageFiles | ForEach-Object Name) -join ', ')."
}
if ($allPackageDirectoryFiles.Count -ne 2) {
    throw "The candidate directory must contain exactly the two nupkg candidates: $(@($allPackageDirectoryFiles | ForEach-Object Name) -join ', ')."
}

$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$cargoLockPath = Join-Path $repositoryRootPath 'native/dogwoodsharp-bridge/Cargo.lock'
if (!(Test-Path -LiteralPath $cargoLockPath -PathType Leaf)) {
    throw "The pinned Cargo.lock is missing: $cargoLockPath"
}
$expectedCargoLockHash = Get-CanonicalCargoLockSha256 $cargoLockPath

$report = [ordered]@{
    cargoLockSha256 = $expectedCargoLockHash
    stable = [ordered]@{}
    experimental = [ordered]@{}
    cedarSharp = [ordered]@{}
}

foreach ($candidate in @(
    [pscustomobject]@{ kind = 'stable'; id = $stableId; version = $stableVersion; path = $stablePackage[0].FullName },
    [pscustomobject]@{ kind = 'experimental'; id = $experimentalId; version = $experimentalVersion; path = $experimentalPackage[0].FullName })) {
    $entries = @(Get-ArchiveEntries $candidate.path)
    $entryNames = @($entries | ForEach-Object FullName)
    $nuspecName = "$($candidate.id).nuspec"
    $nuspecEntry = @($entries | Where-Object FullName -CEQ $nuspecName)
    if ($nuspecEntry.Count -ne 1) { throw "The $($candidate.kind) package has no unique $nuspecName." }
    $metadata = Get-NuspecMetadata (Get-EntryText $nuspecEntry[0])
    if ([string] $metadata.id -cne $candidate.id) { throw "The $($candidate.kind) package ID is wrong." }
    if ([string] $metadata.version -cne $candidate.version) { throw "The $($candidate.kind) package version is wrong." }

    $coreProperties = @($entryNames | Where-Object { $_ -ceq 'package/services/metadata/core-properties/nuget.psmdcp' })
    if ($coreProperties.Count -ne 1) { throw "The $($candidate.kind) package has unexpected core-properties metadata." }

    $allowed = @(
        '[Content_Types].xml',
        '_rels/.rels',
        $nuspecName,
        $coreProperties[0],
        'README.md',
        'LICENSE',
        'NOTICE'
    )
    foreach ($tfm in $tfms) {
        $allowed += "lib/$tfm/$($candidate.id).dll"
        $allowed += "lib/$tfm/$($candidate.id).xml"
    }

    if ($candidate.kind -eq 'stable') {
        foreach ($rid in $nativeImages.Keys) {
            $allowed += "runtimes/$rid/native/$($nativeImages[$rid])"
            $allowed += "runtimes/$rid/native/dogwoodsharp-native.json"
        }
    }

    Assert-ExactEntrySet $entryNames $allowed "The $($candidate.kind) package contains unexpected or missing files"

    $readmeEntry = @($entries | Where-Object FullName -CEQ 'README.md')
    $licenseEntry = @($entries | Where-Object FullName -CEQ 'LICENSE')
    $noticeEntry = @($entries | Where-Object FullName -CEQ 'NOTICE')
    $readmeSource = if ($candidate.kind -eq 'stable') {
        Join-Path $repositoryRootPath 'README.md'
    }
    else {
        Join-Path $repositoryRootPath 'src/DogwoodSharp.CedarSharp/README.md'
    }
    Assert-EntryMatchesFile $readmeEntry[0] $readmeSource "$($candidate.kind) README"
    Assert-EntryMatchesFile $licenseEntry[0] (Join-Path $repositoryRootPath 'LICENSE') 'LICENSE'
    Assert-EntryMatchesFile $noticeEntry[0] (Join-Path $repositoryRootPath 'NOTICE') 'NOTICE'

    $packageRuntimes = @($entryNames | Where-Object { $_ -like 'runtimes/*' } | ForEach-Object { ($_ -split '/')[1] } | Sort-Object -Unique)
    $nativeReport = [ordered]@{}
    if ($candidate.kind -eq 'stable') {
        Assert-ExactEntrySet $packageRuntimes @($nativeImages.Keys) 'The stable package has an unexpected runtime set'
        foreach ($rid in $nativeImages.Keys) {
            $imagePath = "runtimes/$rid/native/$($nativeImages[$rid])"
            $manifestPath = "runtimes/$rid/native/dogwoodsharp-native.json"
            $imageEntry = @($entries | Where-Object FullName -CEQ $imagePath)
            $manifestEntry = @($entries | Where-Object FullName -CEQ $manifestPath)
            if ($imageEntry.Count -ne 1 -or $manifestEntry.Count -ne 1) { throw "The stable package is missing the $rid image/manifest pair." }
            $imageHash = Get-EntrySha256 $imageEntry[0]
            $manifestHash = Get-EntrySha256 $manifestEntry[0]
            Assert-NativeManifest (Get-EntryText $manifestEntry[0]) $imageHash $rid $nativeImages[$rid] $expectedCargoLockHash
            $nativeReport[$rid] = [ordered]@{
                file = $nativeImages[$rid]
                target = $nativeTargets[$rid]
                imageSha256 = $imageHash
                manifestSha256 = $manifestHash
            }
        }
    }
    elseif ($packageRuntimes.Count -ne 0) {
        throw 'The experimental package must not contain native runtime assets.'
    }

    $candidateReport = [ordered]@{
        id = [string] $metadata.id
        version = [string] $metadata.version
        sha256 = Get-LowerSha256 $candidate.path
        entries = @($entryNames | Sort-Object -CaseSensitive)
        dependencies = @($metadata.dependencies | ForEach-Object { "$($_.targetFramework):$($_.id):$($_.version)" })
        nativeAssets = $nativeReport
    }
    $report[$candidate.kind] = $candidateReport

    if ($candidate.kind -eq 'stable') {
        if ($metadata.dependencies.Count -ne 0) {
            throw "The stable package has unexpected dependencies: $(@($metadata.dependencies | ForEach-Object { $_.id }) -join ', ')."
        }
    }
    else {
        $groups = @($metadata.dependencies | ForEach-Object targetFramework | Sort-Object -Unique)
        Assert-ExactEntrySet $groups $tfms 'The experimental package has unexpected dependency target frameworks'
        foreach ($tfm in $tfms) {
            $groupDependencies = @($metadata.dependencies | Where-Object targetFramework -CEQ $tfm)
            if ($groupDependencies.Count -ne 2) { throw "The experimental package does not have exactly two dependencies for $tfm." }
            $stableDependency = @($groupDependencies | Where-Object id -CEQ $stableId)
            $cedarDependency = @($groupDependencies | Where-Object id -CEQ 'CedarSharp')
            if ($stableDependency.Count -ne 1 -or [string] $stableDependency[0].version -cne "[$stableVersion]") {
                throw "The experimental package dependency on $stableId is not exact for $tfm."
            }
            if ($cedarDependency.Count -ne 1 -or [string] $cedarDependency[0].version -cne '[1.0.0]') {
                throw "The experimental package dependency on CedarSharp is not exact for $tfm."
            }
        }
    }

    Write-Host "Package candidate verified: $($candidate.id) $($candidate.version) / $($candidateReport.sha256)"
}

if (!$CedarSharpPackagePath) {
    $nugetRoot = $env:NUGET_PACKAGES
    if (!$nugetRoot) {
        $nugetRoot = Join-Path $HOME '.nuget/packages'
    }
    $CedarSharpPackagePath = Join-Path (Join-Path (Join-Path $nugetRoot 'cedarsharp') '1.0.0') 'cedarsharp.1.0.0.nupkg'
}

if (Test-Path -LiteralPath $CedarSharpPackagePath -PathType Leaf) {
    $cedarPath = [System.IO.Path]::GetFullPath($CedarSharpPackagePath)
    $hashManifestPath = Join-Path $PSScriptRoot 'CedarSharp-1.0.0.package-hashes.json'
    $expected = ConvertFrom-StrictJson (Get-Content -Raw -LiteralPath $hashManifestPath) 'The CedarSharp hash manifest'
    Assert-ExactEntrySet @($expected.PSObject.Properties | ForEach-Object Name) `
        @('packageId', 'packageVersion', 'packageSha256', 'entryCount', 'entriesSha256') `
        'The CedarSharp hash manifest has unexpected properties'
    if ([string] $expected.packageId -cne 'CedarSharp' -or [string] $expected.packageVersion -cne '1.0.0') {
        throw 'The CedarSharp hash manifest does not pin CedarSharp 1.0.0.'
    }
    if ([string] $expected.packageSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        [string] $expected.entriesSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        [int] $expected.entryCount -le 0) {
        throw 'The CedarSharp hash manifest contains invalid pinned hashes or entry count.'
    }
    $actualCedarHash = Get-LowerSha256 $cedarPath
    if ($actualCedarHash -cne [string] $expected.packageSha256) {
        throw "The CedarSharp 1.0.0 package hash is not the pinned value: $cedarPath"
    }

    $cedarEntries = @(Get-ArchiveEntries $cedarPath)
    [string[]] $cedarEntryNames = @($cedarEntries | ForEach-Object FullName)
    if ($cedarEntryNames.Count -ne [int] $expected.entryCount) {
        throw "The CedarSharp 1.0.0 package entry count differs from the pinned value."
    }
    [Array]::Sort($cedarEntryNames, [StringComparer]::Ordinal)
    $entryManifest = New-Object System.Text.StringBuilder
    foreach ($name in $cedarEntryNames) {
        $entry = @($cedarEntries | Where-Object FullName -CEQ $name)
        if ($entry.Count -ne 1) { throw "The CedarSharp package has a duplicate entry: $name" }
        [void] $entryManifest.Append((Get-EntrySha256 $entry[0])).Append('  ').Append($name).Append("`n")
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $aggregateHasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $aggregateHash = ([BitConverter]::ToString($aggregateHasher.ComputeHash($utf8.GetBytes($entryManifest.ToString())))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $aggregateHasher.Dispose()
    }
    if ($aggregateHash -cne [string] $expected.entriesSha256) {
        throw 'The CedarSharp 1.0.0 package contents differ from the pinned entry hashes.'
    }

    $cedarNuspec = @($cedarEntries | Where-Object { $_.FullName -ieq 'CedarSharp.nuspec' })
    if ($cedarNuspec.Count -ne 1) { throw 'The CedarSharp package has no unique nuspec.' }
    $cedarMetadata = Get-NuspecMetadata (Get-EntryText $cedarNuspec[0])
    if ([string] $cedarMetadata.id -cne 'CedarSharp' -or [string] $cedarMetadata.version -cne '1.0.0') {
        throw 'The locally available CedarSharp package does not have the exact pinned ID and version.'
    }

    $cedarNativeReport = [ordered]@{}
    foreach ($rid in $cedarNativeImages.Keys) {
        $imagePath = "runtimes/$rid/native/$($cedarNativeImages[$rid])"
        $manifestPath = "runtimes/$rid/native/cedarsharp-native.json"
        $imageEntry = @($cedarEntries | Where-Object FullName -CEQ $imagePath)
        $manifestEntry = @($cedarEntries | Where-Object FullName -CEQ $manifestPath)
        if ($imageEntry.Count -ne 1 -or $manifestEntry.Count -ne 1) {
            throw "The CedarSharp package is missing the $rid image/manifest pair."
        }
        $imageHash = Get-EntrySha256 $imageEntry[0]
        $manifestHash = Get-EntrySha256 $manifestEntry[0]
        Assert-CedarNativeManifest (Get-EntryText $manifestEntry[0]) $imageHash $rid $cedarNativeImages[$rid]
        $cedarNativeReport[$rid] = [ordered]@{
            file = $cedarNativeImages[$rid]
            target = $nativeTargets[$rid]
            imageSha256 = $imageHash
            manifestSha256 = $manifestHash
        }
    }

    $report.cedarSharp = [ordered]@{
        id = [string] $cedarMetadata.id
        version = [string] $cedarMetadata.version
        sha256 = $actualCedarHash
        entryCount = $cedarEntryNames.Count
        entriesSha256 = $aggregateHash
        nativeAssets = $cedarNativeReport
    }
    Write-Host "CedarSharp package verified: 1.0.0 / $actualCedarHash / $($cedarEntryNames.Count) entries"
}
elseif ($RequireCedarSharpPackage) {
    throw "The exact CedarSharp 1.0.0 package is required but was not found locally: $CedarSharpPackagePath"
}
else {
    Write-Warning "CedarSharp 1.0.0 is not locally available; its exact package contents and hashes were not verified in this run: $CedarSharpPackagePath"
    $report.cedarSharp = [ordered]@{ verified = $false; path = $CedarSharpPackagePath }
}

if ($ReportPath) {
    $reportPathFull = [System.IO.Path]::GetFullPath($ReportPath)
    $reportDirectory = Split-Path $reportPathFull -Parent
    if ($reportDirectory) { New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPathFull -Encoding utf8
}

Write-Host 'Package candidate inspection passed.'
