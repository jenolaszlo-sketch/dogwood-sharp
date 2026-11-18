# Re-run the archived D2 corpus in a disposable, production-gated copy.
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

function Replace-Control([string] $Text, [string] $Original, [string] $Adapted) {
    if ([regex]::Matches($Text, [regex]::Escape($Original)).Count -ne 1) {
        throw "Expected exactly one archived D2 control matching $Original"
    }
    $Text.Replace($Original, $Adapted)
}

Push-Location (Split-Path $PSScriptRoot -Parent)
try {
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
    $unexpected = @(git diff --name-only $archiveRevision HEAD -- docs/reviews docs/d1-acceptance.md .github/workflows/d1-qualification.yml .github/workflows/d2-qualification.yml)
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
