param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'guard-regression.json'),
    [string] $RepositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

$archiveRevision = 'b79fdffa07ac45cb49c5ee2ef100299f2d2adcf5'
$protectedArchive = 'docs/reviews/d2-qualification'
$tempParent = Join-Path $env:LOCALAPPDATA 'Temp/opencode'
if (-not (Test-Path $tempParent)) {
    New-Item -ItemType Directory -Force -Path $tempParent | Out-Null
}
$clone = Join-Path $tempParent ("dogwood-sharp-d3-prep-guard-" + [guid]::NewGuid().ToString('N'))
$cases = @()
$cleanup = [ordered]@{ attempted = $false; removed = $false; path = $clone }
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source

function Invoke-Git([string] $ClonePath, [string[]] $Arguments) {
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
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $output = @(& $pwsh -NoLogo -NoProfile -NonInteractive -File $script -RuntimeIdentifier win-x64 -GuardOnly 2>&1)
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

    & git clone --no-hardlinks --quiet $RepositoryPath $clone
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to create disposable clone at $clone."
    }
    Invoke-Git $clone @('config', 'user.name', 'D3 PREP guard regression') | Out-Null
    Invoke-Git $clone @('config', 'user.email', 'd3-prep-guard@example.invalid') | Out-Null

    # The script under test is copied into the disposable clone and committed,
    # so every case starts from a clean tree and the production guard path is
    # tested exactly as shipped rather than by a mocked implementation.
    Copy-Item -LiteralPath (Join-Path $RepositoryPath 'eng/Verify-D2R2.ps1') -Destination (Join-Path $clone 'eng/Verify-D2R2.ps1') -Force
    Commit-All $clone 'test: stage remediated archived-R2 guard'
    $guardCommit = (@(Invoke-Git $clone @('rev-parse', 'HEAD'))[0]).Trim()

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
        schemaVersion = 1
        commission = 'DOGWOOD-SHARP-D3-PREP-01'
        repository = $RepositoryPath
        archiveRevision = $archiveRevision
        guardCommit = $guardCommit
        runtime = [ordered]@{
            os = [Environment]::OSVersion.VersionString
            powershell = $PSVersionTable.PSVersion.ToString()
            git = (@(Invoke-Git $clone @('version'))[0]).Trim()
        }
        summary = [ordered]@{
            total = $cases.Count
            passed = $cases.Count - $failed
            failed = $failed
        }
        cases = $cases
        cleanup = $cleanup
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
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
    if (Test-Path -LiteralPath $OutputPath) {
        # The summary is written before cleanup so its contents can include
        # the cleanup result; rewrite it after the disposable tree is gone.
        $result.cleanup = $cleanup
        $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    }
}
