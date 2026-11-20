param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'guard-regression.json'),
    [string] $RepositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

$archiveRevision = 'b79fdffa07ac45cb49c5ee2ef100299f2d2adcf5'
$preflightRevision = 'e73c1caedb2df27f975f25bcdfe3f122d951ca9a'
$protectedArchive = 'docs/reviews/d2-qualification'
$tempParent = Join-Path $env:LOCALAPPDATA 'Temp/opencode'
if (-not (Test-Path $tempParent)) {
    New-Item -ItemType Directory -Force -Path $tempParent | Out-Null
}
$clone = Join-Path $tempParent ("dogwood-sharp-d3-prep-guard-" + [guid]::NewGuid().ToString('N'))
$cases = @()
$result = $null
$cleanup = [ordered]@{ attempted = $false; removed = $false; path = $clone }
$commands = [System.Collections.Generic.List[object]]::new()
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source
$protectedPaths = @(
    'docs/reviews/d2-qualification',
    'docs/d1-acceptance.md',
    '.github/workflows/d1-qualification.yml',
    '.github/workflows/d2-qualification.yml'
)
$legacyGuardPaths = @(
    'docs/reviews',
    'docs/d1-acceptance.md',
    '.github/workflows/d1-qualification.yml',
    '.github/workflows/d2-qualification.yml'
)

function Invoke-Git([string] $ClonePath, [string[]] $Arguments) {
    $commands.Add([ordered]@{
        command = 'git'
        arguments = @('-C', $ClonePath) + $Arguments
        workingDirectory = $ClonePath
    })
    $output = @(& git -C $ClonePath @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in ${ClonePath}: $($output -join [Environment]::NewLine)"
    }
    return $output
}

function Commit-All([string] $ClonePath, [string] $Message) {
    Invoke-Git $ClonePath @('add', '-A') | Out-Null
    Invoke-Git $ClonePath @('commit', '-m', $Message) | Out-Null
}

function Reset-ToGuard([string] $ClonePath, [string] $Commit) {
    Invoke-Git $ClonePath @('reset', '--hard', $Commit) | Out-Null
}

function Invoke-Guard([string] $ClonePath) {
    $script = Join-Path $ClonePath 'eng/Verify-D2R2.ps1'
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $script, '-RuntimeIdentifier', 'win-x64', '-GuardOnly')
    Write-Information "> $pwsh $($arguments -join ' ')"
    $commands.Add([ordered]@{
        command = $pwsh
        arguments = $arguments
        workingDirectory = $ClonePath
    })
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $output = @(& $pwsh @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $true
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine)
    }
}

