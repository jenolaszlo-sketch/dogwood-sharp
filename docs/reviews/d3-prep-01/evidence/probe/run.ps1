param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'probe-run.json')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
$oracleProject = Join-Path $repository 'docs/reviews/d3-prep-01/evidence/upstream-oracle'
$oracleExe = Join-Path $oracleProject 'target/release/dogwoodsharp-upstream-oracle.exe'
$project = Join-Path $PSScriptRoot 'PrepProbe.csproj'
$fixtures = Join-Path $PSScriptRoot 'fixtures.json'
$differences = Join-Path $PSScriptRoot 'differences.json'
$bridgeAssets = Join-Path $repository 'artifacts/native/runtimes/win-x64/native'
$results = @{}
$logs = @{}
$identity = $null
$commands = [System.Collections.Generic.List[object]]::new()
$expectedUpstreamRevision = '929f49e7305534ac61ef06cf8e47269c596dc851'
$generatedPaths = @(
    (Join-Path $PSScriptRoot 'bin'),
    (Join-Path $PSScriptRoot 'obj'),
    (Join-Path $oracleProject 'target')
)
$expectedOutputPaths = @(
    'docs/reviews/d3-prep-01/evidence/probe/probe-run.json',
    'docs/reviews/d3-prep-01/evidence/probe/prep-results-net8.0.json',
    'docs/reviews/d3-prep-01/evidence/probe/prep-results-net10.0.json',
    'docs/reviews/d3-prep-01/evidence/probe/prep-console-net8.0.log',
    'docs/reviews/d3-prep-01/evidence/probe/prep-console-net10.0.log'
)
$cleanup = [ordered]@{
    attempted = $false
    removed = $false
    paths = @(
        'docs/reviews/d3-prep-01/evidence/probe/bin',
        'docs/reviews/d3-prep-01/evidence/probe/obj',
        'docs/reviews/d3-prep-01/evidence/upstream-oracle/target'
    )
    residualPaths = @()
}
$postRunStatus = @()
$unexpectedSourceChanges = @()

