# Durable publication journal and conservative publication state model.
# The journal is append-only and hash-chained. Every line is
#   <record-sha256>\t<previous-record-sha256>\t<base64(canonical-record-json)>
# so that a partially written, reordered or edited record is detectable.

Set-StrictMode -Version Latest

$script:PublicationJournalSchemaVersion = 1
$script:PublicationState = [ordered]@{
    BindingVerified = 'binding-verified'
    NotAttemptedAbsent = 'not-previously-attempted-and-absent'
    AttemptInitiated = 'publication-attempt-initiated'
    AttemptAborted = 'publication-attempt-aborted-before-upload'
    AcceptedNotVisible = 'accepted-by-publication-endpoint-but-not-yet-visible'
    OutcomeUnknown = 'publication-outcome-unknown'
    PublishedMatching = 'published-and-verified-matching'
    PublishedConflicting = 'published-and-conflicting'
    RegistryUnavailable = 'registry-unavailable'
    ManualReconciliation = 'manual-reconciliation-required'
    SymbolUploadNotAttempted = 'symbol-upload-not-attempted'
    SymbolUploadAccepted = 'symbol-upload-accepted'
    SymbolUploadOutcomeUnknown = 'symbol-upload-outcome-unknown'
    SymbolIndexingPending = 'symbol-indexing-pending'
    SymbolsVerified = 'symbols-available-and-verified'
    SymbolsConflicting = 'symbols-inconsistent-or-conflicting'
    SymbolManualReconciliation = 'symbol-state-requires-manual-reconciliation'
}

function Get-ErrorHttpStatus($ErrorRecord) {
    if ($null -eq $ErrorRecord -or $null -eq $ErrorRecord.Exception) { return 0 }
    $exception = $ErrorRecord.Exception
    while ($null -ne $exception) {
        if ($null -ne $exception.PSObject.Properties['Response']) {
            $response = $exception.Response
            if ($null -ne $response) {
                try {
                    if ($null -ne $response.PSObject.Properties['StatusCode']) {
                        return [int] $response.StatusCode
                    }
                }
                catch { }
                try { return [int] $response.StatusCode.value__ } catch { }
            }
        }
        $exception = $exception.InnerException
    }
    return 0
}

function Get-Sha256Text([string] $Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-Base64Text([string] $Text) {
    return [Convert]::ToBase64String((New-Object System.Text.UTF8Encoding($false)).GetBytes($Text))
}

function Get-TextFromBase64([string] $Base64) {
    return (New-Object System.Text.UTF8Encoding($false, $true)).GetString([Convert]::FromBase64String($Base64))
}

function New-PublicationJournalStore {
    param(
        [string] $Path,
        [string] $Repository,
        [string] $Branch,
        [string] $GitHubPath,
        [string] $RefBaseSha
    )
    if ($Path) {
        return [pscustomobject]@{
            kind = 'local'
            path = [System.IO.Path]::GetFullPath($Path)
            repository = $null
            branch = $null
            githubPath = $null
            refBaseSha = $null
        }
    }
    if (!$Repository -or !$Branch -or !$GitHubPath) {
        throw 'PUBLICATION_JOURNAL_REFUSED: no durable journal store was configured.'
    }
    if ([string] $GitHubPath -match '(^|/)\.\.(/|$)' -or $GitHubPath.StartsWith('/')) {
        throw 'PUBLICATION_JOURNAL_REFUSED: journal path is unsafe.'
    }
    return [pscustomobject]@{
        kind = 'github'
        path = $null
        repository = $Repository
        branch = $Branch
        githubPath = $GitHubPath
        refBaseSha = $RefBaseSha
    }
}

function Get-GitHubApiHeaders {
    if (!$env:GITHUB_TOKEN) {
        throw 'PUBLICATION_JOURNAL_REFUSED: GITHUB_TOKEN is unavailable for durable journal access.'
    }
    return @{
        Authorization = "Bearer $env:GITHUB_TOKEN"
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'dogwoodsharp-publication-journal'
    }
}

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [Parameter(Mandatory)] [string] $Uri,
        $Body
    )
    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = (Get-GitHubApiHeaders)
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        $parameters['Body'] = ($Body | ConvertTo-Json -Compress -Depth 20)
        $parameters['ContentType'] = 'application/json'
    }
    return Invoke-RestMethod @parameters
}