try {
    if (-not (Test-Path (Join-Path $RepositoryPath '.git'))) {
        throw "RepositoryPath is not a Git checkout: $RepositoryPath"
    }

    $sourceHead = (@(Invoke-Git $RepositoryPath @('rev-parse', 'HEAD'))[0]).Trim()
    $sourceTree = (@(Invoke-Git $RepositoryPath @('rev-parse', 'HEAD^{tree}'))[0]).Trim()
    $sourceBranch = (@(Invoke-Git $RepositoryPath @('symbolic-ref', '--short', 'HEAD'))[0]).Trim()
    $sourceCommitTime = (@(Invoke-Git $RepositoryPath @('show', '-s', '--format=%cI', 'HEAD'))[0]).Trim()
    $sourceStatus = @(Invoke-Git $RepositoryPath @('status', '--porcelain=v1', '--untracked-files=all'))
    if ($sourceStatus.Count -ne 0) {
        throw "Guard evidence requires a clean source checkout; $($sourceStatus.Count) path(s) are dirty."
    }
    $originMain = (@(Invoke-Git $RepositoryPath @('rev-parse', 'origin/main'))[0]).Trim()
    $divergence = (@(Invoke-Git $RepositoryPath @('rev-list', '--left-right', '--count', 'HEAD...origin/main'))[0]).Trim() -split '\s+'

    foreach ($ancestor in @($archiveRevision, $preflightRevision)) {
        Invoke-Git $RepositoryPath @('merge-base', '--is-ancestor', $ancestor, 'HEAD') | Out-Null
    }

    $sourceGuardPath = Join-Path $RepositoryPath 'eng/Verify-D2R2.ps1'
    $sourceGuardSha256 = (Get-FileHash -LiteralPath $sourceGuardPath -Algorithm SHA256).Hash
    $sourceGuardBlob = (@(Invoke-Git $RepositoryPath @('rev-parse', 'HEAD:eng/Verify-D2R2.ps1'))[0]).Trim()
    $protectedDiffArguments = @('diff', '--name-status', $archiveRevision, 'HEAD', '--') + $protectedPaths
    $sourceProtectedDiff = @(Invoke-Git $RepositoryPath $protectedDiffArguments)
    if ($sourceProtectedDiff.Count -ne 0) {
        throw "The remediated protected scope already differs from the archive: $($sourceProtectedDiff -join '; ')"
    }

    $legacyDiffArguments = @('diff', '--name-only', $archiveRevision, $preflightRevision, '--') + $legacyGuardPaths
    $legacyObservedChanges = @(Invoke-Git $RepositoryPath $legacyDiffArguments)

    & git clone --no-hardlinks --quiet $RepositoryPath $clone
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to create disposable clone at $clone."
    }
    $commands.Add([ordered]@{
        command = 'git'
        arguments = @('clone', '--no-hardlinks', '--quiet', $RepositoryPath, $clone)
        workingDirectory = $tempParent
    })
    Invoke-Git $clone @('config', 'user.name', 'D3 PREP guard regression') | Out-Null
    Invoke-Git $clone @('config', 'user.email', 'd3-prep-guard@example.invalid') | Out-Null

    $cloneGuardPath = Join-Path $clone 'eng/Verify-D2R2.ps1'
    $cloneGuardSha256 = (Get-FileHash -LiteralPath $cloneGuardPath -Algorithm SHA256).Hash
    if ($sourceGuardSha256 -cne $cloneGuardSha256) {
        throw 'The disposable clone does not contain the exact source guard.'
    }
    $guardCommit = (@(Invoke-Git $clone @('rev-parse', 'HEAD'))[0]).Trim()
    if ($guardCommit -cne $sourceHead) {
        throw "The disposable clone is not at the source revision: $guardCommit != $sourceHead"
    }

    # 1. Unchanged archive and protected paths pass.
    $unchanged = Invoke-Guard $clone
    $cases += [ordered]@{
        id = 'unchanged'
        expected = 'pass'
        exitCode = $unchanged.ExitCode
        passed = ($unchanged.ExitCode -eq 0 -and $unchanged.Output -match 'guard passed')
        output = $unchanged.Output
    }

    # 2. A new, unrelated qualification report is allowed.
    $unrelated = Join-Path $clone 'docs/reviews/d3-prep-01/guard-regression-unrelated.md'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $unrelated) | Out-Null
    Set-Content -LiteralPath $unrelated -Value '# unrelated qualification report' -Encoding utf8
    Commit-All $clone 'test: add unrelated qualification report'
    $newReport = Invoke-Guard $clone
    $cases += [ordered]@{
        id = 'new-unrelated-report'
        expected = 'pass'
        exitCode = $newReport.ExitCode
        passed = ($newReport.ExitCode -eq 0 -and $newReport.Output -match 'guard passed')
        output = $newReport.Output
    }

    # 3. A modification in the protected archive fails.
    Reset-ToGuard $clone $guardCommit
    Add-Content -LiteralPath (Join-Path $clone "$protectedArchive/README.md") -Value "`nregression mutation`n"
    Commit-All $clone 'test: modify protected archive'
    $modified = Invoke-Guard $clone
    $cases += [ordered]@{
        id = 'modified-protected-archive'
        expected = 'fail'
        exitCode = $modified.ExitCode
        passed = ($modified.ExitCode -ne 0 -and $modified.Output -match 'modification or replacement' -and $modified.Output -match 'd2-qualification/README.md')
        output = $modified.Output
    }

    # 4. A deletion in the protected archive fails.
    Reset-ToGuard $clone $guardCommit
    Remove-Item -LiteralPath (Join-Path $clone "$protectedArchive/README.md") -Force
    Commit-All $clone 'test: delete protected archive file'
    $deleted = Invoke-Guard $clone
    $cases += [ordered]@{
        id = 'deleted-protected-archive'
        expected = 'fail'
        exitCode = $deleted.ExitCode
        passed = ($deleted.ExitCode -ne 0 -and $deleted.Output -match 'deletion' -and $deleted.Output -match 'd2-qualification/README.md')
        output = $deleted.Output
    }

    # 5. An unauthorized addition inside the protected archive fails.
    Reset-ToGuard $clone $guardCommit
    $addition = Join-Path $clone "$protectedArchive/unauthorized-addition.txt"
    Set-Content -LiteralPath $addition -Value 'unauthorized' -Encoding utf8
    Commit-All $clone 'test: add unauthorized archive file'
    $added = Invoke-Guard $clone
    $cases += [ordered]@{
        id = 'unauthorized-addition-protected-archive'
        expected = 'fail'
        exitCode = $added.ExitCode
        passed = ($added.ExitCode -ne 0 -and $added.Output -match 'unauthorized addition' -and $added.Output -match 'unauthorized-addition.txt')
        output = $added.Output
    }

    $failed = @($cases | Where-Object { -not $_.passed }).Count
    $result = [ordered]@{
        schemaVersion = 3
        commission = 'DOGWOOD-SHARP-D3-PREP-01'
        repository = $RepositoryPath
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
            guardBlob = $sourceGuardBlob
            guardSha256 = $sourceGuardSha256
            runnerSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        }
        baseline = [ordered]@{
            archiveRevision = $archiveRevision
            archiveTree = (@(Invoke-Git $RepositoryPath @('rev-parse', "$archiveRevision^{tree}"))[0]).Trim()
            preflightRevision = $preflightRevision
            preflightTree = (@(Invoke-Git $RepositoryPath @('rev-parse', "$preflightRevision^{tree}"))[0]).Trim()
            legacyGuardScope = $legacyGuardPaths
            legacyObservedChangeCount = $legacyObservedChanges.Count
            legacyObservedChanges = $legacyObservedChanges
        }
        scope = [ordered]@{
            protectedPaths = $protectedPaths
            guardCommit = $guardCommit
            guardMatchedSource = ($guardCommit -ceq $sourceHead)
            protectedPathDiff = $sourceProtectedDiff
        }
        runtime = [ordered]@{
            os = [Environment]::OSVersion.VersionString
            architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            powershell = $PSVersionTable.PSVersion.ToString()
            git = (@(Invoke-Git $clone @('version'))[0]).Trim()
        }
        commands = $commands
        summary = [ordered]@{
            total = $cases.Count
            passed = $cases.Count - $failed
            failed = $failed
        }
        cases = $cases
        cleanup = $cleanup
    }
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    if ($failed) {
        throw "Archived-R2 guard regression failed: $failed case(s) failed."
    }
    Write-Output "Archived-R2 guard regression passed: $($cases.Count)/$($cases.Count) cases."
}
finally {
    $cleanup.attempted = $true
    if (Test-Path -LiteralPath $clone) {
        Remove-Item -LiteralPath $clone -Recurse -Force
    }
    $cleanup.removed = -not (Test-Path -LiteralPath $clone)
    if ($null -ne $result -and (Test-Path -LiteralPath $OutputPath)) {
        $result.cleanup = $cleanup
        $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    }
}
