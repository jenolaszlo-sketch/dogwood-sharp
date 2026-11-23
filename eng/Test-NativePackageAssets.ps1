param(
    [Parameter(Mandatory)]
    [string] $AssetsRoot,

    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

if (!$RepositoryRoot) {
    $RepositoryRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '.'
}

$expectedDogwoodRevision = '929f49e7305534ac61ef06cf8e47269c596dc851'
$expectedDogwoodVersion = '1.1.2'
$expectedCedarVersion = '4.11.0'
$expectedRustVersion = '1.99.1'

$expected = [ordered]@{
    'win-x64' = [ordered]@{
        file = 'dogwoodsharp_bridge.dll'
        target = 'x86_64-pc-windows-msvc'
    }
    'linux-x64' = [ordered]@{
        file = 'libdogwoodsharp_bridge.so'
        target = 'x86_64-unknown-linux-gnu'
    }
    'osx-arm64' = [ordered]@{
        file = 'libdogwoodsharp_bridge.dylib'
        target = 'aarch64-apple-darwin'
    }
}

$manifestProperties = @(
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

function Assert-ExactSet([string[]] $Actual, [string[]] $Allowed, [string] $Description) {
    $actualSorted = @($Actual | Sort-Object -CaseSensitive)
    $allowedSorted = @($Allowed | Sort-Object -CaseSensitive)
    if (($actualSorted -join "`n") -cne ($allowedSorted -join "`n")) {
        throw "$Description. Actual: $($actualSorted -join ', '). Allowed: $($allowedSorted -join ', ')."
    }
}

function Assert-LockedPackage(
    [string] $LockText,
    [string] $PackageName,
    [string] $PackageVersion,
    [string] $ExpectedSource = '') {
    $escapedName = [regex]::Escape($PackageName)
    $escapedVersion = [regex]::Escape($PackageVersion)
    $pattern = "(?ms)^\[\[package\]\]\r?\nname = `"$escapedName`"\r?\nversion = `"$escapedVersion`"\r?\n(?:source = `"(?<source>[^`"]+)`"\r?\n)?"
    $matches = [regex]::Matches($LockText, $pattern)
    if ($matches.Count -ne 1) {
        throw "Cargo.lock must contain exactly one $PackageName $PackageVersion package entry; found $($matches.Count)."
    }

    if ($ExpectedSource) {
        $source = [string] $matches[0].Groups['source'].Value
        if ($source -cne $ExpectedSource) {
            throw "Cargo.lock has an unexpected source for $PackageName ${PackageVersion}: $source"
        }
    }
}

$assetsRootPath = [System.IO.Path]::GetFullPath($AssetsRoot)
$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$runtimesRoot = Join-Path $assetsRootPath 'runtimes'
if (!(Test-Path -LiteralPath $runtimesRoot -PathType Container)) {
    throw "The native package asset root is missing: $runtimesRoot"
}

$runtimeDirectories = @(Get-ChildItem -LiteralPath $runtimesRoot -Directory)
Assert-ExactSet @($runtimeDirectories | ForEach-Object Name) @($expected.Keys) 'Unexpected native runtime set'

$cargoLock = Join-Path $repositoryRootPath 'native/dogwoodsharp-bridge/Cargo.lock'
if (!(Test-Path -LiteralPath $cargoLock -PathType Leaf)) {
    throw "The pinned Cargo.lock is missing: $cargoLock"
}
$expectedCargoLockHash = Get-CanonicalCargoLockSha256 $cargoLock
$cargoLockText = [System.IO.File]::ReadAllText($cargoLock)
Assert-LockedPackage $cargoLockText 'dogwood-language' $expectedDogwoodVersion `
    "git+https://github.com/dogwood-policy/dogwood?rev=$expectedDogwoodRevision#$expectedDogwoodRevision"
foreach ($cedarPackage in @('cedar-policy', 'cedar-policy-core', 'cedar-policy-formatter')) {
    Assert-LockedPackage $cargoLockText $cedarPackage $expectedCedarVersion
}

$dogwoodSourcePins = @(
    [regex]::Matches($cargoLockText, '(?m)^source = "git\+https://github\.com/dogwood-policy/dogwood\?rev=(?<revision>[0-9a-f]{40})#[0-9a-f]{40}"$') |
        ForEach-Object { $_.Groups['revision'].Value } |
        Sort-Object -CaseSensitive -Unique)
if ($dogwoodSourcePins.Count -ne 1 -or $dogwoodSourcePins[0] -cne $expectedDogwoodRevision) {
    throw "Cargo.lock contains an unexpected Dogwood Git revision set: $($dogwoodSourcePins -join ', ')."
}

foreach ($rid in $expected.Keys) {
    $nativeDirectory = Join-Path (Join-Path $runtimesRoot $rid) 'native'
    if (!(Test-Path -LiteralPath $nativeDirectory -PathType Container)) {
        throw "The required native runtime directory is missing: $nativeDirectory"
    }

    $manifestPath = Join-Path $nativeDirectory 'dogwoodsharp-native.json'
    $imagePath = Join-Path $nativeDirectory $expected[$rid].file
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "The required native manifest is missing: $manifestPath"
    }
    if (!(Test-Path -LiteralPath $imagePath -PathType Leaf)) {
        throw "The required native image is missing: $imagePath"
    }

    $directoryFiles = @(Get-ChildItem -LiteralPath $nativeDirectory -File)
    Assert-ExactSet @($directoryFiles | ForEach-Object Name) @('dogwoodsharp-native.json', $expected[$rid].file) "Unexpected files in $nativeDirectory"

    $manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
    if ($manifestBytes.Length -le 0 -or $manifestBytes.Length -gt 4096) {
        throw "The native manifest has an invalid size: $manifestPath"
    }

    $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
    try {
        $manifestText = $strictUtf8.GetString($manifestBytes)
    }
    catch {
        throw "The native manifest is not valid UTF-8: $manifestPath"
    }

    # ConvertFrom-Json accepts duplicate names without reporting them. Count the member names
    # first so a duplicate cannot silently replace the value that is checked below.
    $memberMatches = [regex]::Matches(
        $manifestText,
        '(?m)"(?<name>[A-Za-z][A-Za-z0-9]*)"\s*:')
    $memberNames = @($memberMatches | ForEach-Object { $_.Groups['name'].Value })
    $duplicates = @($memberNames | Group-Object -CaseSensitive | Where-Object Count -ne 1)
    if ($duplicates.Count -ne 0) {
        throw "The native manifest contains duplicate or malformed member names: $manifestPath"
    }

    try {
        $manifest = $manifestText | ConvertFrom-Json
    }
    catch {
        throw "The native manifest is not valid JSON: $manifestPath"
    }

    Assert-ExactSet @($manifest.PSObject.Properties | ForEach-Object Name) $manifestProperties "Unexpected native manifest properties in $manifestPath"

    if ([int64] $manifest.manifestVersion -ne 1) { throw "Unexpected manifestVersion in $manifestPath" }
    if ([int64] $manifest.abiVersion -ne 1) { throw "Unexpected abiVersion in $manifestPath" }
    if ([string] $manifest.rid -cne $rid) { throw "Unexpected rid in $manifestPath" }
    if ([string] $manifest.file -cne $expected[$rid].file) { throw "Unexpected image name in $manifestPath" }
    if ([string] $manifest.target -cne $expected[$rid].target) { throw "Unexpected native target in $manifestPath" }
    if ([string] $manifest.bridgeVersion -cne '0.1.0') { throw "Unexpected bridgeVersion in $manifestPath" }
    if ([string] $manifest.dogwoodRevision -cne $expectedDogwoodRevision) { throw "Unexpected dogwoodRevision in $manifestPath" }
    if ([string] $manifest.dogwoodVersion -cne $expectedDogwoodVersion) { throw "Unexpected dogwoodVersion in $manifestPath" }
    if ([string] $manifest.cedarVersion -cne $expectedCedarVersion) { throw "Unexpected cedarVersion in $manifestPath" }
    if ([string] $manifest.rustVersion -cne $expectedRustVersion) { throw "Unexpected rustVersion in $manifestPath" }

    $dogwoodFeatures = @($manifest.dogwoodFeatures)
    if ($dogwoodFeatures.Count -ne 0) { throw "The Dogwood feature set differs in $manifestPath" }
    $cedarFeatures = @($manifest.cedarFeatures)
    if (($cedarFeatures -join ',') -cne 'datetime,decimal,ipaddr') {
        throw "The Cedar feature set differs in $manifestPath"
    }

    $manifestImageHash = [string] $manifest.sha256
    if ($manifestImageHash -cnotmatch '^[0-9a-f]{64}$') {
        throw "The native manifest has an invalid image hash: $manifestPath"
    }
    $actualImageHash = Get-LowerSha256 $imagePath
    if ($manifestImageHash -cne $actualImageHash) {
        throw "The native image is stale relative to its manifest: $imagePath"
    }

    $manifestCargoLockHash = [string] $manifest.cargoLockSha256
    if ($manifestCargoLockHash -cnotmatch '^[0-9a-f]{64}$') {
        throw "The native manifest has an invalid Cargo.lock hash: $manifestPath"
    }
    if ($manifestCargoLockHash -cne $expectedCargoLockHash) {
        throw "The native manifest is stale relative to the pinned Cargo.lock: $manifestPath"
    }

    Write-Host "Native package asset verified: $rid / $($expected[$rid].file) / $actualImageHash"
}

Write-Host 'All required Dogwood native package assets are present, paired, current, and restricted to the supported RIDs.'
