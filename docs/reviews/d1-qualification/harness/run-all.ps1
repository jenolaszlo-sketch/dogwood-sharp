#!/usr/bin/env pwsh
# Independent D1 qualification runner. Reproduces the producer build in the
# selected RID, then runs the qualification-only C ABI consumer, upstream
# parser differential, managed API/loader matrix, and AOT smoke.
#
# This script never modifies production sources. Harness outputs go under
# artifacts/d1-qualification (ignored) unless -OutputRoot is changed.

param(
    [string] $RuntimeIdentifier,
    [string] $AssetsRoot,
    [string] $OutputRoot = 'artifacts/d1-qualification',
    [string[]] $Frameworks = @('net8.0', 'net10.0'),
    [bool] $Valgrind = $false,
    [switch] $SkipAot,
    [switch] $SkipDifferential,
    [switch] $SkipManaged,
    [switch] $SkipNativeBuild
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

if (!$RuntimeIdentifier) {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    $RuntimeIdentifier = switch ("$IsWindows,$IsLinux,$IsMacOS,$architecture") {
        'True,False,False,X64' { 'win-x64' }
        'False,True,False,X64' { 'linux-x64' }
        'False,False,True,Arm64' { 'osx-arm64' }
        default { throw "No D1 qualification profile for this host ($architecture)." }
    }
}

$target, $image, $fakeImage, $fakeMinimal = switch ($RuntimeIdentifier) {
    'win-x64' { 'x86_64-pc-windows-msvc', 'dogwoodsharp_bridge.dll', 'fake_bridge.dll', 'fake_minimal.dll' }
    'linux-x64' { 'x86_64-unknown-linux-gnu', 'libdogwoodsharp_bridge.so', 'libfake_bridge.so', 'libfake_minimal.so' }
    'osx-arm64' { 'aarch64-apple-darwin', 'libdogwoodsharp_bridge.dylib', 'libfake_bridge.dylib', 'libfake_minimal.dylib' }
    default { throw "Unsupported runtime identifier '$RuntimeIdentifier'." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$harness = $PSScriptRoot
$nativeSource = Join-Path $harness 'native'
$managedProject = Join-Path $harness 'managed/QualificationHarness.csproj'
$managedOutput = Join-Path $harness 'managed/bin/Release'
$aotProject = Join-Path $harness 'aot/D1QualificationAot.csproj'
$oracleProject = Join-Path $harness 'oracle/Cargo.toml'
$cargoLock = Join-Path $root 'native/dogwoodsharp-bridge/Cargo.lock'
$pythonScript = Join-Path $harness 'differential.py'

$out = Join-Path $root $OutputRoot
$logDir = Join-Path $out 'logs'
$binDir = Join-Path $out "bin/$RuntimeIdentifier"
$objDir = Join-Path $out "obj/$RuntimeIdentifier"
$reportDir = Join-Path $out "reports/$RuntimeIdentifier"
$workDir = Join-Path $out "work/$RuntimeIdentifier"
foreach ($directory in @($out, $logDir, $binDir, $objDir, $reportDir, $workDir)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

if (!$AssetsRoot) {
    if ($SkipNativeBuild) {
        throw '-SkipNativeBuild requires -AssetsRoot.'
    }
    $AssetsRoot = Join-Path $root 'artifacts/native'
    & (Join-Path $root 'eng/Build-Native.ps1') -RuntimeIdentifier $RuntimeIdentifier
}

$assets = (Resolve-Path $AssetsRoot).Path
$nativeDir = Join-Path $assets "runtimes/$RuntimeIdentifier/native"
$imagePath = Join-Path $nativeDir $image
if (!(Test-Path $imagePath)) {
    throw "Native image not found: $imagePath"
}

function Resolve-Tool([string] $Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) {
        throw "Required tool '$Name' is not on PATH."
    }
    return $command.Source
}

function Invoke-Tool {
    param([string] $Name, [string] $FilePath, [string[]] $Arguments)
    $log = Join-Path $logDir "$Name.log"
    Write-Host "== $Name"
    & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "$Name failed with exit code $code. See $log."
    }
}

function Write-JsonFile([string] $Path, $Value) {
    $text = $Value | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $text + "`n", [System.Text.UTF8Encoding]::new($false))
}

function Assert-ProductionTreeUnchanged {
    $diff = & git -C $root diff --name-only
    $staged = & git -C $root diff --cached --name-only
    if ($LASTEXITCODE -ne 0) {
        throw 'git diff failed while checking production sources.'
    }
    $changed = @(@($diff) + @($staged) | Where-Object { $_ })
    if ($changed.Count -ne 0) {
        throw "Tracked files changed during qualification: $($changed -join ', ')"
    }
}

# --- Artifact manifest verification (independent of the managed loader) ---
$manifestPath = Join-Path $nativeDir 'dogwoodsharp-native.json'
$manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
if ($manifestBytes.Length -lt 1 -or $manifestBytes.Length -gt 4096) {
    throw "Manifest size $($manifestBytes.Length) is outside 1..4096 bytes."
}
$manifestText = [System.Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
$manifest = $manifestText | ConvertFrom-Json -AsHashtable
$expectedFields = @(
    'manifestVersion', 'rid', 'file', 'sha256', 'cargoLockSha256', 'abiVersion',
    'bridgeVersion', 'dogwoodRevision', 'dogwoodVersion', 'cedarVersion',
    'rustVersion', 'target', 'dogwoodFeatures', 'cedarFeatures'
)
$actualFields = @($manifest.Keys | Sort-Object)
if (($actualFields -join '|') -ne (($expectedFields | Sort-Object) -join '|')) {
    throw "Manifest fields differ: $($actualFields -join ', ')."
}
$expectedIdentity = @{
    manifestVersion = 1
    rid = $RuntimeIdentifier
    file = $image
    abiVersion = 1
    bridgeVersion = '0.1.0'
    dogwoodRevision = '929f49e7305534ac61ef06cf8e47269c596dc851'
    dogwoodVersion = '1.1.2'
    cedarVersion = '4.11.0'
    rustVersion = '1.99.1'
    target = $target
}
foreach ($name in $expectedIdentity.Keys) {
    if ($manifest[$name] -ne $expectedIdentity[$name]) {
        throw "Manifest field $name is '$($manifest[$name])', expected '$($expectedIdentity[$name])'."
    }
}
if (@($manifest.dogwoodFeatures).Count -ne 0) {
    throw 'Manifest dogwoodFeatures is not empty.'
}
if ((@($manifest.cedarFeatures) -join ',') -ne 'datetime,decimal,ipaddr') {
    throw "Manifest cedarFeatures is '$(@($manifest.cedarFeatures) -join ',')'."
}
$imageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $imagePath).Hash.ToLowerInvariant()
$cargoHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cargoLock).Hash.ToLowerInvariant()
if ($manifest.sha256 -cne $imageHash) {
    throw "Image SHA-256 '$imageHash' differs from manifest '$($manifest.sha256)'."
}
if ($manifest.cargoLockSha256 -cne $cargoHash) {
    throw "Cargo.lock SHA-256 '$cargoHash' differs from manifest '$($manifest.cargoLockSha256)'."
}
Write-JsonFile (Join-Path $reportDir 'manifest-verification.json') ([ordered]@{
    result = 'pass'
    manifestBytes = $manifestBytes.Length
    image = $image
    imageSha256 = $imageHash
    cargoLockSha256 = $cargoHash
    fields = @($actualFields)
})

