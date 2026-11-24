param(
    [string] $EvidencePath = (Join-Path $PSScriptRoot '../../../../d3-impl-01-r4/evidence'),
    [string] $WorkRoot = (Join-Path $env:TEMP ('dogwood-d3-r4-' + [guid]::NewGuid().ToString('N')))
)

# DOGWOOD-SHARP-D3-IMPL-01-R4 differential and lifecycle evidence runner.
#
# Builds the pinned upstream oracle and the hardened managed probe from their
# locked graphs, stages the verified native asset, runs the harness regression
# self-test, and then executes the full R4 corpus against both target
# frameworks. The lifecycle controls execute their scenarios here: each
# destructive scenario runs in an isolated child process against a private
# staged copy of the probe host directory, and the residency control runs in
# the probe process itself.
#
# The R4 corpus carries the R3 corpus forward unchanged and closes the two C-23
# typed-context gaps under the owner-approved ED-06 representability ceiling.
# Every case must now pass: the runner fails when any case fails, when any
# family coverage error is reported, or when the harness self-test fails.

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../../../..')).Path
$oracleProject = Join-Path $PSScriptRoot 'upstream-oracle'
$oracleExe = Join-Path $oracleProject 'target/release/dogwoodsharp-upstream-oracle.exe'
$project = Join-Path $PSScriptRoot 'probe/R4Probe.csproj'
$fixtures = Join-Path $PSScriptRoot 'fixtures.json'
$differences = Join-Path $PSScriptRoot 'differences.json'
$bridgeAssets = Join-Path $repository 'artifacts/native/runtimes/win-x64/native'
$expectedUpstreamRevision = '929f49e7305534ac61ef06cf8e47269c596dc851'

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

$selfTestPath = Join-Path $EvidencePath 'r4-harness-self-test.json'
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

    $resultPath = Join-Path $EvidencePath "r4-differential-$tfm-results.json"
    $lifecyclePath = Join-Path $EvidencePath "r4-lifecycle-$tfm.json"
    $logPath = Join-Path $EvidencePath "r4-differential-$tfm.log"
    $run = Invoke-Native 'dotnet' @(
        (Join-Path $bin 'PrepProbe.dll'),
        'differential', $fixtures, $oracleExe, $differences,
        $resultPath, $lifecyclePath, (Join-Path $WorkRoot $tfm)) $repository
    Record $run "differential-$tfm"
    $run.output | Set-Content -LiteralPath $logPath -Encoding utf8

    $document = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    $failedIds = @($document.cases | Where-Object { $_.verdict -ne 'pass' } | ForEach-Object { $_.id })
    if ($failedIds.Count -ne 0) {
        throw "the R4 differential failed: $($failedIds -join ', ')"
    }
    $coverageErrors = @($document.coverageErrors)
    if ($coverageErrors.Count -ne 0) {
        throw "the R4 differential reported family coverage errors: $($coverageErrors -join ', ')"
    }
    $summary[$tfm] = [ordered]@{
        total = $document.summary.total
        passed = $document.summary.passed
        failed = $document.summary.failed
        pending = $document.summary.pending
        familyCount = $document.summary.familyCount
        classifications = $document.summary.classifications
        coverageErrors = $coverageErrors
        result = "docs/reviews/d3-impl-01-r4/evidence/r4-differential-$tfm-results.json"
        lifecycle = "docs/reviews/d3-impl-01-r4/evidence/r4-lifecycle-$tfm.json"
        log = "docs/reviews/d3-impl-01-r4/evidence/r4-differential-$tfm.log"
    }
}

Copy-Item $fixtures (Join-Path $EvidencePath 'r4-differential-fixtures.json') -Force
Copy-Item $differences (Join-Path $EvidencePath 'r4-differential-differences.json') -Force

$runLog = [ordered]@{
    schemaVersion = 3
    commission = 'DOGWOOD-SHARP-D3-IMPL-01-R4'
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
$runLog | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $EvidencePath 'r4-differential-run.log') -Encoding utf8

# The work root is scratch space under TEMP, not evidence. A transient Windows
# file lock on a staged native image (a lifecycle child process that has just
# exited) must not fail an otherwise complete evidence run, so the cleanup is
# best effort: it is retried briefly and then abandoned with a warning.
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Remove-Item -LiteralPath $WorkRoot -Recurse -Force -ErrorAction Stop
        break
    }
    catch {
        if ($attempt -eq 5) {
            Write-Warning "the R4 work root could not be removed and is left under $WorkRoot : $($_.Exception.Message)"
        }
        else {
            Start-Sleep -Milliseconds 250
        }
    }
}
Write-Output "R4 differential evidence written to $EvidencePath"
