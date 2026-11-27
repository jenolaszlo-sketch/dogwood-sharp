param(
    [Parameter(Mandatory)]
    [string] $OutputPath,

    [string] $DependencyOutputPath,

    [string] $DotNetPath = 'dotnet',

    [string] $NativeAssetsRoot,

    [string] $RepositoryRoot,

    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

if (!$RepositoryRoot) {
    $RepositoryRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '.'
}

$expectedSdk = '10.0.404'
$stableId = 'DogwoodSharp'
$stableVersion = '0.1.0-preview.1'
$experimentalId = 'DogwoodSharp.CedarSharp'
$experimentalVersion = '0.1.0-experimental.1'

function Invoke-Dotnet([string[]] $Arguments) {
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-LowerSha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
if (!$NativeAssetsRoot) {
    $NativeAssetsRoot = Join-Path $repositoryRootPath 'artifacts/native'
}
$nativeAssetsRootPath = [System.IO.Path]::GetFullPath($NativeAssetsRoot)
$outputPathFull = [System.IO.Path]::GetFullPath($OutputPath)

Push-Location $repositoryRootPath
try {
    $globalJsonPath = Join-Path $repositoryRootPath 'global.json'
    if (!(Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
        throw "The pinned global.json is missing: $globalJsonPath"
    }
    $globalJson = Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json
    if ([string] $globalJson.sdk.version -cne $expectedSdk -or [string] $globalJson.sdk.rollForward -cne 'disable') {
        throw "global.json must pin SDK $expectedSdk with rollForward=disable."
    }

    $sdkVersion = (& $DotNetPath --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -cne $expectedSdk) {
        throw "The package candidate gate requires .NET SDK $expectedSdk exactly; found '$sdkVersion'."
    }
    Write-Host "Using .NET SDK $sdkVersion"

    $packageShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue
    $packageShellArguments = ''
    if ($null -eq $packageShell) {
        # Windows PowerShell is a local-only compatibility path for hosts without PowerShell 7.
        # Hosted CI always takes the pwsh path above.
        $packageShell = Get-Command powershell -CommandType Application -ErrorAction SilentlyContinue
        $packageShellArguments = '-ExecutionPolicy Bypass'
    }
    if ($null -eq $packageShell) {
        throw 'PowerShell 7 (pwsh) is required to run the package candidate gate.'
    }
    $packageShellPath = @($packageShell)[0].Source

    & (Join-Path $PSScriptRoot 'Test-NativePackageAssets.ps1') -AssetsRoot $nativeAssetsRootPath -RepositoryRoot $repositoryRootPath

    New-Item -ItemType Directory -Force -Path $outputPathFull | Out-Null
    # Remove any prior candidate from this directory, including an older
    # release identity, so the verification gate sees exactly this run's files.
    Get-ChildItem -LiteralPath $outputPathFull -File |
        Where-Object {
            $_.Name -like "$stableId.*.nupkg" -or
            $_.Name -like "$stableId.*.snupkg" -or
            $_.Name -like "$experimentalId.*.nupkg" -or
            $_.Name -like "$experimentalId.*.snupkg"
        } |
        Remove-Item -Force

    $stableProject = Join-Path $repositoryRootPath 'src/DogwoodSharp/DogwoodSharp.csproj'
    $experimentalProject = Join-Path $repositoryRootPath 'src/DogwoodSharp.CedarSharp/DogwoodSharp.CedarSharp.csproj'
    $stableLock = Join-Path $repositoryRootPath 'src/DogwoodSharp/packages.lock.json'
    $experimentalLock = Join-Path $repositoryRootPath 'src/DogwoodSharp.CedarSharp/packages.lock.json'
    foreach ($lock in @($stableLock, $experimentalLock)) {
        if (!(Test-Path -LiteralPath $lock -PathType Leaf)) {
            throw "The required locked dependency graph is missing: $lock"
        }
    }
    $stableLockHash = Get-LowerSha256 $stableLock
    $experimentalLockHash = Get-LowerSha256 $experimentalLock

    Invoke-Dotnet @('restore', $stableProject, '--locked-mode')
    Invoke-Dotnet @('restore', $experimentalProject, '--locked-mode')
    Invoke-Dotnet @(
        'pack', $stableProject,
        '--configuration', 'Release',
        '--no-restore',
        '--output', $outputPathFull,
        '-p:ContinuousIntegrationBuild=true',
        '-p:DeterministicTimestamp=true',
        '-p:IncludeSymbols=false',
        # The release package version is a pack-time identity. The checked-in project
        # Version stays at the qualification-candidate value so historical probe
        # lockfiles and the qualified restore graph remain reproducible.
        "-p:PackageVersion=$stableVersion",
        "-p:DogwoodSharpNativeAssetsRoot=$nativeAssetsRootPath",
        "-p:DogwoodSharpPackageShell=$packageShellPath",
        "-p:DogwoodSharpPackageShellArguments=$packageShellArguments")
    Invoke-Dotnet @(
        'pack', $experimentalProject,
        '--configuration', 'Release',
        '--no-restore',
        '--output', $outputPathFull,
        '-p:ContinuousIntegrationBuild=true',
        '-p:DeterministicTimestamp=true',
        '-p:IncludeSymbols=false',
        "-p:PackageVersion=$experimentalVersion",
        "-p:DogwoodSharpNativeAssetsRoot=$nativeAssetsRootPath",
        "-p:DogwoodSharpPackageShell=$packageShellPath",
        "-p:DogwoodSharpPackageShellArguments=$packageShellArguments")

    $stablePackage = Join-Path $outputPathFull "$stableId.$stableVersion.nupkg"
    $experimentalPackage = Join-Path $outputPathFull "$experimentalId.$experimentalVersion.nupkg"
    if (!(Test-Path -LiteralPath $stablePackage -PathType Leaf)) {
        throw "The stable package candidate was not produced: $stablePackage"
    }
    if (!(Test-Path -LiteralPath $experimentalPackage -PathType Leaf)) {
        throw "The experimental package candidate was not produced: $experimentalPackage"
    }
    if ((Get-LowerSha256 $stableLock) -cne $stableLockHash -or
        (Get-LowerSha256 $experimentalLock) -cne $experimentalLockHash) {
        throw 'A locked dependency graph changed during package production.'
    }

    # NuGet's ZIP writer records the wall-clock entry time even when deterministic builds are
    # enabled. Normalize only our unsigned candidates to a fixed DOS timestamp so repeated
    # producer runs have byte-identical package hashes. The original CedarSharp dependency is
    # copied separately and is never rewritten.
    $normalizerProject = Join-Path $PSScriptRoot 'PackageNormalizer/PackageNormalizer.csproj'
    Invoke-Dotnet @(
        'run', '--project', $normalizerProject,
        '--configuration', 'Release',
        '--',
        $stablePackage,
        $experimentalPackage)

    $nugetRoot = $env:NUGET_PACKAGES
    if (!$nugetRoot) {
        $nugetRoot = Join-Path $HOME '.nuget/packages'
    }
    $cedarPackage = Join-Path (Join-Path (Join-Path $nugetRoot 'cedarsharp') '1.0.0') 'cedarsharp.1.0.0.nupkg'
    if (!(Test-Path -LiteralPath $cedarPackage -PathType Leaf)) {
        throw "The restored CedarSharp 1.0.0 package is required but was not found: $cedarPackage"
    }
    $cedarHashManifest = Join-Path $PSScriptRoot 'CedarSharp-1.0.0.package-hashes.json'
    if (!(Test-Path -LiteralPath $cedarHashManifest -PathType Leaf)) {
        throw "The pinned CedarSharp 1.0.0 hash manifest is missing: $cedarHashManifest"
    }
    $cedarForInspection = $cedarPackage
    if ($DependencyOutputPath) {
        $dependencyOutputPathFull = [System.IO.Path]::GetFullPath($DependencyOutputPath)
        New-Item -ItemType Directory -Force -Path $dependencyOutputPathFull | Out-Null
        $copiedCedarPackage = Join-Path $dependencyOutputPathFull 'cedarsharp.1.0.0.nupkg'
        $copiedCedarHashManifest = Join-Path $dependencyOutputPathFull 'CedarSharp-1.0.0.package-hashes.json'
        foreach ($copied in @($copiedCedarPackage, $copiedCedarHashManifest)) {
            if (Test-Path -LiteralPath $copied) {
                Remove-Item -LiteralPath $copied -Force
            }
        }
        Copy-Item -LiteralPath $cedarPackage -Destination $copiedCedarPackage
        Copy-Item -LiteralPath $cedarHashManifest -Destination $copiedCedarHashManifest
        $cedarForInspection = $copiedCedarPackage
    }

    $inspectionParameters = @{
        PackageDirectory = $outputPathFull
        RepositoryRoot = $repositoryRootPath
        CedarSharpPackagePath = $cedarForInspection
        RequireCedarSharpPackage = $true
    }
    if ($ReportPath) {
        $inspectionParameters.ReportPath = $ReportPath
    }
    & (Join-Path $PSScriptRoot 'Test-PackageCandidates.ps1') @inspectionParameters
}
finally {
    Pop-Location
}