# --- Reproduce the producer toolchain gates for this RID ---
if (!$SkipNativeBuild) {
    Invoke-Tool 'cargo-fmt' (Resolve-Tool 'cargo') @(
        'fmt', '--manifest-path', (Join-Path $root 'native/dogwoodsharp-bridge/Cargo.toml'), '--check')
    Invoke-Tool 'cargo-clippy' (Resolve-Tool 'cargo') @(
        'clippy', '--locked', '--release', '--manifest-path',
        (Join-Path $root 'native/dogwoodsharp-bridge/Cargo.toml'), '--all-targets', '--', '-D', 'warnings')
    Invoke-Tool 'cargo-test' (Resolve-Tool 'cargo') @(
        'test', '--locked', '--release', '--manifest-path',
        (Join-Path $root 'native/dogwoodsharp-bridge/Cargo.toml'), '--', '--nocapture')
}

# --- Independent C ABI consumer and controllable fakes ---
$probe = Join-Path $binDir ($IsWindows ? 'abi_probe.exe' : 'abi_probe')
$fake = Join-Path $binDir $fakeImage
$minimal = Join-Path $binDir $fakeMinimal
Push-Location $objDir
try {
    if ($IsWindows) {
        $compiler = Resolve-Tool 'cl'
        Invoke-Tool 'build-abi-probe' $compiler @(
            '/nologo', '/std:c11', '/O2', '/W4', '/WX', "/Fe:$probe", (Join-Path $nativeSource 'abi_probe.c'))
        Invoke-Tool 'build-fake-bridge' $compiler @(
            '/nologo', '/std:c11', '/O2', '/W4', '/WX', '/LD', '/MT', "/Fe:$fake",
            (Join-Path $nativeSource 'fake_bridge.c'))
        Invoke-Tool 'build-fake-minimal' $compiler @(
            '/nologo', '/std:c11', '/O2', '/W4', '/WX', '/LD', '/MT', "/Fe:$minimal",
            (Join-Path $nativeSource 'fake_minimal.c'))
    }
    else {
        $compiler = Resolve-Tool ($env:CC ? $env:CC : 'cc')
        $common = @('-std=c11', '-O2', '-Wall', '-Wextra', '-Werror',
            '-D_DEFAULT_SOURCE', '-D_POSIX_C_SOURCE=200809L')
        $link = if ($IsLinux) { @('-pthread', '-ldl') } else { @('-pthread') }
        Invoke-Tool 'build-abi-probe' $compiler ($common + $link + @(
            '-o', $probe, (Join-Path $nativeSource 'abi_probe.c')))
        $shared = if ($IsMacOS) { '-dynamiclib' } else { '-shared' }
        Invoke-Tool 'build-fake-bridge' $compiler ($common + @('-fPIC', $shared, '-o', $fake,
            (Join-Path $nativeSource 'fake_bridge.c')))
        Invoke-Tool 'build-fake-minimal' $compiler ($common + @('-fPIC', $shared, '-o', $minimal,
            (Join-Path $nativeSource 'fake_minimal.c')))
    }
}
finally {
    Pop-Location
}

