[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $OutputPath,
    [string] $LogPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (!$RepositoryRoot) { $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')) }
$review = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-prep-01'
$inventoryPath = Join-Path $review 'publication-inventory.json'
$artifacts = Join-Path $review 'artifacts'
if (!$OutputPath) { $OutputPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/archive-controls.json' }
if (!$LogPath) { $LogPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/archive-controls.log' }

$work = Join-Path $RepositoryRoot ('artifacts/publication-archive-controls/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$script:log = New-Object System.Text.StringBuilder
$script:controls = @()

function Add-Log([string] $Text) {
    [void] $script:log.AppendLine($Text)
    Write-Host $Text
}

function Write-JsonFile([string] $Path, $Object) {
    $dir = Split-Path $Path -Parent
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, (($Object | ConvertTo-Json -Depth 12) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

function Copy-BaseArtifacts([string] $Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $artifacts '*') -Destination $Destination -Force
}

function New-Repacked([string] $Source, [string] $Destination, [scriptblock] $Mutate) {
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    $zip = [System.IO.Compression.ZipFile]::Open($Destination, [System.IO.Compression.ZipArchiveMode]::Update)
    try { & $Mutate $zip } finally { $zip.Dispose() }
}

function Add-ZipText($Zip, [string] $Name, [string] $Text) {
    $entry = $Zip.CreateEntry($Name)
    $writer = New-Object System.IO.StreamWriter($entry.Open())
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}

function Set-ZipText($Zip, [string] $Name, [string] $Text) {
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing entry $Name" }
    $entry.Delete()
    Add-ZipText $Zip $Name $Text
}

function Invoke-StoredEntryByteCorruption([string] $Path, [string] $Name) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($Name)
    for ($index = 0; $index -le ($bytes.Length - 30); $index++) {
        if ($bytes[$index] -ne 0x50 -or $bytes[$index + 1] -ne 0x4B -or $bytes[$index + 2] -ne 0x03 -or $bytes[$index + 3] -ne 0x04) { continue }
        $method = [int] ([BitConverter]::ToUInt16($bytes, $index + 8))
        $compressedSize = [int64] ([BitConverter]::ToUInt32($bytes, $index + 18))
        $nameLength = [int] ([BitConverter]::ToUInt16($bytes, $index + 26))
        $extraLength = [int] ([BitConverter]::ToUInt16($bytes, $index + 28))
        if ($nameLength -ne $nameBytes.Length) { continue }
        $nameMatch = $true
        for ($nameIndex = 0; $nameIndex -lt $nameLength; $nameIndex++) {
            if ($bytes[$index + 30 + $nameIndex] -ne $nameBytes[$nameIndex]) { $nameMatch = $false; break }
        }
        if (!$nameMatch) { continue }
        if ($method -ne 0) { throw "Entry '$Name' is not stored, so in-place corruption is unsafe." }
        $dataOffset = $index + 30 + $nameLength + $extraLength
        if ($compressedSize -lt 8 -or $dataOffset + $compressedSize -gt $bytes.Length) { throw "Entry '$Name' has an invalid stored size." }
        $target = $dataOffset + [int] $compressedSize - 1
        $bytes[$target] = [byte] ($bytes[$target] -bxor 0xFF)
        [System.IO.File]::WriteAllBytes($Path, $bytes)
        return
    }
    throw "Stored entry '$Name' was not found in $Path"
}

function Invoke-Reconcile([string] $Name, [string] $Inventory, [string] $ArtifactDirectory, [string] $Feed, [string] $SignatureMode, [switch] $RequireRepository, [switch] $FirstPublication) {
    $dir = Join-Path $work $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $report = Join-Path $dir 'report.json'
    $stdout = Join-Path $dir 'stdout.txt'
    $stderr = Join-Path $dir 'stderr.txt'
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $RepositoryRoot 'scripts/publication/Reconcile-Publication.ps1'),
        '-InventoryPath', $Inventory,
        '-ArtifactDirectory', $ArtifactDirectory,
        '-OutputPath', $report,
        '-OfflineFeedDirectory', $Feed,
        '-SignatureVerification', $SignatureMode,
        '-PollAttempts', '1',
        '-PollDelaySeconds', '0'
    )
    if ($RequireRepository) { $arguments += '-RequireRepositorySignature' }
    if ($FirstPublication) { $arguments += '-FirstPublication' }
    $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $stdoutText = if (Test-Path $stdout) { [string] (Get-Content -Raw $stdout) } else { '' }
    $stderrText = if (Test-Path $stderr) { [string] (Get-Content -Raw $stderr) } else { '' }
    if ($null -eq $stdoutText) { $stdoutText = '' }
    if ($null -eq $stderrText) { $stderrText = '' }
    $reportObject = $null
    if (Test-Path -LiteralPath $report) { $reportObject = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json }
    return [pscustomobject]@{
        name = $Name
        exitCode = $process.ExitCode
        reportWritten = (Test-Path -LiteralPath $report)
        stdout = $stdoutText.Trim()
        stderr = $stderrText.Trim()
        report = $reportObject
        reportPath = $report
    }
}

function Get-PackageRow($Result, [string] $Id) {
    if ($null -eq $Result.report) { return $null }
    return @($Result.report.packages | Where-Object { $_.id -ceq $Id })[0]
}

function Add-Control([string] $Name, [bool] $Passed, [string] $Detail, $Evidence) {
    $evidencePath = $null
    if ($null -ne $Evidence) {
        $evidenceDirectory = Join-Path (Split-Path $OutputPath -Parent) 'archive-evidence'
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        $safeName = $Name -replace '[^A-Za-z0-9._-]', '-'
        $evidencePath = Join-Path $evidenceDirectory "$safeName.json"
        Write-JsonFile $evidencePath $Evidence
    }
    $script:controls += [ordered]@{ name = $Name; passed = $Passed; detail = $Detail; evidenceFile = $evidencePath }
    if ($Passed) { Add-Log "CONTROL PASS $Name" } else { Add-Log "CONTROL FAIL $Name :: $Detail" }
}

# Self-match control.
$acceptedDir = Join-Path $work 'accepted'
Copy-BaseArtifacts $acceptedDir
$feedSelf = Join-Path $work 'feed-self'
Copy-BaseArtifacts $feedSelf
$self = Invoke-Reconcile -Name 'self-match' -Inventory $inventoryPath -ArtifactDirectory $acceptedDir -Feed $feedSelf -SignatureMode 'None' -FirstPublication
$selfRow = Get-PackageRow $self 'DogwoodSharp'
Add-Control 'accepted-original-self-match' ($self.exitCode -eq 0 -and $selfRow.status -eq 'published-matching' -and $selfRow.payloadMatches -eq $true) "exit=$($self.exitCode) status=$($selfRow.status)" ([ordered]@{ result = $self })

# Unexpected archive entry.
$feedExtra = Join-Path $work 'feed-extra'
Copy-BaseArtifacts $feedExtra
New-Repacked (Join-Path $artifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $feedExtra 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    Add-ZipText $zip 'unexpected-entry.txt' 'unexpected public payload'
}
$extra = Invoke-Reconcile -Name 'extra-entry' -Inventory $inventoryPath -ArtifactDirectory $acceptedDir -Feed $feedExtra -SignatureMode 'None' -FirstPublication
$extraRow = Get-PackageRow $extra 'DogwoodSharp'
Add-Control 'unexpected-entry-refused' ($extra.exitCode -eq 1 -and $extraRow.status -eq 'published-conflicting' -and (@($extraRow.payloadMismatches) -contains 'unexpected:unexpected-entry.txt')) "exit=$($extra.exitCode) status=$($extraRow.status) mismatches=$(@($extraRow.payloadMismatches) -join ',')" ([ordered]@{ result = $extra })

# Missing archive entry.
$feedMissing = Join-Path $work 'feed-missing'
Copy-BaseArtifacts $feedMissing
New-Repacked (Join-Path $artifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $feedMissing 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    $zip.GetEntry('README.md').Delete()
}
$missing = Invoke-Reconcile -Name 'missing-entry' -Inventory $inventoryPath -ArtifactDirectory $acceptedDir -Feed $feedMissing -SignatureMode 'None' -FirstPublication
$missingRow = Get-PackageRow $missing 'DogwoodSharp'
Add-Control 'missing-entry-refused' ($missing.exitCode -eq 1 -and $missingRow.status -eq 'published-conflicting' -and (@($missingRow.payloadMismatches) -contains 'missing:README.md')) "exit=$($missing.exitCode) status=$($missingRow.status) mismatches=$(@($missingRow.payloadMismatches) -join ',')" ([ordered]@{ result = $missing })

# Modified archive entry.
$feedModified = Join-Path $work 'feed-modified'
Copy-BaseArtifacts $feedModified
$originalReadme = ''
$zip = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $artifacts 'DogwoodSharp.0.1.0-preview.1.nupkg'))
try {
    $entry = $zip.GetEntry('README.md')
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { $originalReadme = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally { $zip.Dispose() }
New-Repacked (Join-Path $artifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $feedModified 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    Set-ZipText $zip 'README.md' ($originalReadme + "`nmodified-public-entry")
}
$modified = Invoke-Reconcile -Name 'modified-entry' -Inventory $inventoryPath -ArtifactDirectory $acceptedDir -Feed $feedModified -SignatureMode 'None' -FirstPublication
$modifiedRow = Get-PackageRow $modified 'DogwoodSharp'
Add-Control 'modified-entry-refused' ($modified.exitCode -eq 1 -and $modifiedRow.status -eq 'published-conflicting' -and (@($modifiedRow.payloadMismatches) -contains 'changed:README.md')) "exit=$($modified.exitCode) status=$($modifiedRow.status) mismatches=$(@($modifiedRow.payloadMismatches) -join ',')" ([ordered]@{ result = $modified })

# Duplicate archive entry identity.
$feedDuplicate = Join-Path $work 'feed-duplicate'
Copy-BaseArtifacts $feedDuplicate
New-Repacked (Join-Path $artifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $feedDuplicate 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    Add-ZipText $zip 'README.md' 'second README identity'
}
$duplicate = Invoke-Reconcile -Name 'duplicate-entry' -Inventory $inventoryPath -ArtifactDirectory $acceptedDir -Feed $feedDuplicate -SignatureMode 'None' -FirstPublication
$duplicateRow = Get-PackageRow $duplicate 'DogwoodSharp'
$duplicateMessage = (@($duplicateRow.payloadMismatches) -join ',')
Add-Control 'duplicate-entry-refused' ($duplicate.exitCode -eq 1 -and $duplicateRow.status -eq 'published-conflicting' -and $duplicateMessage -match 'duplicate') "exit=$($duplicate.exitCode) status=$($duplicateRow.status) mismatches=$duplicateMessage" ([ordered]@{ result = $duplicate })

# Unsafe path normalization.
$feedUnsafe = Join-Path $work 'feed-unsafe'
Copy-BaseArtifacts $feedUnsafe
New-Repacked (Join-Path $artifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') (Join-Path $feedUnsafe 'DogwoodSharp.0.1.0-preview.1.nupkg') {
    param($zip)
    Add-ZipText $zip '../escape.txt' 'unsafe path'
}
$unsafe = Invoke-Reconcile -Name 'unsafe-path' -Inventory $inventoryPath -ArtifactDirectory $acceptedDir -Feed $feedUnsafe -SignatureMode 'None' -FirstPublication
$unsafeRow = Get-PackageRow $unsafe 'DogwoodSharp'
Add-Control 'unsafe-path-refused' ($unsafe.exitCode -eq 1 -and $unsafeRow.status -eq 'published-conflicting' -and (@($unsafeRow.payloadMismatches) -join ',') -match 'relative path segment') "exit=$($unsafe.exitCode) status=$($unsafeRow.status) mismatches=$(@($unsafeRow.payloadMismatches) -join ',')" ([ordered]@{ result = $unsafe })

# Incorrect package identity or version.
$wrongVersionInventory = Join-Path $work 'inventory-wrong-version.json'
$inv = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
$inv.packages[0].version = '0.1.0-preview.9'
Write-JsonFile $wrongVersionInventory $inv
$wrongVersion = Invoke-Reconcile -Name 'wrong-version' -Inventory $wrongVersionInventory -ArtifactDirectory $acceptedDir -Feed $feedSelf -SignatureMode 'None' -FirstPublication
Add-Control 'incorrect-identity-refused' ($wrongVersion.exitCode -eq 1 -and $wrongVersion.stderr -match 'identity is not the approved identity') "exit=$($wrongVersion.exitCode) stderr=$($wrongVersion.stderr)" ([ordered]@{ result = $wrongVersion })

# Repository-signing fixture: a genuine NuGet.org repository-signed package,
# used only as a local, non-production fixture. The accepted original is the
# same package with its signature entry removed, so the only payload difference
# is the repository signature.
$fixtureSource = Join-Path $work 'newtonsoft.json.13.0.3.nupkg'
if (!(Test-Path -LiteralPath $fixtureSource)) {
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg' -OutFile $fixtureSource -UseBasicParsing
}
$fixtureHash = (Get-FileHash -LiteralPath $fixtureSource -Algorithm SHA256).Hash.ToLowerInvariant()
if ($fixtureHash -cne '872fc189e638ab1056555b03aaa38f68bcb54286e221aa646eb1129babf63c77') {
    throw "Repository-signature fixture hash is not the pinned value: $fixtureHash"
}
$sigAcceptedDir = Join-Path $work 'sig-accepted'
$sigFeedDir = Join-Path $work 'sig-feed'
New-Item -ItemType Directory -Force -Path $sigAcceptedDir, $sigFeedDir | Out-Null
Copy-Item -LiteralPath $fixtureSource -Destination (Join-Path $sigFeedDir 'newtonsoft.json.13.0.3.nupkg') -Force
Copy-Item -LiteralPath $fixtureSource -Destination (Join-Path $sigAcceptedDir 'newtonsoft.json.13.0.3.nupkg') -Force
$acceptedZip = [System.IO.Compression.ZipFile]::Open((Join-Path $sigAcceptedDir 'newtonsoft.json.13.0.3.nupkg'), [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $acceptedZip.GetEntry('.signature.p7s').Delete()
}
finally { $acceptedZip.Dispose() }
$acceptedFixtureHash = (Get-FileHash -LiteralPath (Join-Path $sigAcceptedDir 'newtonsoft.json.13.0.3.nupkg') -Algorithm SHA256).Hash.ToLowerInvariant()
$acceptedFixtureSize = (Get-Item -LiteralPath (Join-Path $sigAcceptedDir 'newtonsoft.json.13.0.3.nupkg')).Length
$sigInventory = Join-Path $work 'inventory-signature-fixture.json'
Write-JsonFile $sigInventory ([ordered]@{
    schemaVersion = 1
    inventoryId = 'DOGWOOD-SHARP-PUB-REMED-01-SIGNATURE-FIXTURE'
    releaseCandidate = [ordered]@{ commit = ('0' * 40); tree = ('0' * 40) }
    destination = 'https://api.nuget.org/v3/index.json'
    environment = 'fixture'
    packages = @([ordered]@{
        order = 1
        kind = 'package'
        id = 'Newtonsoft.Json'
        version = '13.0.3'
        file = 'newtonsoft.json.13.0.3.nupkg'
        size = [int64] $acceptedFixtureSize
        sha256 = $acceptedFixtureHash
    })
})
$validSignature = Invoke-Reconcile -Name 'valid-repository-signature' -Inventory $sigInventory -ArtifactDirectory $sigAcceptedDir -Feed $sigFeedDir -SignatureMode 'DotnetVerify' -RequireRepository -FirstPublication
$validRow = Get-PackageRow $validSignature 'Newtonsoft.Json'
Add-Control 'valid-repository-signing-transformation' ($validSignature.exitCode -eq 0 -and $validRow.status -eq 'published-matching' -and $validRow.payloadMatches -eq $true -and $validRow.repositorySignature.repository -eq $true) "exit=$($validSignature.exitCode) status=$($validRow.status) repository=$($validRow.repositorySignature.repository)" ([ordered]@{ result = $validSignature; fixtureSha256 = $fixtureHash; acceptedFixtureSha256 = $acceptedFixtureHash })

# Invalid signature: corrupt the stored repository signature without changing
# the archive entry identity or its uncompressed storage.
$feedInvalidSignature = Join-Path $work 'feed-invalid-signature'
New-Item -ItemType Directory -Force -Path $feedInvalidSignature | Out-Null
Copy-Item -LiteralPath $fixtureSource -Destination (Join-Path $feedInvalidSignature 'newtonsoft.json.13.0.3.nupkg') -Force
Invoke-StoredEntryByteCorruption (Join-Path $feedInvalidSignature 'newtonsoft.json.13.0.3.nupkg') '.signature.p7s'
$invalidSignature = Invoke-Reconcile -Name 'invalid-signature' -Inventory $sigInventory -ArtifactDirectory $sigAcceptedDir -Feed $feedInvalidSignature -SignatureMode 'DotnetVerify' -RequireRepository -FirstPublication
$invalidRow = Get-PackageRow $invalidSignature 'Newtonsoft.Json'
$invalidVerify = if ($null -ne $invalidRow -and $null -ne $invalidRow.repositorySignature) { $invalidRow.repositorySignature.verified } else { $null }
Add-Control 'invalid-signature-refused' ($invalidSignature.exitCode -eq 1 -and $invalidRow.status -eq 'published-conflicting' -and $invalidVerify -eq $false) "exit=$($invalidSignature.exitCode) status=$($invalidRow.status) verify=$invalidVerify" ([ordered]@{ result = $invalidSignature })

# Unexpected signing-related content: add a second signature-like entry.
$feedUnexpectedSignature = Join-Path $work 'feed-unexpected-signature'
New-Item -ItemType Directory -Force -Path $feedUnexpectedSignature | Out-Null
Copy-Item -LiteralPath $fixtureSource -Destination (Join-Path $feedUnexpectedSignature 'newtonsoft.json.13.0.3.nupkg') -Force
$signatureBytes = $null
$fixtureZip = [System.IO.Compression.ZipFile]::OpenRead($fixtureSource)
try {
    $sigEntry = $fixtureZip.GetEntry('.signature.p7s')
    $sigStream = $sigEntry.Open()
    try {
        $memory = New-Object System.IO.MemoryStream
        try { $sigStream.CopyTo($memory); $signatureBytes = [byte[]] $memory.ToArray() } finally { $memory.Dispose() }
    }
    finally { $sigStream.Dispose() }
}
finally { $fixtureZip.Dispose() }
$unexpectedZip = [System.IO.Compression.ZipFile]::Open((Join-Path $feedUnexpectedSignature 'newtonsoft.json.13.0.3.nupkg'), [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $unexpectedZip.CreateEntry('unexpected-signature.p7s')
    $stream = $entry.Open()
    try { $stream.Write($signatureBytes, 0, $signatureBytes.Length) } finally { $stream.Dispose() }
}
finally { $unexpectedZip.Dispose() }
$unexpectedSignature = Invoke-Reconcile -Name 'unexpected-signature' -Inventory $sigInventory -ArtifactDirectory $sigAcceptedDir -Feed $feedUnexpectedSignature -SignatureMode 'Structural' -FirstPublication
$unexpectedRow = Get-PackageRow $unexpectedSignature 'Newtonsoft.Json'
Add-Control 'unexpected-signing-content-refused' ($unexpectedSignature.exitCode -eq 1 -and $unexpectedRow.status -eq 'published-conflicting' -and (@($unexpectedRow.payloadMismatches) -join ',') -match 'unexpected:unexpected-signature.p7s') "exit=$($unexpectedSignature.exitCode) status=$($unexpectedRow.status) mismatches=$(@($unexpectedRow.payloadMismatches) -join ',')" ([ordered]@{ result = $unexpectedSignature })

# Untrusted author signature fixture (self-signed, generated locally).
$untrustedDir = Join-Path ([System.IO.Path]::GetTempPath()) ('dogwoodsharp-untrusted-fixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $untrustedDir | Out-Null
$projectDir = Join-Path $untrustedDir 'project'
New-Item -ItemType Directory -Force -Path $projectDir | Out-Null
$projectText = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>DogwoodSharp.RemTestFixture</PackageId>
    <Version>1.0.0</Version>
    <Authors>Remediation Test</Authors>
    <Description>Local untrusted-signature fixture</Description>
  </PropertyGroup>
</Project>
'@
[System.IO.File]::WriteAllText((Join-Path $projectDir 'Fixture.csproj'), $projectText, (New-Object System.Text.UTF8Encoding($false)))
Push-Location $projectDir
try {
    & dotnet pack -o (Join-Path $untrustedDir 'out') --nologo -v q 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to pack the untrusted-signature fixture.' }
}
finally { Pop-Location }
$unsignedFixture = Join-Path $untrustedDir 'out/DogwoodSharp.RemTestFixture.1.0.0.nupkg'
$signedFixture = Join-Path $untrustedDir 'signed/DogwoodSharp.RemTestFixture.1.0.0.nupkg'
New-Item -ItemType Directory -Force -Path (Join-Path $untrustedDir 'signed') | Out-Null
Copy-Item -LiteralPath $unsignedFixture -Destination $signedFixture -Force
$certificate = $null
try {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=DogwoodSharp Remediation Untrusted Fixture' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(1)
    $password = ConvertTo-SecureString 'fixture-only' -AsPlainText -Force
    $pfx = Join-Path $untrustedDir 'fixture.pfx'
    Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password $password | Out-Null
    Push-Location ([System.IO.Path]::GetTempPath())
    try {
        & dotnet nuget sign $signedFixture --certificate-path $pfx --certificate-password 'fixture-only' --overwrite 2>&1 | Out-Null
    }
    finally { Pop-Location }
    $untrustedAccepted = Join-Path $untrustedDir 'accepted'
    $untrustedFeed = Join-Path $untrustedDir 'feed'
    New-Item -ItemType Directory -Force -Path $untrustedAccepted, $untrustedFeed | Out-Null
    Copy-Item -LiteralPath $unsignedFixture -Destination (Join-Path $untrustedAccepted 'DogwoodSharp.RemTestFixture.1.0.0.nupkg') -Force
    Copy-Item -LiteralPath $signedFixture -Destination (Join-Path $untrustedFeed 'DogwoodSharp.RemTestFixture.1.0.0.nupkg') -Force
    $untrustedHash = (Get-FileHash -LiteralPath (Join-Path $untrustedAccepted 'DogwoodSharp.RemTestFixture.1.0.0.nupkg') -Algorithm SHA256).Hash.ToLowerInvariant()
    $untrustedSize = (Get-Item -LiteralPath (Join-Path $untrustedAccepted 'DogwoodSharp.RemTestFixture.1.0.0.nupkg')).Length
    $untrustedInventory = Join-Path $untrustedDir 'inventory.json'
    Write-JsonFile $untrustedInventory ([ordered]@{
        schemaVersion = 1
        inventoryId = 'DOGWOOD-SHARP-PUB-REMED-01-UNTRUSTED-SIGNATURE-FIXTURE'
        releaseCandidate = [ordered]@{ commit = ('0' * 40); tree = ('0' * 40) }
        destination = 'https://api.nuget.org/v3/index.json'
        environment = 'fixture'
        packages = @([ordered]@{
            order = 1
            kind = 'package'
            id = 'DogwoodSharp.RemTestFixture'
            version = '1.0.0'
            file = 'DogwoodSharp.RemTestFixture.1.0.0.nupkg'
            size = [int64] $untrustedSize
            sha256 = $untrustedHash
        })
    })
    $untrusted = Invoke-Reconcile -Name 'untrusted-signature' -Inventory $untrustedInventory -ArtifactDirectory $untrustedAccepted -Feed $untrustedFeed -SignatureMode 'DotnetVerify' -RequireRepository -FirstPublication
    $untrustedRow = Get-PackageRow $untrusted 'DogwoodSharp.RemTestFixture'
    $untrustedVerify = if ($null -ne $untrustedRow -and $null -ne $untrustedRow.repositorySignature) { $untrustedRow.repositorySignature.verified } else { $null }
    Add-Control 'untrusted-signature-refused' ($untrusted.exitCode -eq 1 -and $untrustedRow.status -eq 'published-conflicting' -and $untrustedVerify -eq $false) "exit=$($untrusted.exitCode) status=$($untrustedRow.status) verify=$untrustedVerify" ([ordered]@{ result = $untrusted; certificateSha256 = $certificate.GetCertHashString('SHA256') })
}
finally {
    if ($null -ne $certificate) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
}

$failed = @($script:controls | Where-Object { !$_.passed })
$resultObject = [ordered]@{
    schemaVersion = 1
    commission = 'DOGWOOD-SHARP-PUB-REMED-01'
    executedAtUtc = [DateTime]::UtcNow.ToString('o')
    total = $script:controls.Count
    passed = @($script:controls | Where-Object { $_.passed }).Count
    failed = $failed.Count
    controls = $script:controls
}
Write-JsonFile $OutputPath $resultObject
$logDir = Split-Path $LogPath -Parent
if ($logDir) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
[System.IO.File]::WriteAllText($LogPath, $script:log.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Add-Log "ARCHIVE_CONTROLS=$($resultObject.passed)/$($resultObject.total)"
if ($failed.Count -ne 0) { exit 1 }
exit 0
