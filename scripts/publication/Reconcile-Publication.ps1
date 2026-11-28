[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InventoryPath,
    [Parameter(Mandatory)] [string] $ArtifactDirectory,
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $OfflineFeedDirectory,
    [string] $Destination,
    [switch] $VerifyRepositorySignature
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_RECONCILIATION_REFUSED: $Message")
    exit 1
}

function Get-LowerSha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ZipEntryMap([string] $Path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $map = @{}
        foreach ($entry in @($archive.Entries | Where-Object { !$_.FullName.EndsWith('/') })) {
            $stream = $entry.Open()
            try {
                $memory = New-Object System.IO.MemoryStream
                try {
                    $stream.CopyTo($memory)
                    $sha = [System.Security.Cryptography.SHA256]::Create()
                    try {
                        $hash = ([BitConverter]::ToString($sha.ComputeHash($memory.ToArray()))).Replace('-', '').ToLowerInvariant()
                    }
                    finally { $sha.Dispose() }
                    $map[$entry.FullName] = $hash
                }
                finally { $memory.Dispose() }
            }
            finally { $stream.Dispose() }
        }
        return $map
    }
    finally {
        $archive.Dispose()
    }
}

function Test-PayloadMatch([string] $AcceptedPath, [string] $PublicPath) {
    $accepted = Get-ZipEntryMap $AcceptedPath
    $public = Get-ZipEntryMap $PublicPath
    $excluded = @(
        '_rels/.rels',
        '[Content_Types].xml',
        '.signature.p7s'
    )
    $payload = @($accepted.Keys | Where-Object {
        $_ -notin $excluded -and $_ -notlike 'package/services/metadata/core-properties/*'
    } | Sort-Object -CaseSensitive)
    $mismatches = @()
    foreach ($name in $payload) {
        if (!$public.ContainsKey($name)) {
            $mismatches += "missing:$name"
        }
        elseif ($public[$name] -cne $accepted[$name]) {
            $mismatches += "changed:$name"
        }
    }
    return [pscustomobject]@{
        payloadMatches = ($mismatches.Count -eq 0)
        mismatches = @($mismatches)
        acceptedPayloadEntries = $payload.Count
        publicHasSignature = $public.ContainsKey('.signature.p7s')
    }
}

$inventory = Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json
if (!$Destination) { $Destination = [string] $inventory.destination }
if ([string] $inventory.destination -cne [string] $Destination) {
    Refuse('Reconciliation destination is not the approved destination.')
}
if (!$OfflineFeedDirectory -and [string] $inventory.destination -cne 'https://api.nuget.org/v3/index.json') {
    Refuse('Online reconciliation only supports the approved NuGet.org destination.')
}
if ($OfflineFeedDirectory -and !(Test-Path -LiteralPath $OfflineFeedDirectory -PathType Container)) {
    Refuse("Offline feed directory is missing: $OfflineFeedDirectory")
}

$results = @()
$conflicts = @()
foreach ($package in @($inventory.packages | Where-Object { $_.kind -eq 'package' })) {
    $acceptedPath = Join-Path $ArtifactDirectory $package.file
    if (!(Test-Path -LiteralPath $acceptedPath -PathType Leaf)) {
        Refuse("Accepted package is missing: $($package.file)")
    }
    $acceptedHash = Get-LowerSha256 $acceptedPath
    if ($acceptedHash -cne [string] $package.sha256) {
        Refuse("Accepted package is not the approved artifact: $($package.file)")
    }

    $publicPath = $null
    $publicHash = $null
    $status = 'missing'
    $signature = $null
    if ($OfflineFeedDirectory) {
        $candidate = Join-Path $OfflineFeedDirectory $package.file
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $publicPath = $candidate
        }
    }
    else {
        $id = ([string] $package.id).ToLowerInvariant()
        $version = ([string] $package.version).ToLowerInvariant()
        $url = "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg"
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("dogwoodsharp-reconcile-" + [Guid]::NewGuid().ToString('N') + '.nupkg')
        try {
            Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -ErrorAction Stop | Out-Null
            $publicPath = $tmp
        }
        catch {
            $response = $_.Exception.Response
            $code = if ($response) { [int] $response.StatusCode } else { 0 }
            if ($code -ne 404) {
                Refuse("NuGet registry query failed for $($package.id) $($package.version): HTTP $code")
            }
        }
    }

    if ($publicPath) {
        $publicHash = Get-LowerSha256 $publicPath
        $comparison = Test-PayloadMatch $acceptedPath $publicPath
        if (!$comparison.payloadMatches) {
            $status = 'conflict'
            $conflicts += "$($package.id)/$($package.version): $($comparison.mismatches -join ', ')"
        }
        else {
            $status = 'published-matching'
        }
        if ($VerifyRepositorySignature) {
            $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
            if ($null -eq $dotnet) {
                $signature = [ordered]@{ verified = $false; note = 'dotnet CLI is unavailable.' }
            }
            else {
                $out = & dotnet nuget verify $publicPath --all 2>&1
                $signature = [ordered]@{
                    verified = ($LASTEXITCODE -eq 0)
                    exitCode = $LASTEXITCODE
                    output = (@($out) -join "`n").Trim()
                }
            }
        }
    }

    $action = if ($status -eq 'conflict') { 'refuse' } elseif ($status -eq 'published-matching') { 'skip' } else { 'push' }
    $results += [ordered]@{
        file = [string] $package.file
        id = [string] $package.id
        version = [string] $package.version
        acceptedUploadSha256 = $acceptedHash
        publicDownloadedSha256 = $publicHash
        payloadMatches = if ($publicPath) { $comparison.payloadMatches } else { $null }
        payloadMismatches = if ($publicPath) { $comparison.mismatches } else { @() }
        publicHasRepositorySignature = if ($publicPath) { $comparison.publicHasSignature } else { $null }
        repositorySignature = $signature
        status = $status
        action = $action
    }
}

$symbolResults = @()
foreach ($symbol in @($inventory.packages | Where-Object { $_.kind -eq 'symbols' })) {
    $symbolResults += [ordered]@{
        file = [string] $symbol.file
        id = [string] $symbol.id
        version = [string] $symbol.version
        status = 'symbol-server-verification-required'
        note = 'NuGet.org does not expose the .snupkg in the flat container. Verify the PDBs through https://symbols.nuget.org/download/symbols/ using the PDB name and GUID/age.'
    }
}

$report = [ordered]@{
    destination = [string] $Destination
    offlineFeedDirectory = $OfflineFeedDirectory
    packages = $results
    symbols = $symbolResults
    conflicts = $conflicts
    result = if ($conflicts.Count -eq 0) { 'no-conflict' } else { 'conflict' }
}
$outDir = Split-Path ([System.IO.Path]::GetFullPath($OutputPath)) -Parent
if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputPath), (($report | ConvertTo-Json -Depth 10) + "`n"), (New-Object System.Text.UTF8Encoding($false)))

foreach ($row in $results) {
    Write-Host "PACKAGE $($row.id) $($row.version) $($row.status) $($row.action)"
}
if ($conflicts.Count -ne 0) {
    Refuse("Publication conflicts: $($conflicts -join '; ')")
}
Write-Host 'PUBLICATION_RECONCILIATION=NO_CONFLICT'
exit 0
