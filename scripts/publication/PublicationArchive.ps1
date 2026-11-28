# Publication archive inspection helpers.
# Shared by the publication verifier, the registry reconciler and the publication
# operation driver. The functions are deliberately data-only: no dynamic code
# execution, no extraction to disk and no path-dependent lookup of archive entries.

Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:RepositorySignatureEntry = '.signature.p7s'

function Get-Sha256Hex([byte[]] $Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-FileSha256Hex([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-SafeArchiveEntryName([string] $Name) {
    if ([string]::IsNullOrEmpty($Name)) { return 'empty entry name' }
    if ($Name.Length -gt 1024) { return 'entry name is too long' }
    if ($Name.StartsWith('/')) { return 'absolute entry name' }
    if ($Name.Contains('\')) { return 'backslash in entry name' }
    if ($Name.Contains(':')) { return 'colon or drive designator in entry name' }
    foreach ($character in $Name.ToCharArray()) {
        if ([int] $character -lt 32 -or [int] $character -eq 127) {
            return 'control character in entry name'
        }
    }
    if ($Name -match '%2f' -or $Name -match '%5c') {
        return 'percent-encoded path separator in entry name'
    }
    $isDirectory = $Name.EndsWith('/')
    $candidate = if ($isDirectory) { $Name.Substring(0, $Name.Length - 1) } else { $Name }
    if ($candidate.Length -eq 0) { return 'empty entry name' }
    foreach ($segment in $candidate.Split('/')) {
        if ($segment.Length -eq 0) { return 'empty path segment in entry name' }
        if ($segment -eq '.' -or $segment -eq '..') { return 'relative path segment in entry name' }
        if ($segment.EndsWith(' ') -or $segment.EndsWith('.')) {
            return 'ambiguous trailing space or dot in entry name'
        }
    }
    try {
        $normalized = $candidate.Normalize([System.Text.NormalizationForm]::FormC)
    }
    catch {
        return 'entry name cannot be normalized'
    }
    if ($normalized -cne $candidate) { return 'non-normalized Unicode entry name' }
    return $null
}

function Read-ArchiveEntries([string] $Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Archive is missing: $Path"
    }
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @()
        $exactNames = @{}
        $caseFoldNames = @{}
        foreach ($entry in @($archive.Entries)) {
            $name = [string] $entry.FullName
            $problem = Test-SafeArchiveEntryName $name
            if ($null -ne $problem) {
                throw "Unsafe or ambiguous archive entry '$name' in $([System.IO.Path]::GetFileName($Path)): $problem"
            }
            if ($exactNames.ContainsKey($name)) {
                throw "Duplicate archive entry identity '$name' in $([System.IO.Path]::GetFileName($Path))"
            }
            $fold = $name.ToLowerInvariant()
            if ($caseFoldNames.ContainsKey($fold)) {
                throw "Ambiguous case-insensitive archive entry identities '$($caseFoldNames[$fold])' and '$name' in $([System.IO.Path]::GetFileName($Path))"
            }
            $exactNames[$name] = $true
            $caseFoldNames[$fold] = $name

            $isDirectory = $name.EndsWith('/')
            $sha = $null
            $length = [int64] $entry.Length
            $compressedLength = [int64] $entry.CompressedLength
            if (!$isDirectory) {
                $stream = $entry.Open()
                try {
                    $memory = New-Object System.IO.MemoryStream
                    try {
                        $stream.CopyTo($memory)
                        $bytes = [byte[]] $memory.ToArray()
                        $sha = Get-Sha256Hex $bytes
                        if ([int64] $bytes.Length -ne $length) {
                            throw "Archive entry length is inconsistent for '$name'"
                        }
                    }
                    finally {
                        $memory.Dispose()
                    }
                }
                finally {
                    $stream.Dispose()
                }
            }
            $entries += [pscustomobject]@{
                FullName = $name
                IsDirectory = $isDirectory
                Length = $length
                CompressedLength = $compressedLength
                Sha256 = $sha
            }
        }
        return $entries
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ArchiveEntryMap($Entries) {
    $map = @{}
    foreach ($entry in @($Entries)) {
        if ($entry.IsDirectory) { continue }
        $map[$entry.FullName] = $entry
    }
    return $map
}

function Test-PackagePayloadEquivalence(
    [string] $AcceptedPath,
    [string] $PublicPath,
    [switch] $AllowRepositorySignature) {

    $acceptedEntries = @(Read-ArchiveEntries $AcceptedPath)
    $publicEntries = @(Read-ArchiveEntries $PublicPath)
    $accepted = Get-ArchiveEntryMap $acceptedEntries
    $public = Get-ArchiveEntryMap $publicEntries

    $expected = @($accepted.Keys | Sort-Object -CaseSensitive)
    $observed = @($public.Keys | Sort-Object -CaseSensitive)
    $missing = @($expected | Where-Object { !$public.ContainsKey($_) })
    $unexpected = @($observed | Where-Object { !$accepted.ContainsKey($_) })
    $changed = @()
    $signaturePresent = $public.ContainsKey($script:RepositorySignatureEntry)
    $signatureProblems = @()

    if ($AllowRepositorySignature) {
        if ($accepted.ContainsKey($script:RepositorySignatureEntry)) {
            $signatureProblems += 'accepted-original-already-contains-signature'
        }
        if (!$signaturePresent) {
            $signatureProblems += 'repository-signature-missing'
        }
        else {
            $unexpected = @($unexpected | Where-Object { $_ -cne $script:RepositorySignatureEntry })
            $signatureEntry = $public[$script:RepositorySignatureEntry]
            if ($signatureEntry.CompressedLength -ne $signatureEntry.Length) {
                $signatureProblems += 'repository-signature-is-compressed'
            }
            if ($signatureEntry.Length -lt 64) {
                $signatureProblems += 'repository-signature-is-too-small'
            }
            $signatureBytes = Get-ArchiveEntryBytes $PublicPath $script:RepositorySignatureEntry
            if ($signatureBytes.Length -lt 2 -or $signatureBytes[0] -ne 0x30) {
                $signatureProblems += 'repository-signature-is-not-a-der-sequence'
            }
        }
    }
    elseif ($signaturePresent) {
        $unexpected += $script:RepositorySignatureEntry
        $unexpected = @($unexpected | Sort-Object -Unique -CaseSensitive)
    }

    foreach ($name in $expected) {
        if (!$public.ContainsKey($name)) { continue }
        if ([string] $public[$name].Sha256 -cne [string] $accepted[$name].Sha256) {
            $changed += $name
        }
        elseif ([int64] $public[$name].Length -ne [int64] $accepted[$name].Length) {
            $changed += $name
        }
    }

    $mismatches = @()
    foreach ($name in $missing) { $mismatches += "missing:$name" }
    foreach ($name in $unexpected) { $mismatches += "unexpected:$name" }
    foreach ($name in $changed) { $mismatches += "changed:$name" }
    foreach ($problem in $signatureProblems) { $mismatches += "signature:$problem" }

    return [pscustomobject]@{
        payloadMatches = ($mismatches.Count -eq 0)
        mismatches = @($mismatches | Sort-Object -CaseSensitive)
        missing = @($missing | Sort-Object -CaseSensitive)
        unexpected = @($unexpected | Sort-Object -CaseSensitive)
        changed = @($changed | Sort-Object -CaseSensitive)
        acceptedPayloadEntries = $accepted.Count
        publicPayloadEntries = $public.Count
        publicHasSignature = $signaturePresent
        signatureValidated = $false
    }
}

function Get-ArchiveEntryBytes([string] $Path, [string] $Name) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $matches = @($archive.Entries | Where-Object { $_.FullName -ceq $Name })
        if ($matches.Count -ne 1) {
            throw "Archive entry '$Name' is missing or duplicated in $Path"
        }
        $stream = $matches[0].Open()
        try {
            $memory = New-Object System.IO.MemoryStream
            try {
                $stream.CopyTo($memory)
                return [byte[]] $memory.ToArray()
            }
            finally { $memory.Dispose() }
        }
        finally { $stream.Dispose() }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ArchiveEntryText([string] $Path, [string] $Name) {
    $bytes = Get-ArchiveEntryBytes $Path $Name
    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    try { return $utf8.GetString($bytes) }
    catch { throw "Archive entry is not valid UTF-8: $Name" }
}

function Get-NuspecIdentity([string] $Path, [string] $ExpectedId) {
    $nuspecName = "$ExpectedId.nuspec"
    $text = Get-ArchiveEntryText $Path $nuspecName
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    try { $xml.LoadXml(($text -replace '^\uFEFF', '')) }
    catch { throw "Package nuspec is not valid XML: $Path" }
    $idNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='id']")
    $versionNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']")
    if ($null -eq $idNode -or $null -eq $versionNode) {
        throw "Package nuspec has no exact id or version: $Path"
    }
    return [pscustomobject]@{
        id = [string] $idNode.InnerText
        version = [string] $versionNode.InnerText
    }
}

function Read-UInt16([byte[]] $Bytes, [int] $Offset) {
    $low = [int] $Bytes[$Offset]
    $high = [int] $Bytes[$Offset + 1]
    return [int] ($low -bor ($high -shl 8))
}

function Read-UInt32([byte[]] $Bytes, [int] $Offset) {
    $b0 = [int64] $Bytes[$Offset]
    $b1 = [int64] $Bytes[$Offset + 1]
    $b2 = [int64] $Bytes[$Offset + 2]
    $b3 = [int64] $Bytes[$Offset + 3]
    return [int64] ([uint32] ($b0 -bor ($b1 -shl 8) -bor ($b2 -shl 16) -bor ($b3 -shl 24)))
}

function Get-PortablePdbIdentity([string] $PdbPath) {
    $bytes = [System.IO.File]::ReadAllBytes($PdbPath)
    if ($bytes.Length -lt 32 -or (Read-UInt32 $bytes 0) -ne 0x424A5342) {
        throw "PDB is not a supported portable PDB: $PdbPath"
    }
    $versionLength = [int] (Read-UInt32 $bytes 12)
    $position = 16 + $versionLength
    $position = ($position + 3) -band (-bnot 3)
    if ($position + 4 -gt $bytes.Length) { throw "Portable PDB metadata root is truncated: $PdbPath" }
    $streamCount = Read-UInt16 $bytes ($position + 2)
    $position += 4
    $pdbStreamOffset = -1
    $pdbStreamSize = -1
    for ($index = 0; $index -lt $streamCount; $index++) {
        if ($position + 8 -gt $bytes.Length) { throw "Portable PDB stream header is truncated: $PdbPath" }
        $offset = [int] (Read-UInt32 $bytes $position)
        $size = [int] (Read-UInt32 $bytes ($position + 4))
        $nameStart = $position + 8
        $nameEnd = $nameStart
        while ($nameEnd -lt $bytes.Length -and $bytes[$nameEnd] -ne 0) { $nameEnd++ }
        if ($nameEnd -ge $bytes.Length) { throw "Portable PDB stream name is truncated: $PdbPath" }
        $name = [System.Text.Encoding]::UTF8.GetString($bytes, $nameStart, $nameEnd - $nameStart)
        $position = $nameStart + ($nameEnd - $nameStart) + 1
        $position = ($position + 3) -band (-bnot 3)
        if ($name -ceq '#Pdb') {
            $pdbStreamOffset = $offset
            $pdbStreamSize = $size
        }
    }
    if ($pdbStreamOffset -lt 0 -or $pdbStreamSize -lt 20) {
        throw "Portable PDB has no usable #Pdb stream: $PdbPath"
    }
    if ($pdbStreamOffset + 20 -gt $bytes.Length) {
        throw "Portable PDB #Pdb stream is truncated: $PdbPath"
    }
    $guidBytes = New-Object byte[] 16
    [Array]::Copy($bytes, $pdbStreamOffset, $guidBytes, 0, 16)
    $guid = ([System.Guid]::new($guidBytes)).ToString('N').ToUpperInvariant()
    $stamp = [uint32] (Read-UInt32 $bytes ($pdbStreamOffset + 16))
    $checksumBytes = [byte[]] $bytes.Clone()
    for ($index = 0; $index -lt 20; $index++) {
        $checksumBytes[$pdbStreamOffset + $index] = 0
    }
    return [pscustomobject]@{
        guid = $guid
        stamp = $stamp
        sha256 = Get-Sha256Hex $bytes
        checksumSha256 = Get-Sha256Hex $checksumBytes
        size = [int64] $bytes.Length
        format = 'portable-pdb'
    }
}

function Get-PeImageDebugIdentity([string] $DllPath) {
    $bytes = [System.IO.File]::ReadAllBytes($DllPath)
    if ($bytes.Length -lt 0x40) { throw "PE image is truncated: $DllPath" }
    if ($bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "PE image has no DOS signature: $DllPath" }
    $peOffset = [int] (Read-UInt32 $bytes 0x3C)
    if ($peOffset + 24 -gt $bytes.Length) { throw "PE header is truncated: $DllPath" }
    if ($bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45 -or $bytes[$peOffset + 2] -ne 0 -or $bytes[$peOffset + 3] -ne 0) {
        throw "PE image has no PE signature: $DllPath"
    }
    $sectionCount = Read-UInt16 $bytes ($peOffset + 6)
    $optionalHeaderSize = Read-UInt16 $bytes ($peOffset + 20)
    $optionalOffset = $peOffset + 24
    if ($optionalOffset + $optionalHeaderSize -gt $bytes.Length) { throw "PE optional header is truncated: $DllPath" }
    $magic = Read-UInt16 $bytes $optionalOffset
    $dataDirectoryOffset = if ($magic -eq 0x20B) { $optionalOffset + 112 } elseif ($magic -eq 0x10B) { $optionalOffset + 96 } else { throw "Unsupported PE optional header magic: $DllPath" }
    $debugRva = [int] (Read-UInt32 $bytes ($dataDirectoryOffset + (6 * 8)))
    $debugSize = [int] (Read-UInt32 $bytes ($dataDirectoryOffset + (6 * 8) + 4))
    if ($debugRva -le 0 -or $debugSize -le 0) { throw "PE image has no debug directory: $DllPath" }

    $sectionOffset = $optionalOffset + $optionalHeaderSize
    $sections = @()
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $offset = $sectionOffset + ($index * 40)
        if ($offset + 40 -gt $bytes.Length) { throw "PE section table is truncated: $DllPath" }
        $sections += [pscustomobject]@{
            virtualAddress = [int] (Read-UInt32 $bytes ($offset + 12))
            virtualSize = [int] (Read-UInt32 $bytes ($offset + 8))
            rawSize = [int] (Read-UInt32 $bytes ($offset + 16))
            rawPointer = [int] (Read-UInt32 $bytes ($offset + 20))
        }
    }
    function Convert-RvaToOffset([int64] $Rva) {
        foreach ($section in $sections) {
            $span = [Math]::Max($section.virtualSize, $section.rawSize)
            if ($Rva -ge $section.virtualAddress -and $Rva -lt ($section.virtualAddress + $span)) {
                return [int] ($section.rawPointer + ($Rva - $section.virtualAddress))
            }
        }
        throw "PE RVA does not map to a section: $DllPath"
    }

    $debugOffset = Convert-RvaToOffset $debugRva
    $count = [int] ($debugSize / 28)
    $codeView = $null
    $pdbChecksum = $null
    for ($index = 0; $index -lt $count; $index++) {
        $entryOffset = $debugOffset + ($index * 28)
        if ($entryOffset + 28 -gt $bytes.Length) { throw "PE debug directory is truncated: $DllPath" }
        $type = [int] (Read-UInt32 $bytes ($entryOffset + 12))
        $dataSize = [int] (Read-UInt32 $bytes ($entryOffset + 16))
        $dataRva = [int] (Read-UInt32 $bytes ($entryOffset + 20))
        $dataPointer = [int] (Read-UInt32 $bytes ($entryOffset + 24))
        if ($type -ne 2 -and $type -ne 19) { continue }
        $dataOffset = if ($dataPointer -gt 0) { $dataPointer } else { Convert-RvaToOffset $dataRva }
        if ($dataOffset + $dataSize -gt $bytes.Length) {
            throw "PE debug data is truncated: $DllPath"
        }
        if ($type -eq 2) {
            if ($dataSize -lt 24) { throw "PE CodeView data is truncated: $DllPath" }
            if ($bytes[$dataOffset] -ne 0x52 -or $bytes[$dataOffset + 1] -ne 0x53 -or $bytes[$dataOffset + 2] -ne 0x44 -or $bytes[$dataOffset + 3] -ne 0x53) {
                throw "PE CodeView data does not use the RSDS format: $DllPath"
            }
            $guidBytes = New-Object byte[] 16
            [Array]::Copy($bytes, $dataOffset + 4, $guidBytes, 0, 16)
            $guid = ([System.Guid]::new($guidBytes)).ToString('N').ToUpperInvariant()
            $age = [int] (Read-UInt32 $bytes ($dataOffset + 20))
            $pathStart = $dataOffset + 24
            $pathEnd = $pathStart
            while ($pathEnd -lt ($dataOffset + $dataSize) -and $bytes[$pathEnd] -ne 0) { $pathEnd++ }
            $pdbPath = [System.Text.Encoding]::UTF8.GetString($bytes, $pathStart, $pathEnd - $pathStart)
            $codeView = [pscustomobject]@{
                guid = $guid
                age = $age
                pdbPath = $pdbPath
            }
        }
        elseif ($type -eq 19) {
            $nameStart = $dataOffset
            $nameEnd = $nameStart
            while ($nameEnd -lt ($dataOffset + $dataSize) -and $bytes[$nameEnd] -ne 0) { $nameEnd++ }
            if ($nameEnd -ge ($dataOffset + $dataSize)) { continue }
            $algorithm = [System.Text.Encoding]::ASCII.GetString($bytes, $nameStart, $nameEnd - $nameStart)
            $checksumStart = $nameEnd + 1
            $checksumLength = ($dataOffset + $dataSize) - $checksumStart
            if ($checksumLength -le 0) { continue }
            $checksumBytes = New-Object byte[] $checksumLength
            [Array]::Copy($bytes, $checksumStart, $checksumBytes, 0, $checksumLength)
            $pdbChecksum = [pscustomobject]@{
                algorithm = $algorithm
                hex = ([BitConverter]::ToString($checksumBytes)).Replace('-', '').ToLowerInvariant()
            }
        }
    }
    if ($null -eq $codeView) {
        throw "PE image has no CodeView debug entry: $DllPath"
    }
    return [pscustomobject]@{
        guid = $codeView.guid
        age = $codeView.age
        pdbPath = $codeView.pdbPath
        pdbChecksumAlgorithm = if ($pdbChecksum) { $pdbChecksum.algorithm } else { $null }
        pdbChecksum = if ($pdbChecksum) { $pdbChecksum.hex } else { $null }
    }
}

function Get-SymbolServerKey([string] $PdbGuid, [int] $Age, [string] $PdbFormat) {
    if ($PdbFormat -ceq 'portable-pdb') {
        return $PdbGuid.ToUpperInvariant() + 'ffffffff'
    }
    return $PdbGuid.ToUpperInvariant() + $Age.ToString('x')
}

function Get-ExpectedSymbolIdentities([string] $PackagePath, [string] $SymbolPath) {
    $packageName = [System.IO.Path]::GetFileNameWithoutExtension($PackagePath)
    $temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('dogwoodsharp-symbols-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $temporary | Out-Null
    try {
        $identities = @()
        $archive = [System.IO.Compression.ZipFile]::OpenRead($SymbolPath)
        try {
            foreach ($entry in @($archive.Entries)) {
                if ($entry.FullName -notmatch '^lib/(net[0-9.]+)/(.+\.pdb)$') { continue }
                $tfm = $Matches[1]
                $pdbName = $Matches[2]
                $pdbTarget = [System.IO.Path]::Combine($temporary, $entry.FullName.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
                $directory = Split-Path $pdbTarget -Parent
                New-Item -ItemType Directory -Force -Path $directory | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $pdbTarget, $true)
                $pdbIdentity = Get-PortablePdbIdentity $pdbTarget

                $dllEntryName = "lib/$tfm/$([System.IO.Path]::GetFileNameWithoutExtension($pdbName)).dll"
                $dllTarget = [System.IO.Path]::Combine($temporary, 'dll', $tfm, "$([System.IO.Path]::GetFileNameWithoutExtension($pdbName)).dll")
                $dllDirectory = Split-Path $dllTarget -Parent
                New-Item -ItemType Directory -Force -Path $dllDirectory | Out-Null
                $packageArchive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
                try {
                    $dllMatch = @($packageArchive.Entries | Where-Object FullName -CEQ $dllEntryName)
                    if ($dllMatch.Count -ne 1) {
                        throw "Package has no matching assembly entry: $dllEntryName"
                    }
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($dllMatch[0], $dllTarget, $true)
                }
                finally {
                    $packageArchive.Dispose()
                }
                $debug = Get-PeImageDebugIdentity $dllTarget
                if ($debug.guid -cne $pdbIdentity.guid) {
                    throw "PDB identity does not match the assembly CodeView identity: $dllEntryName"
                }
                if ([string] $debug.pdbChecksumAlgorithm -cne 'SHA256') {
                    throw "Assembly has no supported PDB checksum entry: $dllEntryName"
                }
                if ([string] $debug.pdbChecksum -cne $pdbIdentity.checksumSha256) {
                    throw "PDB content does not match the assembly PDB checksum: $dllEntryName"
                }
                $key = Get-SymbolServerKey $debug.guid $debug.age $pdbIdentity.format
                $identities += [pscustomobject]@{
                    tfm = $tfm
                    pdbName = $pdbName
                    pdbGuid = $pdbIdentity.guid
                    pdbStamp = $pdbIdentity.stamp
                    pdbAge = $debug.age
                    symbolKey = $key
                    pdbSha256 = $pdbIdentity.sha256
                    pdbChecksumSha256 = $pdbIdentity.checksumSha256
                    pdbSize = $pdbIdentity.size
                    pdbChecksumAlgorithm = $debug.pdbChecksumAlgorithm
                    assembly = $dllEntryName
                    packageName = $packageName
                }
            }
        }
        finally {
            $archive.Dispose()
        }
        if ($identities.Count -eq 0) {
            throw "Symbol package contains no supported PDB entries: $SymbolPath"
        }
        return @($identities | Sort-Object tfm)
    }
    finally {
        try { Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction Stop }
        catch { }
    }
}