Invoke-Tool 'native-selftest' $probe @('selftest', $imagePath, $target)

if ($Valgrind) {
    if (!$IsLinux) {
        throw '-Valgrind is only supported on Linux.'
    }
    $valgrind = Resolve-Tool 'valgrind'
    $valgrindLog = Join-Path $logDir 'native-selftest-valgrind.log'
    Invoke-Tool 'native-selftest-valgrind' $valgrind @(
        '--error-exitcode=1', '--leak-check=full', '--errors-for-leak-kinds=definite,indirect',
        "--log-file=$valgrindLog", $probe, 'selftest', $imagePath, $target)
    $memory = Get-Content -LiteralPath $valgrindLog -Raw
    if ($memory -notmatch 'ERROR SUMMARY: 0 errors') {
        throw "Valgrind reported errors. See $valgrindLog."
    }
    if ($memory -notmatch 'definitely lost: 0 bytes') {
        throw "Valgrind reported definite leaks. See $valgrindLog."
    }
    Write-JsonFile (Join-Path $reportDir 'valgrind-summary.json') ([ordered]@{
        result = 'pass'
        errorSummary = '0 errors'
        definitelyLost = '0 bytes'
        log = 'logs/native-selftest-valgrind.log'
    })
}

# --- Independent upstream-parser differential ---
if (!$SkipDifferential) {
    $oracleDir = Join-Path $harness 'oracle'
    Invoke-Tool 'build-oracle' (Resolve-Tool 'cargo') @(
        'build', '--release', '--locked', '--manifest-path', $oracleProject)
    $oracleName = if ($IsWindows) {
        'target/release/d1-qualification-oracle.exe'
    }
    else {
        'target/release/d1-qualification-oracle'
    }
    $oracle = Join-Path $oracleDir $oracleName
    $python = Resolve-Tool ($IsWindows ? 'python' : 'python3')
    $differentialReport = Join-Path $reportDir 'differential'
    Invoke-Tool 'differential' $python @(
        $pythonScript,
        '--probe', $probe,
        '--library', $imagePath,
        '--oracle', $oracle,
        '--work', (Join-Path $workDir 'differential'),
        '--report', $differentialReport,
        '--target', $target)
    $differential = Get-Content -Raw (Join-Path $differentialReport 'differential-report.json') |
        ConvertFrom-Json
    if ($differential.result -ne 'pass') {
        throw 'The parser differential did not pass.'
    }
    if (!$differential.responseCeilingExercised) {
        throw 'The response ceiling was never exercised.'
    }
}