function Invoke-Checked([string] $Command, [string[]] $Arguments, [string] $WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        Write-Output "> $Command $($Arguments -join ' ')"
        $commands.Add([ordered]@{
            command = $Command
            arguments = $Arguments
            workingDirectory = $WorkingDirectory
        })
        $output = @(& $Command @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "$Command failed with exit $exitCode`n$($output -join [Environment]::NewLine)"
        }
        return $output
    }
    finally {
        Pop-Location
    }
}

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-RelativePath([string] $Path) {
    return [IO.Path]::GetRelativePath($repository, $Path).Replace('\', '/')
}

try {
    $sourceHead = (@(& git -C $repository rev-parse HEAD)[0]).Trim()
    $sourceTree = (@(& git -C $repository rev-parse 'HEAD^{tree}')[0]).Trim()
    $sourceBranch = (@(& git -C $repository symbolic-ref --short HEAD)[0]).Trim()
    $sourceCommitTime = (@(& git -C $repository show -s --format=%cI HEAD)[0]).Trim()
    $sourceStatus = @(& git -C $repository status --porcelain=v1 --untracked-files=all)
    if ($sourceStatus.Count -ne 0) {
        throw "Probe evidence requires a clean source checkout; $($sourceStatus.Count) path(s) are dirty."
    }
    $originMain = (@(& git -C $repository rev-parse origin/main)[0]).Trim()
    $divergence = (@(& git -C $repository rev-list --left-right --count HEAD...origin/main)[0]).Trim() -split '\s+'

    $env:Path = "$env:USERPROFILE\.cargo\bin;$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

    $bridgeDll = Join-Path $bridgeAssets 'dogwoodsharp_bridge.dll'
    if (-not (Test-Path -LiteralPath $bridgeDll)) {
        throw "The verified bridge asset is missing: $bridgeDll. Run eng/Verify.ps1 first."
    }

    $cargoLockPath = Join-Path $oracleProject 'Cargo.lock'
    $cargoLockText = Get-Content -Raw -LiteralPath $cargoLockPath
    $expectedLockSource = 'source = "git+https://github.com/dogwood-policy/dogwood?rev={0}#{0}"' -f $expectedUpstreamRevision
    if (-not $cargoLockText.Contains($expectedLockSource)) {
        throw "Cargo.lock does not bind dogwood-language to $expectedUpstreamRevision."
    }
    if (-not $cargoLockText.Contains('name = "dogwood-language"')) {
        throw 'Cargo.lock does not contain dogwood-language.'
    }

    Write-Output '== verifying the locked upstream dependency =='
    $cargoTree = Invoke-Checked 'cargo' @(
        'tree',
        '--locked',
        '--offline',
        '--manifest-path', (Join-Path $oracleProject 'Cargo.toml'),
        '-p', 'dogwood-language',
        '--depth', '0'
    ) $repository
    $cargoTreeText = ($cargoTree -join [Environment]::NewLine)
    if ($cargoTreeText -notmatch [regex]::Escape($expectedUpstreamRevision.Substring(0, 7))) {
        throw "Cargo tree did not resolve the expected upstream revision: $cargoTreeText"
    }

    Write-Output '== building the upstream Dogwood authorizer oracle (locked) =='
    Invoke-Checked 'cargo' @('build', '--locked', '--release', '--offline', '--manifest-path', (Join-Path $oracleProject 'Cargo.toml')) $repository | Out-Null

    $cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
    $checkoutPattern = Join-Path $cargoHome 'git/checkouts/dogwood-*/*/dogwood-language/Cargo.toml'
    $checkoutCandidates = @(
        Get-ChildItem -Path $checkoutPattern -File -ErrorAction SilentlyContinue | ForEach-Object {
            Split-Path (Split-Path $_.FullName -Parent) -Parent
        } | Sort-Object -Unique
    )
    $matchingCheckouts = @(
        $checkoutCandidates | Where-Object {
            try {
                (@(& git -C $_ rev-parse HEAD)[0]).Trim() -ceq $expectedUpstreamRevision
            }
            catch {
                $false
            }
        }
    )
    if ($matchingCheckouts.Count -eq 0) {
        throw "Cargo did not retain a checkout at $expectedUpstreamRevision."
    }
    $upstreamCheckout = $matchingCheckouts[0]
    $upstreamCheckoutHead = (@(& git -C $upstreamCheckout rev-parse HEAD)[0]).Trim()
    $upstreamCheckoutTree = (@(& git -C $upstreamCheckout rev-parse 'HEAD^{tree}')[0]).Trim()

    Write-Output '== restoring and building the managed differential probe =='
    Invoke-Checked 'dotnet' @('restore', $project, '--locked-mode', '--nologo') $repository | Out-Null
    Invoke-Checked 'dotnet' @('build', $project, '-c', 'Release', '--no-restore', '--nologo') $repository | Out-Null

    foreach ($tfm in @('net8.0', 'net10.0')) {
        $bin = Join-Path $PSScriptRoot "bin/Release/$tfm"
        $native = Join-Path $bin 'runtimes/win-x64/native'
        New-Item -ItemType Directory -Force -Path $native | Out-Null
        Copy-Item (Join-Path $bridgeAssets '*') $native -Force
        $resultPath = Join-Path $PSScriptRoot "prep-results-$tfm.json"
        $logPath = Join-Path $PSScriptRoot "prep-console-$tfm.log"
        $probeExe = Join-Path $bin 'PrepProbe.dll'
        Write-Output "> dotnet $probeExe $resultPath $oracleExe $fixtures $differences"
        $commands.Add([ordered]@{
            command = 'dotnet'
            arguments = @($probeExe, $resultPath, $oracleExe, $fixtures, $differences)
            workingDirectory = $repository
        })
        $PSNativeCommandUseErrorActionPreference = $false
        try {
            $output = @(& dotnet $probeExe $resultPath $oracleExe $fixtures $differences 2>&1)
            $exitCode = $LASTEXITCODE
        }
        finally {
            $PSNativeCommandUseErrorActionPreference = $true
        }
        $output | Tee-Object -FilePath $logPath
        if ($exitCode -ne 0) {
            throw "The managed/upstream differential probe failed for $tfm (exit $exitCode)."
        }
        $results[$tfm] = $resultPath
        $logs[$tfm] = $logPath
    }

    $summary = [ordered]@{}
    foreach ($tfm in @('net8.0', 'net10.0')) {
        $document = Get-Content -Raw $results[$tfm] | ConvertFrom-Json
        $summary[$tfm] = [ordered]@{
            total = $document.summary.total
            passed = $document.summary.passed
            failed = $document.summary.failed
            result = "docs/reviews/d3-prep-01/evidence/probe/prep-results-$tfm.json"
            log = "docs/reviews/d3-prep-01/evidence/probe/prep-console-$tfm.log"
        }
    }

    $harnessFiles = [ordered]@{
        'probe/Program.cs' = (Join-Path $PSScriptRoot 'Program.cs')
        'probe/fixtures.json' = $fixtures
        'probe/differences.json' = $differences
        'probe/PrepProbe.csproj' = $project
        'probe/packages.lock.json' = (Join-Path $PSScriptRoot 'packages.lock.json')
        'probe/run.ps1' = (Join-Path $PSScriptRoot 'run.ps1')
        'upstream-oracle/Cargo.toml' = (Join-Path $oracleProject 'Cargo.toml')
        'upstream-oracle/Cargo.lock' = $cargoLockPath
        'upstream-oracle/src/main.rs' = (Join-Path $oracleProject 'src/main.rs')
    }
    $fileHashes = [ordered]@{}
    foreach ($entry in $harnessFiles.GetEnumerator()) {
        $fileHashes[$entry.Key] = Get-Sha256 $entry.Value
    }

    $cedarSharpNative = Join-Path $PSScriptRoot 'bin/Release/net10.0/runtimes/win-x64/native/cedarsharp_native.dll'
    if (-not (Test-Path -LiteralPath $cedarSharpNative)) {
        throw "The CedarSharp native asset is missing after build: $cedarSharpNative"
    }

    $postRunStatus = @(& git -C $repository status --porcelain=v1 --untracked-files=all)
    $unexpectedSourceChanges = @(
        $postRunStatus | Where-Object {
            if ($_.Length -lt 4) {
                return $true
            }
            $path = $_.Substring(3).Trim().Replace('\', '/')
            return $expectedOutputPaths -notcontains $path
        }
    )

    $identity = [ordered]@{
        schemaVersion = 3
        commission = 'DOGWOOD-SHARP-D3-PREP-01'
        source = [ordered]@{
            head = $sourceHead
            tree = $sourceTree
            branch = $sourceBranch
            commitTime = $sourceCommitTime
            worktreeCleanAtStart = $true
            dirtyFileCount = 0
            originMain = $originMain
            aheadBehind = [ordered]@{
                ahead = [int] $divergence[0]
                behind = [int] $divergence[1]
            }
            postRunStatus = $postRunStatus
            unexpectedSourceChanges = $unexpectedSourceChanges
        }
        upstream = [ordered]@{
            repository = 'https://github.com/dogwood-policy/dogwood'
            revision = $expectedUpstreamRevision
            checkoutHead = $upstreamCheckoutHead
            checkoutTree = $upstreamCheckoutTree
            checkoutPath = $upstreamCheckout
            matchingCheckoutCount = $matchingCheckouts.Count
            version = 'dogwood-language 1.1.2'
            cedarVersion = '4.11.0'
            cargoTree = $cargoTreeText
            cargoLock = Get-Sha256 $cargoLockPath
            oracleSha256 = Get-Sha256 $oracleExe
        }
        managed = [ordered]@{
            dotnet = ((& dotnet --version) | Select-Object -First 1)
            packageLock = Get-Sha256 (Join-Path $PSScriptRoot 'packages.lock.json')
            bridgeSha256 = Get-Sha256 $bridgeDll
            cedarSharpNativeSha256 = Get-Sha256 $cedarSharpNative
        }
        harnessFiles = $fileHashes
        runtime = [ordered]@{
            os = [Environment]::OSVersion.VersionString
            architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            powershell = $PSVersionTable.PSVersion.ToString()
            git = (@(& git --version)[0]).Trim()
            cargo = ((& cargo --version) | Select-Object -First 1)
            rustc = ((& rustc --version) | Select-Object -First 1)
        }
        commands = $commands
        summary = $summary
        cleanup = $cleanup
    }
    $identity | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    if ($unexpectedSourceChanges.Count -ne 0) {
        throw "Probe run changed unexpected source paths: $($unexpectedSourceChanges -join '; ')"
    }
    Write-Output "Probe evidence written to $OutputPath"
}
finally {
    $cleanup.attempted = $true
    foreach ($path in $generatedPaths) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
    $cleanup.residualPaths = @($generatedPaths | Where-Object { Test-Path -LiteralPath $_ })
    $cleanup.removed = $cleanup.residualPaths.Count -eq 0
    if ($null -ne $identity -and (Test-Path -LiteralPath $OutputPath)) {
        $identity.cleanup = $cleanup
        $identity | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    }
}
