# DOGWOOD-SHARP-D3-E1-Q1 NativeAOT reproduction (win-x64, one TFM per run).
#
# Publishes the reviewer AOT consumer with PublishAot, then proves the
# fail-closed asset contract three times:
#   1. unstaged publish must fail closed (no Dogwood bridge asset),
#   2. staged  publish must succeed (bridge at runtimes\win-x64\native),
#   3. unstaged again must fail closed.
# Reviewer tooling only; never a production path.

param(
    [Parameter(Mandatory)]
    [ValidateSet('net8.0', 'net10.0')]
    [string] $TargetFramework
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$evidence = Join-Path $PSScriptRoot 'evidence'
$project = Join-Path $PSScriptRoot 'evidence\probe-aot\AotProbe.csproj'
$bridgeSource = Join-Path $repository 'artifacts\native\runtimes\win-x64\native'
$work = Join-Path (Join-Path $env:TEMP 'opencode') "q1-aot-$TargetFramework"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

Write-Output "== publishing NativeAOT $TargetFramework for win-x64 =="
dotnet publish $project -c Release -f $TargetFramework -r win-x64 -o $work --nologo 2>&1 |
    Tee-Object -FilePath (Join-Path $evidence "aot-publish-$TargetFramework.log")
if ($LASTEXITCODE -ne 0) { throw 'the AOT publish failed' }

$exe = Join-Path $work 'AotProbe.exe'
$unstagedResults = Join-Path $evidence "aot-results-$TargetFramework-unstaged.json"
$stagedResults = Join-Path $evidence "aot-results-$TargetFramework-staged.json"
$againResults = Join-Path $evidence "aot-results-$TargetFramework-unstaged-again.json"

Write-Output '== negative control: unstaged publish must fail closed =='
$unstaged = & $exe $unstagedResults 2>&1
$unstagedExit = $LASTEXITCODE
$unstaged | Tee-Object -FilePath (Join-Path $evidence "aot-console-$TargetFramework-unstaged.log") | Out-Null
if ($unstagedExit -eq 0) { throw 'the unstaged publish did not fail closed' }
Write-Output "unstaged exit code: $unstagedExit (expected nonzero)"

Write-Output '== staging the bridge and rerunning =='
$native = Join-Path $work 'runtimes\win-x64\native'
New-Item -ItemType Directory -Force -Path $native | Out-Null
Copy-Item (Join-Path $bridgeSource '*') $native -Force
$staged = & $exe $stagedResults 2>&1
$stagedExit = $LASTEXITCODE
$staged | Tee-Object -FilePath (Join-Path $evidence "aot-console-$TargetFramework-staged.log") | Out-Null
Write-Output "staged exit code: $stagedExit (expected 0)"

Write-Output '== unstaging again: must fail closed again =='
Remove-Item $native -Recurse -Force
$again = & $exe $againResults 2>&1
$againExit = $LASTEXITCODE
$again | Tee-Object -FilePath (Join-Path $evidence "aot-console-$TargetFramework-unstaged-again.log") | Out-Null
if ($againExit -eq 0) { throw 'the unstaged rerun did not fail closed' }
Write-Output "unstaged-again exit code: $againExit (expected nonzero)"

$summary = [ordered]@{
    targetFramework = $TargetFramework
    publishDirectory = $work
    cedarSharpNativeAtPublishRoot = (Test-Path (Join-Path $work 'cedarsharp_native.dll'))
    unstagedExitCode = $unstagedExit
    stagedExitCode = $stagedExit
    unstagedAgainExitCode = $againExit
    unstagedMessage = (($unstaged | Out-String).Trim())
    stagedExeSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash
}
$summary | ConvertTo-Json | Set-Content (Join-Path $evidence "aot-summary-$TargetFramework.json") -Encoding utf8

if ($stagedExit -ne 0) { throw 'the staged AOT run failed' }
Write-Output "AOT reproduction complete for $TargetFramework"
exit 0