function Get-GitHubJournalFile($Store) {
    $segments = @($Store.githubPath.Split('/') | ForEach-Object { [System.Uri]::EscapeDataString($_) })
    $encodedPath = $segments -join '/'
    $uri = "https://api.github.com/repos/$($Store.repository)/contents/${encodedPath}?ref=$([System.Uri]::EscapeDataString($Store.branch))"
    try {
        $response = Invoke-GitHubApi -Method 'GET' -Uri $uri
        $content = Get-TextFromBase64 ([string] $response.content).Replace("`n", '').Replace("`r", '')
        return [pscustomobject]@{ exists = $true; content = $content; sha = [string] $response.sha }
    }
    catch {
        $status = Get-ErrorHttpStatus $_
        if ($status -ne 404) {
            throw "PUBLICATION_JOURNAL_REFUSED: durable journal read failed with HTTP $status."
        }
        return [pscustomobject]@{ exists = $false; content = ''; sha = $null }
    }
}

function Initialize-GitHubJournalBranch($Store) {
    $uri = "https://api.github.com/repos/$($Store.repository)/git/ref/heads/$([System.Uri]::EscapeDataString($Store.branch))"
    try {
        Invoke-GitHubApi -Method 'GET' -Uri $uri | Out-Null
        return
    }
    catch {
        $status = Get-ErrorHttpStatus $_
        if ($status -ne 404) {
            throw "PUBLICATION_JOURNAL_REFUSED: durable journal ref read failed with HTTP $status."
        }
    }
    if (!$Store.refBaseSha -or [string] $Store.refBaseSha -notmatch '^[0-9a-f]{40}$') {
        throw 'PUBLICATION_JOURNAL_REFUSED: the durable journal branch does not exist and no valid base revision was supplied.'
    }
    $body = @{
        ref = "refs/heads/$($Store.branch)"
        sha = [string] $Store.refBaseSha
    }
    try {
        Invoke-GitHubApi -Method 'POST' -Uri "https://api.github.com/repos/$($Store.repository)/git/refs" -Body $body | Out-Null
    }
    catch {
        $status = Get-ErrorHttpStatus $_
        if ($status -eq 422) {
            # A concurrent creator already made the ref. Re-read it and fail closed if absent.
            Invoke-GitHubApi -Method 'GET' -Uri $uri | Out-Null
            return
        }
        throw "PUBLICATION_JOURNAL_REFUSED: durable journal ref creation failed with HTTP $status."
    }
}

function Read-PublicationJournalFile($Store) {
    if ($Store.kind -eq 'local') {
        if (!(Test-Path -LiteralPath $Store.path -PathType Leaf)) {
            return [pscustomobject]@{ exists = $false; content = ''; sha = $null }
        }
        return [pscustomobject]@{
            exists = $true
            content = [System.IO.File]::ReadAllText($Store.path)
            sha = $null
        }
    }
    return Get-GitHubJournalFile $Store
}

