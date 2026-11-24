param(
    [switch] $Write
)

# Verifies (or regenerates) the R4 evidence manifest against the exact
# committed Git blobs.
#
# The repository forces LF (`.gitattributes`: `* text=auto eol=lf`), so the
# working-copy bytes of an evidence file can differ from its committed bytes
# on a Windows checkout. The manifest therefore hashes the committed blob, and
# this script reads that blob with `git cat-file blob` through a binary-safe
# redirection before hashing it.
#
# Usage:
#   pwsh -File verify-manifest.ps1            # verify the committed manifest
#   pwsh -File verify-manifest.ps1 -Write     # regenerate it from HEAD

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$manifestPath = Join-Path $PSScriptRoot 'manifest.sha256'
$evidenceRoot = 'docs/reviews/d3-impl-01-r4/evidence'
# The manifest covers the published evidence files. The manifest itself cannot
# contain its own hash, and the verification script is the tool that produces
# it, so neither is in scope. The paths are repository-relative because that is
# how `git ls-tree` reports them.
$excluded = @(
    "$evidenceRoot/manifest.sha256",
    "$evidenceRoot/verify-manifest.ps1"
)

function Remove-TemporaryBlob([string] $Path) {
    # The temporary blob is scratch space. It is removed with the framework
    # delete primitive rather than the PowerShell provider, because the
    # provider's Remove-Item can fail with "Access is denied" on a file that a
    # real-time scanner has just touched, even though the delete itself is
    # permitted. The removal is retried briefly and then abandoned; a blob that
    # is no longer visible to the filesystem is already gone.
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            [IO.File]::Delete($Path)
            if (-not [IO.File]::Exists($Path)) { return }
        }
        catch {
            if (-not [IO.File]::Exists($Path)) { return }
        }
        Start-Sleep -Milliseconds 200
    }
    Write-Warning "the temporary blob could not be removed and is left behind: $Path"
}

function Get-BlobHash([string] $Path) {
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("dogwood-blob-" + [guid]::NewGuid().ToString('N'))
    try {
        # cmd redirection keeps the byte stream unmodified.
        & cmd.exe /d /c "git -C `"$repository`" cat-file blob `"HEAD:$Path`" > `"$temporary`""
        if ($LASTEXITCODE -ne 0) {
            throw "git cat-file blob failed for $Path"
        }
        return (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    finally {
        Remove-TemporaryBlob $temporary
    }
}

$paths = @(git -C $repository ls-tree -r --name-only HEAD -- $evidenceRoot |
    Where-Object { $excluded -notcontains $_ } |
    Sort-Object)

if ($paths.Count -eq 0) {
    throw "no committed evidence files were found under $evidenceRoot"
}

$lines = foreach ($path in $paths) {
    '{0}  {1}' -f (Get-BlobHash $path), $path
}

if ($Write) {
    $text = ($lines -join "`n") + "`n"
    [IO.File]::WriteAllText($manifestPath, $text.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    Write-Output "manifest written: $manifestPath ($($paths.Count) files)"
    return
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "the manifest is missing: $manifestPath"
}

$expected = @{}
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split '\s+', 2
    $expected[$parts[1].Trim()] = $parts[0].Trim().ToLowerInvariant()
}
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($path in $expected.Keys | Sort-Object) {
    if (-not (Test-Path -LiteralPath (Join-Path $repository $path))) {
        $failures.Add("$path is listed in the manifest but not present in the checkout")
        continue
    }
    $actual = Get-BlobHash $path
    if ($actual -ne $expected[$path]) {
        $failures.Add("$path manifest hash $($expected[$path]) != committed blob hash $actual")
    }
}
foreach ($path in $paths) {
    if (-not $expected.ContainsKey($path)) {
        $failures.Add("$path is committed but missing from the manifest")
    }
}

if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Output "manifest verified against the committed blobs: $($expected.Count) files"
