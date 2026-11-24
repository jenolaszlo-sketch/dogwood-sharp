param(
    [string] $EvidencePath = (Join-Path $PSScriptRoot '../../../../d3-impl-01-r3/evidence'),
    [string] $WorkRoot = (Join-Path $env:TEMP ('dogwood-d3-r3-' + [guid]::NewGuid().ToString('N')))
)

# DOGWOOD-SHARP-D3-IMPL-01-R3 differential and lifecycle evidence runner.
#
# Builds the pinned upstream oracle and the hardened managed probe from their
# locked graphs, stages the verified native asset, runs the harness regression
# self-test, and then executes the full R3 corpus against both target
# frameworks. The lifecycle controls execute their scenarios here: each
# destructive scenario runs in an isolated child process against a private
# staged copy of the probe host directory, and the residency control runs in
# the probe process itself.
#
# The R3 corpus contains two cases that are known unresolved blocking gaps
# (the ipaddr and datetime typed context value ceilings). The differential run
# therefore reports a non-zero exit code and this script records that exit
# code as the expected outcome instead of hiding it.

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../../../..')).Path
$oracleProject = Join-Path $PSScriptRoot 'upstream-oracle'
$oracleExe = Join-Path $oracleProject 'target/release/dogwoodsharp-upstream-oracle.exe'
$project = Join-Path $PSScriptRoot 'probe/R3Probe.csproj'
$fixtures = Join-Path $PSScriptRoot 'fixtures.json'
$differences = Join-Path $PSScriptRoot 'differences.json'
$bridgeAssets = Join-Path $repository 'artifacts/native/runtimes/win-x64/native'
$expectedUpstreamRevision = '929f49e7305534ac61ef06cf8e47269c596dc851'
$blockingCases = @('R3-C23-08-ipaddr-typed-context', 'R3-C23-09-datetime-typed-context')

$env:Path = "$env:USERPROFILE\.cargo\bin;$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

New-Item -ItemType Directory -Force -Path $EvidencePath | Out-Null
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

function Invoke-Native([string] $Command, [string[]] $Arguments, [string] $WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        Write-Host "> $Command $($Arguments -join ' ')"
        $output = @(& $Command @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        return [ordered]@{ exitCode = $exitCode; output = ($output -join [Environment]::NewLine) }
    }
    finally {
        Pop-Location
    }
}

$log = [System.Collections.Generic.List[string]]::new()
$commands = [System.Collections.Generic.List[object]]::new()

function Record([object] $Result, [string] $Label) {
    $script:log.Add("[$Label] exit=$($Result.exitCode)")
    $script:commands.Add([ordered]@{ label = $Label; exitCode = $Result.exitCode })
}

Write-Output '== verifying the locked upstream dependency =='
$tree = Invoke-Native 'cargo' @(
    'tree', '--locked', '--offline', '--manifest-path', (Join-Path $oracleProject 'Cargo.toml'),
    '-p', 'dogwood-language', '--depth', '0') $repository
Record $tree 'cargo-tree'
if (($tree.output -replace '\s', '') -notmatch [regex]::Escape($expectedUpstreamRevision.Substring(0, 7))) {
    throw "cargo tree did not resolve the expected upstream revision: $($tree.output)"
}

Write-Output '== building the upstream Dogwood authorizer oracle (locked) =='
$build = Invoke-Native 'cargo' @(
    'build', '--locked', '--release', '--offline', '--manifest-path', (Join-Path $oracleProject 'Cargo.toml')) $repository
Record $build 'cargo-build-oracle'
if ($build.exitCode -ne 0) { throw 'the upstream oracle build failed' }

Write-Output '== restoring and building the managed differential probe =='
$restore = Invoke-Native 'dotnet' @('restore', $project, '--locked-mode', '--nologo') $repository
Record $restore 'dotnet-restore'
if ($restore.exitCode -ne 0) { throw 'the probe restore failed' }
$probeBuild = Invoke-Native 'dotnet' @('build', $project, '-c', 'Release', '--no-restore', '--nologo') $repository
Record $probeBuild 'dotnet-build'
if ($probeBuild.exitCode -ne 0) { throw 'the probe build failed' }

$selfTestPath = Join-Path $EvidencePath 'r3-harness-self-test.json'
$selfTest = Invoke-Native 'dotnet' @(
    (Join-Path $PSScriptRoot 'probe/bin/Release/net10.0/PrepProbe.dll'),
    'self-test', $oracleExe, $selfTestPath) $repository
Record $selfTest 'harness-self-test'
if ($selfTest.exitCode -ne 0) { throw 'the harness regression self-test failed' }

$summary = [ordered]@{}
foreach ($tfm in @('net8.0', 'net10.0')) {
    $bin = Join-Path $PSScriptRoot "probe/bin/Release/$tfm"
    $native = Join-Path $bin 'runtimes/win-x64/native'
    New-Item -ItemType Directory -Force -Path $native | Out-Null
    Copy-Item (Join-Path $bridgeAssets '*') $native -Force
    Copy-Item (Join-Path $bridgeAssets '*') $bin -Force

    $resultPath = Join-Path $EvidencePath "r3-differential-$tfm-results.json"
    $lifecyclePath = Join-Path $EvidencePath "r3-lifecycle-$tfm.json"
    $logPath = Join-Path $EvidencePath "r3-differential-$tfm.log"
    $run = Invoke-Native 'dotnet' @(
        (Join-Path $bin 'PrepProbe.dll'),
        'differential', $fixtures, $oracleExe, $differences,
        $resultPath, $lifecyclePath, (Join-Path $WorkRoot $tfm)) $repository
    Record $run "differential-$tfm"
    $run.output | Set-Content -LiteralPath $logPath -Encoding utf8

    $document = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    $failedIds = @($document.cases | Where-Object { $_.verdict -ne 'pass' } | ForEach-Object { $_.id })
    $unexpected = @($failedIds | Where-Object { $blockingCases -notcontains $_ })
    if ($unexpected.Count -ne 0) {
        throw "the R3 differential failed outside the two recorded blocking cases: $($unexpected -join ', ')"
    }
    $summary[$tfm] = [ordered]@{
        total = $document.summary.total
        passed = $document.summary.passed
        failed = $document.summary.failed
        blockingCases = $failedIds
        coverageErrors = @($document.coverageErrors)
        result = "docs/reviews/d3-impl-01-r3/evidence/r3-differential-$tfm-results.json"
        lifecycle = "docs/reviews/d3-impl-01-r3/evidence/r3-lifecycle-$tfm.json"
        log = "docs/reviews/d3-impl-01-r3/evidence/r3-differential-$tfm.log"
    }
}

Copy-Item $fixtures (Join-Path $EvidencePath 'r3-differential-fixtures.json') -Force
Copy-Item $differences (Join-Path $EvidencePath 'r3-differential-differences.json') -Force

$runLog = [ordered]@{
    schemaVersion = 2
    commission = 'DOGWOOD-SHARP-D3-IMPL-01-R3'
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    upstream = [ordered]@{
        repository = 'https://github.com/dogwood-policy/dogwood'
        revision = $expectedUpstreamRevision
        version = 'dogwood-language 1.1.2'
        cedarVersion = '4.11.0'
    }
    commands = $commands
    summary = $summary
}
$runLog | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $EvidencePath 'r3-differential-run.log') -Encoding utf8

Remove-Item -LiteralPath $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "R3 differential evidence written to $EvidencePath"
