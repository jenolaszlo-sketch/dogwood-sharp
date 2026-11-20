# DOGWOOD-SHARP-D3-E1-Q1 independent reviewer probe runner (JIT, win-x64).
#
# Rebuilds the pinned Cedar 4.11 FFI reference deterministically (--locked),
# builds the reviewer probe for net8.0 and net10.0, stages the D2 bridge
# beside each probe binary (the D2 native asset discovery contract), and runs
# the probe harness for both TFMs, writing JSON results and console logs into
# evidence/. Reviewer tooling only; never a production path.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$evidence = Join-Path $PSScriptRoot 'evidence'
$probe = Join-Path $PSScriptRoot 'evidence\probe'
$bridgeSource = Join-Path $repository 'artifacts\native\runtimes\win-x64\native'
$refProject = Join-Path $repository 'native\dogwoodsharp-e1-ref'

Write-Output '== rebuilding the pinned Cedar 4.11 FFI reference (--locked) =='
cargo build --locked --release --manifest-path (Join-Path $refProject 'Cargo.toml')
if ($LASTEXITCODE -ne 0) { throw 'the reference build failed' }
$refExe = Join-Path $refProject 'target\release\dogwoodsharp-e1-ref.exe'
if (-not (Test-Path $refExe)) { throw 'the reference binary is missing' }

Write-Output '== building the reviewer probe (net8.0, net10.0) =='
dotnet build (Join-Path $probe 'Probe.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'the probe build failed' }

Write-Output '== staging the D2 bridge and running both TFMs =='
foreach ($tfm in @('net8.0', 'net10.0')) {
    $bin = Join-Path $probe "bin\Release\$tfm"
    $native = Join-Path $bin 'runtimes\win-x64\native'
    New-Item -ItemType Directory -Force -Path $native | Out-Null
    Copy-Item (Join-Path $bridgeSource '*') $native -Force
    $results = Join-Path $evidence "probe-results-$tfm.json"
    & dotnet (Join-Path $bin 'Probe.dll') $results $refExe 2>&1 |
        Tee-Object -FilePath (Join-Path $evidence "probe-console-$tfm.log")
    if ($LASTEXITCODE -ne 0) { throw "the probe run failed for $tfm (see probe-console-$tfm.log)" }
}

Write-Output '== recording asset identity and baseline =='
$identity = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    head = (& git -C $repository rev-parse HEAD)
    tree = (& git -C $repository rev-parse 'HEAD^{tree}')
    dirtyFiles = @(& git -C $repository status --porcelain).Count
    bridgeSha256 = (Get-FileHash (Join-Path $bridgeSource 'dogwoodsharp_bridge.dll') -Algorithm SHA256).Hash
    bridgeManifest = (Get-Content (Join-Path $bridgeSource 'dogwoodsharp-native.json') -Raw | ConvertFrom-Json)
    referenceExeSha256 = (Get-FileHash $refExe -Algorithm SHA256).Hash
    referenceTreeClean = (@(& git -C $repository status --porcelain -- 'native/dogwoodsharp-e1-ref')).Count -eq 0
    cedarSharpNativeSha256 = (Get-FileHash (Join-Path $probe 'bin\Release\net10.0\runtimes\win-x64\native\cedarsharp_native.dll') -Algorithm SHA256).Hash
}
$identity | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'asset-identity.json') -Encoding utf8
Write-Output 'run complete'
exit 0