# --- Managed API and loader matrix ---
if (!$SkipManaged) {
    $dotnet = Resolve-Tool 'dotnet'
    Invoke-Tool 'build-managed-library' $dotnet @(
        'build', (Join-Path $root 'src/DogwoodSharp/DogwoodSharp.csproj'), '-c', 'Release', '-v', 'minimal')
    Invoke-Tool 'build-managed-harness' $dotnet @(
        'build', $managedProject, '-c', 'Release', '-v', 'minimal')
    Assert-ProductionTreeUnchanged

    foreach ($framework in $Frameworks) {
        $assembly = Join-Path $managedOutput "$framework/QualificationHarness.dll"
        $report = Join-Path $reportDir "managed-$framework"
        $work = Join-Path $workDir "managed-$framework"
        Invoke-Tool "managed-$framework" $dotnet @(
            $assembly,
            '--dotnet', $dotnet,
            '--real-assets', $assets,
            '--fake-bridge', $fake,
            '--fake-minimal', $minimal,
            '--cargo-lock', $cargoLock,
            '--report', $report,
            '--work', $work,
            '--framework', $framework)
        $managed = Get-Content -Raw (Join-Path $report 'managed-report.json') | ConvertFrom-Json
        if ($managed.failed -ne 0 -or $managed.passed -ne $managed.scenarioCount) {
            throw "Managed $framework matrix did not pass."
        }
    }

    # --- Trimmed NativeAOT smoke, one independent executable per framework ---
    if (!$SkipAot) {
        foreach ($framework in $Frameworks) {
            $aotDir = Join-Path $out "aot/$RuntimeIdentifier/$framework"
            if (Test-Path $aotDir) {
                Remove-Item -Recurse -Force $aotDir
            }
            Invoke-Tool "aot-publish-$framework" $dotnet @(
                'publish', $aotProject, '-c', 'Release', '-r', $RuntimeIdentifier, '-f', $framework,
                '-o', $aotDir, '--nologo')
            Copy-Item (Join-Path $assets 'runtimes') $aotDir -Recurse -Force
            $exe = Join-Path $aotDir ($IsWindows ? 'D1QualificationAot.exe' : 'D1QualificationAot')
            if (!(Test-Path $exe)) {
                throw "AOT executable not found: $exe"
            }
            $env:D1Q_TARGET = $target
            try {
                Invoke-Tool "aot-smoke-$framework" $exe @()
            }
            finally {
                Remove-Item Env:D1Q_TARGET -ErrorAction SilentlyContinue
            }
        }
    }
}

Assert-ProductionTreeUnchanged

$differentialReportPath = if ($SkipDifferential) {
    $null
}
else {
    "reports/$RuntimeIdentifier/differential/differential-report.json"
}
$managedReportPaths = if ($SkipManaged) {
    @()
}
else {
    @($Frameworks | ForEach-Object { "reports/$RuntimeIdentifier/managed-$_/managed-report.json" })
}
$aotPaths = if ($SkipManaged -or $SkipAot) {
    @()
}
else {
    @($Frameworks | ForEach-Object { "aot/$RuntimeIdentifier/$_" })
}

$summary = [ordered]@{
    result = 'pass'
    runtimeIdentifier = $RuntimeIdentifier
    target = $target
    frameworks = @($Frameworks)
    image = $image
    imageSha256 = $imageHash
    cargoLockSha256 = $cargoHash
    nativeSelftest = 'logs/native-selftest.log'
    differential = $differentialReportPath
    managed = $managedReportPaths
    aot = $aotPaths
}
Write-JsonFile (Join-Path $out "qualification-$RuntimeIdentifier.json") $summary
Write-Host "QUALIFICATION PASS $RuntimeIdentifier ($target)"
