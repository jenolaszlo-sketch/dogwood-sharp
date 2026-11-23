param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [string] $CedarSharpPackageDirectory,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,

    [Parameter(Mandatory)]
    [ValidateSet('net8.0', 'net10.0')]
    [string] $TargetFramework,

    [string] $WorkDirectory,

    [string] $DotNetPath = 'dotnet',

    [switch] $KeepWorkDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$expectedSdk = '10.0.404'
$candidateId = 'DogwoodSharp.CedarSharp'
$candidateVersion = '0.0.0-experimental.1'
$stableId = 'DogwoodSharp'
$stableVersion = '0.0.0-discovery.1'
$cedarVersion = '1.0.0'

$dogwoodImages = @{
    'win-x64' = 'dogwoodsharp_bridge.dll'
    'linux-x64' = 'libdogwoodsharp_bridge.so'
    'osx-arm64' = 'libdogwoodsharp_bridge.dylib'
}
$cedarImages = @{
    'win-x64' = 'cedarsharp_native.dll'
    'linux-x64' = 'libcedarsharp_native.so'
    'osx-arm64' = 'libcedarsharp_native.dylib'
}

function Invoke-Dotnet([string[]] $Arguments) {
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Require([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

function Get-LowerSha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ZipEntrySha256([string] $PackagePath, [string] $EntryPath) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq $EntryPath })
        Require ($entries.Count -eq 1) "The package does not contain exactly one $EntryPath entry."
        $stream = $entries[0].Open()
        try {
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
            }
            finally {
                $sha.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ZipEntryText([string] $PackagePath, [string] $EntryPath) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq $EntryPath })
        Require ($entries.Count -eq 1) "The package does not contain exactly one $EntryPath entry."
        $stream = $entries[0].Open()
        try {
            $reader = New-Object System.IO.StreamReader($stream, (New-Object System.Text.UTF8Encoding($false, $true)))
            try {
                return $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-PackageGraph([string] $AssetsPath, [string] $LockPath) {
    $assets = Get-Content -Raw -LiteralPath $AssetsPath | ConvertFrom-Json
    $matchingFrameworks = @($assets.project.frameworks.PSObject.Properties | Where-Object { $_.Name -eq $TargetFramework -or $_.Name.StartsWith("$TargetFramework/", [StringComparison]::Ordinal) })
    Require ($matchingFrameworks.Count -ge 1) "The restored assets have no $TargetFramework framework graph."
    foreach ($framework in $matchingFrameworks) {
        $directDependencies = @($framework.Value.dependencies.PSObject.Properties)
        $declaredDependencies = @($directDependencies | Where-Object {
            $automaticProperty = $_.Value.PSObject.Properties['autoReferenced']
            $null -eq $automaticProperty -or $automaticProperty.Value -ne $true
        })
        Require ($declaredDependencies.Count -eq 1) "The $TargetFramework consumer has more than one declared direct package dependency."
        Require ($declaredDependencies[0].Name -ceq $candidateId) "The only declared direct package dependency must be $candidateId."
        Require ([string] $declaredDependencies[0].Value.version -ceq "[$candidateVersion, $candidateVersion]") 'The direct candidate dependency range is not exact.'
        foreach ($automatic in @($directDependencies | Where-Object {
            $automaticProperty = $_.Value.PSObject.Properties['autoReferenced']
            $null -ne $automaticProperty -and $automaticProperty.Value -eq $true
        })) {
            Require ($automatic.Name -ceq 'Microsoft.NET.ILLink.Tasks' -or $automatic.Name -ceq 'Microsoft.DotNet.ILCompiler') "The consumer has an unexpected automatic package dependency: $($automatic.Name)."
        }
    }

    $libraryNames = @($assets.libraries.PSObject.Properties | ForEach-Object Name)
    foreach ($expected in @(
        "$candidateId/$candidateVersion",
        "$stableId/$stableVersion",
        "CedarSharp/$cedarVersion")) {
        Require ($libraryNames -ccontains $expected) "The restored package graph is missing $expected."
    }
    foreach ($library in @($assets.libraries.PSObject.Properties)) {
        Require ([string] $library.Value.type -cne 'project') "A project/source fallback is present in the package graph: $($library.Name)."
    }

    $lock = Get-Content -Raw -LiteralPath $LockPath | ConvertFrom-Json
    $lockFrameworks = @($lock.dependencies.PSObject.Properties | Where-Object { $_.Name -eq $TargetFramework -or $_.Name.StartsWith("$TargetFramework/", [StringComparison]::Ordinal) })
    Require ($lockFrameworks.Count -ge 1) "The disposable lockfile has no $TargetFramework graph."
    foreach ($framework in $lockFrameworks) {
        $dependencies = $framework.Value.PSObject.Properties
        $candidate = @($dependencies | Where-Object Name -CEQ $candidateId)
        $stable = @($dependencies | Where-Object Name -CEQ $stableId)
        $cedar = @($dependencies | Where-Object Name -CEQ 'CedarSharp')
        Require ($candidate.Count -eq 1 -and [string] $candidate[0].Value.type -ceq 'Direct') 'The candidate is not the sole direct lockfile dependency.'
        Require ($stable.Count -eq 1 -and [string] $stable[0].Value.type -ceq 'Transitive') 'The stable package is not an exact transitive lockfile dependency.'
        Require ($cedar.Count -eq 1 -and [string] $cedar[0].Value.type -ceq 'Transitive') 'CedarSharp is not an exact transitive lockfile dependency.'
        Require ([string] $stable[0].Value.resolved -ceq $stableVersion) 'The resolved stable package version is not exact.'
        Require ([string] $cedar[0].Value.resolved -ceq $cedarVersion) 'The resolved CedarSharp package version is not exact.'
    }
}

function Assert-ManifestAndImage(
    [string] $Directory,
    [string] $ManifestName,
    [string] $ImageName,
    [string] $ExpectedImageHash,
    [string] $ExpectedManifestHash) {
    # NuGet native assets can be consumed in either a flat root layout (NativeAOT publish)
    # or a nested runtimes/<rid>/native layout (JIT build). Resolve whichever is present.
    $nestedDirectory = Join-Path $Directory "runtimes/$RuntimeIdentifier/native"
    $resolvedDirectory = if (Test-Path -LiteralPath (Join-Path $Directory $ManifestName) -PathType Leaf) {
        $Directory
    }
    elseif (Test-Path -LiteralPath (Join-Path $nestedDirectory $ManifestName) -PathType Leaf) {
        $nestedDirectory
    }
    else {
        $Directory
    }

    $manifestPath = Join-Path $resolvedDirectory $ManifestName
    $imagePath = Join-Path $resolvedDirectory $ImageName
    Require (Test-Path -LiteralPath $manifestPath -PathType Leaf) "The published native manifest is missing: $manifestPath"
    Require (Test-Path -LiteralPath $imagePath -PathType Leaf) "The published native image is missing: $imagePath"

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $actualImageHash = Get-LowerSha256 $imagePath
    Require ([string] $manifest.sha256 -ceq $actualImageHash) "The published native image is stale relative to $manifestPath."
    Require ($actualImageHash -ceq $ExpectedImageHash) "The published native image differs from the package image: $imagePath."
    Require ((Get-LowerSha256 $manifestPath) -ceq $ExpectedManifestHash) "The published native manifest differs from the package manifest: $manifestPath."
}

function Assert-PublishedNativeSet(
    [string] $Directory,
    [string] $StablePackage,
    [string] $CedarPackage) {
    $dogwoodImage = $dogwoodImages[$RuntimeIdentifier]
    $cedarImage = $cedarImages[$RuntimeIdentifier]
    $dogwoodImageEntry = "runtimes/$RuntimeIdentifier/native/$dogwoodImage"
    $dogwoodManifestEntry = "runtimes/$RuntimeIdentifier/native/dogwoodsharp-native.json"
    $cedarImageEntry = "runtimes/$RuntimeIdentifier/native/$cedarImage"
    $cedarManifestEntry = "runtimes/$RuntimeIdentifier/native/cedarsharp-native.json"

    Assert-ManifestAndImage $Directory 'dogwoodsharp-native.json' $dogwoodImage `
        (Get-ZipEntrySha256 $StablePackage $dogwoodImageEntry) `
        (Get-ZipEntrySha256 $StablePackage $dogwoodManifestEntry)
    Assert-ManifestAndImage $Directory 'cedarsharp-native.json' $cedarImage `
        (Get-ZipEntrySha256 $CedarPackage $cedarImageEntry) `
        (Get-ZipEntrySha256 $CedarPackage $cedarManifestEntry)
}

$packageDirectoryPath = [System.IO.Path]::GetFullPath($PackageDirectory)
if (!(Test-Path -LiteralPath $packageDirectoryPath -PathType Container)) {
    throw "The uploaded candidate package directory is missing: $packageDirectoryPath"
}
$candidatePackage = Join-Path $packageDirectoryPath "$candidateId.$candidateVersion.nupkg"
$stablePackage = Join-Path $packageDirectoryPath "$stableId.$stableVersion.nupkg"
Require (Test-Path -LiteralPath $candidatePackage -PathType Leaf) "The candidate package is missing: $candidatePackage"
Require (Test-Path -LiteralPath $stablePackage -PathType Leaf) "The stable package is missing: $stablePackage"
$candidateHashBefore = Get-LowerSha256 $candidatePackage
$stableHashBefore = Get-LowerSha256 $stablePackage

$cedarPackage = $null
if ($CedarSharpPackageDirectory) {
    $cedarDirectoryPath = [System.IO.Path]::GetFullPath($CedarSharpPackageDirectory)
    $cedarCandidate = Join-Path $cedarDirectoryPath "cedarsharp.$cedarVersion.nupkg"
    if (Test-Path -LiteralPath $cedarCandidate -PathType Leaf) {
        $cedarPackage = $cedarCandidate
    }
}
if (!$cedarPackage) {
    throw "The original CedarSharp $cedarVersion package was not supplied in the uploaded dependency package directory."
}

$kitRoot = $PSScriptRoot
$projectSource = Join-Path $kitRoot 'D3.PackageConsumer.csproj'
$programSource = Join-Path $kitRoot 'Program.cs'
$globalJsonSource = Join-Path $kitRoot 'global.json'
foreach ($source in @($projectSource, $programSource, $globalJsonSource)) {
    Require (Test-Path -LiteralPath $source -PathType Leaf) "The consumer kit is incomplete: $source"
}

$projectText = Get-Content -Raw -LiteralPath $projectSource
if ($projectText -match 'ProjectReference|<Reference\b|HintPath|DOGWOOD\w*_NATIVE|runtimes[/\\]') {
    throw 'The consumer project contains a forbidden source, reference, native override, or manual staging path.'
}
$projectXml = [xml] $projectText
$packageReferences = @($projectXml.Project.ItemGroup.PackageReference)
Require ($packageReferences.Count -eq 1) 'The consumer must have exactly one PackageReference.'
Require ([string] $packageReferences[0].Include -ceq $candidateId) "The consumer's only PackageReference must be $candidateId."
Require ([string] $packageReferences[0].Version -ceq "[$candidateVersion]") 'The consumer candidate version is not exact.'

if (!$WorkDirectory) {
    if ($env:RUNNER_TEMP) {
        $workBase = $env:RUNNER_TEMP
    }
    elseif ($env:OS -eq 'Windows_NT') {
        $workBase = 'C:\Windows\Temp\opencode'
    }
    else {
        $workBase = [System.IO.Path]::GetTempPath()
    }
}
else {
    $workBase = [System.IO.Path]::GetFullPath($WorkDirectory)
}
New-Item -ItemType Directory -Force -Path $workBase | Out-Null
$cellWork = Join-Path $workBase "d3-package-consumer-$RuntimeIdentifier-$TargetFramework-$([Guid]::NewGuid().ToString('N'))"
$consumerRoot = Join-Path $cellWork 'consumer'
$nugetRoot = Join-Path $cellWork 'nuget'
$nugetPackages = Join-Path $nugetRoot 'packages'
$nugetHttpCache = Join-Path $nugetRoot 'http-cache'
$dotnetHome = Join-Path $cellWork 'dotnet-home'
New-Item -ItemType Directory -Force -Path $consumerRoot, $nugetRoot, $nugetPackages, $nugetHttpCache, $dotnetHome | Out-Null
Copy-Item -LiteralPath $projectSource, $programSource, $globalJsonSource -Destination $consumerRoot

$nugetConfigPath = Join-Path $cellWork 'NuGet.config'
$escapedCandidateSource = [System.Security.SecurityElement]::Escape($packageDirectoryPath)
$escapedCedarSource = [System.Security.SecurityElement]::Escape((Split-Path $cedarPackage -Parent))
$escapedPackages = [System.Security.SecurityElement]::Escape($nugetPackages)
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="d3-candidates" value="$escapedCandidateSource" />
    <add key="d3-cedarsharp" value="$escapedCedarSource" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value="$escapedPackages" />
  </config>
</configuration>
"@
Set-Content -LiteralPath $nugetConfigPath -Value $nugetConfig -Encoding utf8

$oldEnvironment = @{
    NUGET_PACKAGES = $env:NUGET_PACKAGES
    NUGET_HTTP_CACHE_PATH = $env:NUGET_HTTP_CACHE_PATH
    DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME
    DOTNET_NOLOGO = $env:DOTNET_NOLOGO
    DOTNET_CLI_TELEMETRY_OPTOUT = $env:DOTNET_CLI_TELEMETRY_OPTOUT
}
try {
    $env:NUGET_PACKAGES = $nugetPackages
    $env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache
    $env:DOTNET_CLI_HOME = $dotnetHome
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    Push-Location $consumerRoot
    try {
        $sdkVersion = (& $DotNetPath --version).Trim()
        Require ($LASTEXITCODE -eq 0 -and $sdkVersion -ceq $expectedSdk) "The package consumer requires .NET SDK $expectedSdk exactly; found '$sdkVersion'."
        Write-Host "D3_CONSUMER_SDK=$sdkVersion D3_CONSUMER_RID=$RuntimeIdentifier D3_CONSUMER_TFM=$TargetFramework"

        $project = Join-Path $consumerRoot 'D3.PackageConsumer.csproj'
        $jitLock = Join-Path $cellWork 'jit-package-lock.json'
        $aotLock = Join-Path $cellWork 'aot-package-lock.json'

        Invoke-Dotnet @('restore', $project, '--configfile', $nugetConfigPath, '--force-evaluate', '-p:PublishAot=false', "-p:NuGetLockFilePath=$jitLock")
        Invoke-Dotnet @('restore', $project, '--configfile', $nuGetConfigPath, '--locked-mode', '-p:PublishAot=false', "-p:NuGetLockFilePath=$jitLock")
        Assert-PackageGraph (Join-Path $consumerRoot 'obj/project.assets.json') $jitLock

        Invoke-Dotnet @('build', $project, '--configuration', 'Release', '--framework', $TargetFramework, '--no-restore', '-p:PublishAot=false', "-p:NuGetLockFilePath=$jitLock")
        $jitOutput = Join-Path $consumerRoot "bin/Release/$TargetFramework"
        $jitAssembly = Join-Path $jitOutput 'D3.PackageConsumer.dll'
        Require (Test-Path -LiteralPath $jitAssembly -PathType Leaf) "The JIT consumer assembly is missing: $jitAssembly"
        Assert-PublishedNativeSet $jitOutput $stablePackage $cedarPackage
        Invoke-Dotnet @($jitAssembly)

        Invoke-Dotnet @('restore', $project, '--runtime', $RuntimeIdentifier, '--configfile', $nugetConfigPath, '--force-evaluate', '-p:PublishAot=true', "-p:NuGetLockFilePath=$aotLock")
        Invoke-Dotnet @('restore', $project, '--runtime', $RuntimeIdentifier, '--configfile', $nugetConfigPath, '--locked-mode', '-p:PublishAot=true', "-p:NuGetLockFilePath=$aotLock")
        Assert-PackageGraph (Join-Path $consumerRoot 'obj/project.assets.json') $aotLock

        $publishDirectory = Join-Path $cellWork 'publish'
        Invoke-Dotnet @(
            'publish', $project,
            '--configuration', 'Release',
            '--framework', $TargetFramework,
            '--runtime', $RuntimeIdentifier,
            '--no-restore',
            '--output', $publishDirectory,
            '-p:PublishAot=true',
            '-p:TrimMode=full',
            "-p:NuGetLockFilePath=$aotLock")
        Assert-PublishedNativeSet $publishDirectory $stablePackage $cedarPackage

        $nativeExecutable = Join-Path $publishDirectory 'D3.PackageConsumer'
        if ($env:OS -eq 'Windows_NT') { $nativeExecutable += '.exe' }
        Require (Test-Path -LiteralPath $nativeExecutable -PathType Leaf) "The NativeAOT consumer executable is missing: $nativeExecutable"
        & $nativeExecutable
        if ($LASTEXITCODE -ne 0) { throw "The NativeAOT consumer failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }

    Require ((Get-LowerSha256 $candidatePackage) -ceq $candidateHashBefore) 'The candidate package changed during the consumer run.'
    Require ((Get-LowerSha256 $stablePackage) -ceq $stableHashBefore) 'The stable package changed during the consumer run.'
    Write-Host "D3_CONSUMER_RESULT=PASS RID=$RuntimeIdentifier TFM=$TargetFramework"
}
finally {
    $env:NUGET_PACKAGES = $oldEnvironment.NUGET_PACKAGES
    $env:NUGET_HTTP_CACHE_PATH = $oldEnvironment.NUGET_HTTP_CACHE_PATH
    $env:DOTNET_CLI_HOME = $oldEnvironment.DOTNET_CLI_HOME
    $env:DOTNET_NOLOGO = $oldEnvironment.DOTNET_NOLOGO
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $oldEnvironment.DOTNET_CLI_TELEMETRY_OPTOUT
    if (!$KeepWorkDirectory -and (Test-Path -LiteralPath $cellWork)) {
        Remove-Item -LiteralPath $cellWork -Recurse -Force
    }
}