function ConvertFrom-PublicationJournalLines([string] $Content) {
    $records = @()
    $previousHash = $null
    $lineNumber = 0
    foreach ($line in ($Content -split "`n")) {
        $lineNumber++
        $trimmed = $line.TrimEnd("`r")
        if ($trimmed.Length -eq 0) { continue }
        $parts = $trimmed.Split("`t")
        if ($parts.Count -ne 3) {
            return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber is malformed"; records = @(); lastHash = $null }
        }
        $expectedHash = $parts[0]
        $declaredPrevious = $parts[1]
        $recordJson = $null
        try { $recordJson = Get-TextFromBase64 $parts[2] }
        catch { return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber is not valid base64"; records = @(); lastHash = $null } }
        $actualHash = Get-Sha256Text $recordJson
        if ($actualHash -cne $expectedHash) {
            return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber hash does not match its content"; records = @(); lastHash = $null }
        }
        if ($declaredPrevious -cne '') {
            if ($declaredPrevious -cne $previousHash) {
                return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber does not chain to the previous record"; records = @(); lastHash = $null }
            }
        }
        elseif ($previousHash -ne $null) {
            return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber breaks the hash chain"; records = @(); lastHash = $null }
        }
        try { $record = $recordJson | ConvertFrom-Json }
        catch { return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber is not valid JSON"; records = @(); lastHash = $null } }
        if ([int] $record.schemaVersion -ne $script:PublicationJournalSchemaVersion) {
            return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber has an unsupported schema version"; records = @(); lastHash = $null }
        }
        if ([int] $record.sequence -ne ($records.Count + 1)) {
            return [pscustomobject]@{ valid = $false; error = "journal line $lineNumber has an inconsistent sequence number"; records = @(); lastHash = $null }
        }
        $records += [pscustomobject]@{
            hash = $expectedHash
            sequence = [int] $record.sequence
            record = $record
        }
        $previousHash = $expectedHash
    }
    return [pscustomobject]@{ valid = $true; error = $null; records = $records; lastHash = $previousHash }
}

function Read-PublicationJournal($Store) {
    $file = Read-PublicationJournalFile $Store
    $parsed = ConvertFrom-PublicationJournalLines ([string] $file.content)
    return [pscustomobject]@{
        exists = $file.exists
        content = [string] $file.content
        sha = $file.sha
        valid = $parsed.valid
        error = $parsed.error
        records = @($parsed.records)
        lastHash = $parsed.lastHash
    }
}

function Test-PublicationJournalBinding($Journal, $Binding) {
    if (!$Journal.exists) { return [pscustomobject]@{ valid = $false; error = 'journal does not exist' } }
    if (!$Journal.valid) { return [pscustomobject]@{ valid = $false; error = $Journal.error } }
    if ($Journal.records.Count -eq 0) { return [pscustomobject]@{ valid = $false; error = 'journal has no initialization record' } }
    $initial = $Journal.records[0].record
    if ([string] $initial.event -cne 'journal-initialized') {
        return [pscustomobject]@{ valid = $false; error = 'journal does not start with its binding record' }
    }
    foreach ($entry in $Journal.records) {
        $record = $entry.record
        if ([string] $record.inventoryId -cne [string] $Binding.inventoryId -or
            [string] $record.inventorySha256 -cne [string] $Binding.inventorySha256 -or
            [string] $record.authorizationId -cne [string] $Binding.authorizationId -or
            [string] $record.releaseCandidate -cne [string] $Binding.releaseCandidate) {
            return [pscustomobject]@{ valid = $false; error = "journal sequence $($entry.sequence) is not bound to the authorized inventory" }
        }
    }
    return [pscustomobject]@{ valid = $true; error = $null }
}

function Add-PublicationJournalRecord {
    param(
        [Parameter(Mandatory)] $Store,
        [Parameter(Mandatory)] $Binding,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $State,
        [string] $OperationOrder,
        [string] $OperationKind,
        [string] $OperationId,
        [string] $OperationVersion,
        [string] $OperationFile,
        [string] $OperationSha256,
        [string] $OperationSize,
        [string] $AttemptId,
        $Detail
    )
    $journal = Read-PublicationJournal $Store
    if ($journal.exists) {
        $bindingCheck = Test-PublicationJournalBinding $journal $Binding
        if (!$bindingCheck.valid) {
            throw "PUBLICATION_JOURNAL_REFUSED: $($bindingCheck.error)"
        }
    }
    elseif ($Event -cne 'journal-initialized') {
        throw 'PUBLICATION_JOURNAL_REFUSED: the journal has no binding record.'
    }
    if ($journal.records.Count -gt 0 -and $Event -ceq 'journal-initialized') {
        throw 'PUBLICATION_JOURNAL_REFUSED: the journal is already initialized.'
    }
    if (($journal.content.Length) -gt 524288) {
        throw 'PUBLICATION_JOURNAL_REFUSED: the journal exceeds the supported size.'
    }

    $record = [ordered]@{
        schemaVersion = $script:PublicationJournalSchemaVersion
        sequence = ($journal.records.Count + 1)
        recordedAtUtc = [DateTime]::UtcNow.ToString('o')
        event = $Event
        state = $State
        inventoryId = [string] $Binding.inventoryId
        inventorySha256 = [string] $Binding.inventorySha256
        authorizationId = [string] $Binding.authorizationId
        releaseCandidate = [string] $Binding.releaseCandidate
        operationOrder = $OperationOrder
        operationKind = $OperationKind
        operationId = $OperationId
        operationVersion = $OperationVersion
        operationFile = $OperationFile
        operationSha256 = $OperationSha256
        operationSize = $OperationSize
        attemptId = $AttemptId
        detail = $Detail
    }
    $recordJson = $record | ConvertTo-Json -Compress -Depth 20
    $recordHash = Get-Sha256Text $recordJson
    $previous = if ($journal.lastHash) { [string] $journal.lastHash } else { '' }
    $line = "$recordHash`t$previous`t$(Get-Base64Text $recordJson)`n"

    if ($Store.kind -eq 'local') {
        $directory = Split-Path $Store.path -Parent
        if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
        $stream = New-Object System.IO.FileStream($Store.path, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($line)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }
        $verification = Read-PublicationJournal $Store
        if (!$verification.valid -or $verification.lastHash -cne $recordHash) {
            throw 'PUBLICATION_JOURNAL_REFUSED: the durable journal write could not be verified.'
        }
    }
    else {
        Initialize-GitHubJournalBranch $Store
        $current = Get-GitHubJournalFile $Store
        if ([string] $current.sha -cne [string] $journal.sha) {
            throw 'PUBLICATION_JOURNAL_REFUSED: the durable journal changed concurrently.'
        }
        $newContent = [string] $current.content + $line
        $segments = @($Store.githubPath.Split('/') | ForEach-Object { [System.Uri]::EscapeDataString($_) })
        $encodedPath = $segments -join '/'
        $body = @{
            message = "publication-state: $Event $OperationFile sequence $($record.sequence)"
            content = [Convert]::ToBase64String((New-Object System.Text.UTF8Encoding($false)).GetBytes($newContent))
            branch = [string] $Store.branch
        }
        if ($current.sha) { $body['sha'] = [string] $current.sha }
        try {
            Invoke-GitHubApi -Method 'PUT' -Uri "https://api.github.com/repos/$($Store.repository)/contents/$encodedPath" -Body $body | Out-Null
        }
        catch {
            $status = Get-ErrorHttpStatus $_
            throw "PUBLICATION_JOURNAL_REFUSED: durable journal commit failed with HTTP $status. $($_.Exception.Message)"
        }
        $verification = Read-PublicationJournal $Store
        if (!$verification.valid -or $verification.lastHash -cne $recordHash) {
            throw 'PUBLICATION_JOURNAL_REFUSED: the durable journal commit could not be read back with the expected hash.'
        }
    }
    return [pscustomobject]@{ hash = $recordHash; record = $record }
}

function Get-PublicationOperationJournalState($Journal, [string] $OperationFile) {
    $operationRecords = @($Journal.records | Where-Object { [string] $_.record.operationFile -ceq $OperationFile })
    if ($operationRecords.Count -eq 0) {
        return [pscustomobject]@{
            seen = $false
            state = $null
            event = $null
            hasAttempt = $false
            attemptPending = $false
            verified = $false
            accepted = $false
            aborted = $false
            conflict = $false
            manual = $false
            lastRecord = $null
        }
    }
    $last = $operationRecords[$operationRecords.Count - 1].record
    $terminalRecords = @($operationRecords | Where-Object { [string] $_.record.event -cne 'reconciliation-observed' })
    $lastTerminal = if ($terminalRecords.Count -gt 0) { $terminalRecords[$terminalRecords.Count - 1].record } else { $last }
    $hasAttempt = @($operationRecords | Where-Object { [string] $_.record.event -ceq 'attempt-initiated' }).Count -gt 0
    $accepted = @($operationRecords | Where-Object { [string] $_.record.event -ceq 'upload-accepted' }).Count -gt 0
    $unknown = @($operationRecords | Where-Object { [string] $_.record.event -ceq 'upload-outcome-unknown' }).Count -gt 0
    $aborted = @($operationRecords | Where-Object { [string] $_.record.event -ceq 'attempt-aborted-before-upload' }).Count -gt 0
    $verified = [string] $lastTerminal.event -in @('published-verified', 'symbols-verified')
    $conflict = @($operationRecords | Where-Object { [string] $_.record.event -in @('conflict-detected', 'symbols-conflicting') }).Count -gt 0
    $manual = @($operationRecords | Where-Object { [string] $_.record.event -ceq 'manual-reconciliation-required' }).Count -gt 0
    $attemptPending = $hasAttempt -and !$accepted -and !$unknown -and !$aborted
    return [pscustomobject]@{
        seen = $true
        state = [string] $last.state
        event = [string] $last.event
        hasAttempt = $hasAttempt
        attemptPending = $attemptPending
        verified = $verified
        accepted = $accepted
        aborted = $aborted
        conflict = $conflict
        manual = $manual
        lastRecord = $last
        records = $operationRecords
    }
}

function Assert-NoPendingPublicationAttempt($Journal, [string] $OperationFile) {
    $state = Get-PublicationOperationJournalState $Journal $OperationFile
    if ($state.attemptPending -or ($state.hasAttempt -and !$state.aborted -and !$state.verified -and !$state.conflict -and !$state.manual)) {
        throw "PUBLICATION_STATE_REFUSED: a prior publication attempt exists for $OperationFile and its outcome is not conclusively closed. Owner reconciliation is required."
    }
}
