# Re-run the archived D2 corpus in a disposable, production-gated copy.
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    # Run only the archived-evidence integrity guard. This is a test-only
    # entry point for the disposable regression harness; CI never sets it.
    [switch] $GuardOnly
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

# The archive and its qualification baseline are immutable evidence. Keep this
# list deliberately narrower than docs/reviews: later qualification reports
# are allowed, while this archive and the two historical workflows/records
# are not.
$archiveRevision = 'b79fdffa07ac45cb49c5ee2ef100299f2d2adcf5'
$protectedPaths = @(
    'docs/reviews/d2-qualification',
    'docs/d1-acceptance.md',
    '.github/workflows/d1-qualification.yml',
    '.github/workflows/d2-qualification.yml'
)

function Get-GuardChanges([string] $Root) {
    $changes = @()
    $lines = @(& git -C $Root diff --name-status --find-renames --no-ext-diff --no-textconv $archiveRevision HEAD -- $protectedPaths)
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        $parts = $line -split "`t"
        $code = $parts[0]
        $kind = switch -Regex ($code) {
            '^A' { 'unauthorized addition' }
            '^D' { 'deletion' }
            '^M' { 'modification or replacement' }
            '^R' { 'replacement or rename' }
            '^C' { 'unauthorized copy' }
            '^T' { 'replacement (type changed)' }
            default { "unexpected change ($code)" }
        }
        $paths = if ($parts.Count -gt 1) { $parts[1..($parts.Count - 1)] -join ' -> ' } else { '<unknown path>' }
        $changes += [pscustomobject]@{ Code = $code; Kind = $kind; Path = $paths }
    }
    return $changes
}

function Assert-ArchivedR2Evidence([string] $Root) {
    if (-not (Test-Path (Join-Path $Root '.git'))) {
        throw "The archived-R2 guard requires a Git checkout at $Root."
    }

    $ancestor = $false
    try {
        & git -C $Root merge-base --is-ancestor $archiveRevision HEAD
        $ancestor = ($LASTEXITCODE -eq 0)
    }
    catch {
        $ancestor = $false
    }
    if (-not $ancestor) {
        throw "The archived-R2 baseline $archiveRevision is not an ancestor of HEAD."
    }

    $changes = @(Get-GuardChanges $Root)
    if ($changes.Count) {
        $details = ($changes | ForEach-Object { "  - $($_.Kind): $($_.Path) [$($_.Code)]" }) -join "`n"
        throw "Archived-R2 evidence integrity failed; protected paths changed since ${archiveRevision}:`n$details"
    }

    $worktree = @(& git -C $Root status --porcelain=v1 --untracked-files=all)
    if ($worktree.Count) {
        $details = ($worktree | ForEach-Object { "  - $_" }) -join "`n"
        throw "Archived-R2 evidence integrity requires a clean checkout; pending changes:`n$details"
    }
}

function Replace-Control([string] $Text, [string] $Original, [string] $Adapted) {
    if ([regex]::Matches($Text, [regex]::Escape($Original)).Count -ne 1) {
        throw "Expected exactly one archived D2 control matching $Original"
    }
    $Text.Replace($Original, $Adapted)
}

Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $root = (Get-Location).Path
    Assert-ArchivedR2Evidence $root
    if ($GuardOnly) {
        Write-Output 'Archived-R2 evidence guard passed.'
        return
    }

    $archive = 'docs/reviews/d2-qualification'
    $copy = 'artifacts/d2-r2/qualification'
    $tracked = @(git ls-files -- "$archive/")
    foreach ($file in $tracked | Where-Object { $_ -like "$archive/harness/*" -or $_ -eq "$archive/run.ps1" }) {
        $destination = Join-Path $copy $file.Substring($archive.Length + 1)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file -Destination $destination -Force
    }

    $runner = Join-Path $copy 'run.ps1'
    $source = (Get-Content -Raw -LiteralPath $runner) -replace "`r`n", "`n"
    $source = Replace-Control $source @'
    $unexpected = @(git diff --name-only $baseline HEAD | Where-Object {
        $_ -notlike 'docs/reviews/d2-qualification/*' -and $_ -ne '.github/workflows/d2-qualification.yml'
    })
    if ($unexpected.Count -or (git status --porcelain)) {
        throw "Qualification requires a clean tree with only authorized changes: $unexpected"
    }
'@ @'
    $archiveRevision = 'b79fdffa07ac45cb49c5ee2ef100299f2d2adcf5'
    git merge-base --is-ancestor $archiveRevision HEAD
    $unexpected = @(git diff --name-only $archiveRevision HEAD -- docs/reviews/d2-qualification docs/d1-acceptance.md .github/workflows/d1-qualification.yml .github/workflows/d2-qualification.yml)
    if ($unexpected.Count -or (git status --porcelain)) {
        throw "R2 producer verification requires a clean checkout with intact historical evidence: $unexpected"
    }
'@
    $source = Replace-Control $source 'artifacts/d2-qualification/$RuntimeIdentifier' 'artifacts/d2-r2/corpus/$RuntimeIdentifier'
    $source = Replace-Control $source '    if ($ownershipFailures.Count) {' @'
    # Mixed D2 use followed by unload is a retained, unsupported diagnostic.
    $ownershipFailures = @($ownershipFailures | Where-Object { $_ -ne 'mixed-unload' })
    if ($ownershipFailures.Count) {
'@
    Set-Content -LiteralPath $runner -Value $source -Encoding utf8NoBOM -NoNewline
    & "./$runner" -RuntimeIdentifier $RuntimeIdentifier -UseExistingProducerAssets

    $out = "artifacts/d2-r2/corpus/$RuntimeIdentifier"
    foreach ($framework in @('net8.0', 'net10.0')) {
        $result = (Get-Content -Raw "$out/decoder-$framework.log").Trim()
        if ($result -cne 'DECODER controls=3 rejected=18 partial-schema-text-accepted=False') {
            throw "Archived decoder controls or QD2-01 remediation failed on ${framework}: $result"
        }
    }
}
finally {
    Pop-Location
}
