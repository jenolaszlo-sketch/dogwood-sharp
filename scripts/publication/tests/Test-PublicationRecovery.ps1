[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $OutputPath,
    [string] $LogPath,
    [switch] $Quick
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
if (!$OutputPath) { $OutputPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/recovery-controls.json' }
if (!$LogPath) { $LogPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/recovery-controls.log' }

$inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
$inventoryHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
$authorizationId = 'DOGWOOD-SHARP-PUB-REMED-01-TEST-AUTHORIZATION-1'
$workRoot = Join-Path $RepositoryRoot ('artifacts/publication-recovery/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

$script:log = New-Object System.Text.StringBuilder
function Add-Log([string] $Text) {
    [void] $script:log.AppendLine($Text)
    Write-Host $Text
}

function Write-JsonFile([string] $Path, $Object) {
    $dir = Split-Path $Path -Parent
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, (($Object | ConvertTo-Json -Depth 15) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

function Get-FreePort {
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint] $listener.LocalEndpoint).Port
    $listener.Stop()
    return $port
}

function Start-MockRegistry($Name, $PackageMode, $PackageDirectory, $PackageFile, $SymbolMode, $SymbolDirectory, [bool] $RequireChecksum = $false, $SymbolConflictFile = $null) {
    $dir = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $port = Get-FreePort
    $configPath = Join-Path $dir 'mock-config.json'
    $logPath = Join-Path $dir 'mock-requests.jsonl'
    $readyPath = Join-Path $dir 'mock-ready.txt'
    $config = [ordered]@{
        package = [ordered]@{
            mode = $PackageMode
            directory = $PackageDirectory
            file = $PackageFile
            sourceFile = $null
            delayCount = 2
        }
        symbols = [ordered]@{
            mode = $SymbolMode
            directory = $SymbolDirectory
            requireChecksum = $RequireChecksum
            conflictFile = $SymbolConflictFile
            delayCount = 2
        }
    }
    Write-JsonFile $configPath $config
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Mock-PublicationRegistry.ps1'), '-Port', ([string] $port), '-ConfigPath', $configPath, '-LogPath', $logPath, '-ReadyPath', $readyPath)
    $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -NoNewWindow -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath $readyPath)) {
        if ($process.HasExited) { throw "Mock registry process exited before becoming ready: $Name" }
        Start-Sleep -Milliseconds 100
    }
    if (!(Test-Path -LiteralPath $readyPath)) { throw "Mock registry did not become ready: $Name" }
    return [pscustomobject]@{
        name = $Name
        process = $process
        port = $port
        baseUrl = "http://127.0.0.1:$port"
        configPath = $configPath
        logPath = $logPath
        directory = $dir
    }
}

function Stop-MockRegistry($Mock) {
    if ($null -eq $Mock) { return }
    try { Stop-Process -Id $Mock.process.Id -Force -ErrorAction SilentlyContinue } catch { }
    try { $Mock.process.WaitForExit(5000) | Out-Null } catch { }
}

function Initialize-TestJournal([string] $JournalPath) {
    . (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
    if (Test-Path -LiteralPath $JournalPath) { Remove-Item -LiteralPath $JournalPath -Force }
    $store = New-PublicationJournalStore -Path $JournalPath
    $binding = [pscustomobject]@{
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        authorizationId = $authorizationId
        releaseCandidate = [string] $inventory.releaseCandidate.commit
    }
    Add-PublicationJournalRecord -Store $store -Binding $binding -Event 'journal-initialized' -State 'binding-verified' -Detail ([ordered]@{ test = $true }) | Out-Null
}

function Add-TestJournalRecord([string] $JournalPath, [string] $Order, [string] $Event, [string] $State, [string] $AttemptId, $Detail) {
    . (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
    $operation = @($inventory.packages | Where-Object { [int] $_.order -eq [int] $Order })[0]
    $store = New-PublicationJournalStore -Path $JournalPath
    $binding = [pscustomobject]@{
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        authorizationId = $authorizationId
        releaseCandidate = [string] $inventory.releaseCandidate.commit
    }
    Add-PublicationJournalRecord -Store $store -Binding $binding -Event $Event -State $State `
        -OperationOrder ([string] $operation.order) -OperationKind ([string] $operation.kind) `
        -OperationId ([string] $operation.id) -OperationVersion ([string] $operation.version) `
        -OperationFile ([string] $operation.file) -OperationSha256 ([string] $operation.sha256) `
        -OperationSize ([string] $operation.size) -AttemptId $AttemptId -Detail $Detail | Out-Null
}

function Seed-VerifiedOperation([string] $JournalPath, [int] $Order) {
    Add-TestJournalRecord $JournalPath ([string] $Order) 'attempt-initiated' 'publication-attempt-initiated' 'seed' ([ordered]@{ seed = $true })
    Add-TestJournalRecord $JournalPath ([string] $Order) 'upload-accepted' 'accepted-by-publication-endpoint-but-not-yet-visible' 'seed' ([ordered]@{ seed = $true })
    $operation = @($inventory.packages | Where-Object { [int] $_.order -eq $Order })[0]
    $event = if ($operation.kind -eq 'package') { 'published-verified' } else { 'symbols-verified' }
    $state = if ($operation.kind -eq 'package') { 'published-and-verified-matching' } else { 'symbols-available-and-verified' }
    Add-TestJournalRecord $JournalPath ([string] $Order) $event $state 'seed' ([ordered]@{ seed = $true })
}

function New-TestAuthorization([string] $Path) {
    $authorization = [ordered]@{
        authorizationId = $authorizationId
        scope = 'publication'
        dryRunAuthorized = $false
        publicationAuthorized = $true
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        releaseCandidate = [string] $inventory.releaseCandidate.commit
        sourceRunId = [int64] $inventory.source.runId
        environment = [string] $inventory.environment
        destination = [string] $inventory.destination
        packages = @($inventory.packages | ForEach-Object { [ordered]@{ file = [string] $_.file; sha256 = [string] $_.sha256 } })
        authorizedBy = 'remediation-test-fixture'
        authorizedAt = '2026-11-28'
        note = 'Local controlled-recovery test fixture. It is not an owner publication authorization and cannot authorize a real upload.'
    }
    Write-JsonFile $Path $authorization
}

function New-TestPlan([string] $Path) {
    $plan = [ordered]@{
        schemaVersion = 1
        status = 'verified'
        dryRun = $false
        credentialsRequested = $false
        uploadPerformed = $false
        inventoryId = [string] $inventory.inventoryId
        inventorySha256 = $inventoryHash
        authorizationId = $authorizationId
        releaseCandidate = [string] $inventory.releaseCandidate.commit
        releaseCandidateTree = [string] $inventory.releaseCandidate.tree
        destination = [string] $inventory.destination
        environment = [string] $inventory.environment
        packages = @($inventory.packages | ForEach-Object { [ordered]@{ order = [int] $_.order; kind = [string] $_.kind; id = [string] $_.id; version = [string] $_.version; file = [string] $_.file; size = [int64] $_.size; sha256 = [string] $_.sha256 } })
        publishOrder = @($inventory.publishOrder)
        rebuilt = $false
        repacked = $false
    }
    Write-JsonFile $Path $plan
}

function Invoke-Operation([string] $Name, [int] $Order, [string] $JournalPath, $Mock, [hashtable] $Environment, [int] $PollAttempts = 2, [int] $PollDelaySeconds = 0) {
    $dir = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $stdout = Join-Path $dir 'stdout.txt'
    $stderr = Join-Path $dir 'stderr.txt'
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $RepositoryRoot 'scripts/publication/Invoke-PublicationOperation.ps1'),
        '-Order', ([string] $Order),
        '-InventoryPath', $inventoryPath,
        '-ArtifactDirectory', $artifacts,
        '-AuthorizationPath', (Join-Path $workRoot 'authorization.json'),
        '-PlanPath', (Join-Path $workRoot 'plan.json'),
        '-ReleaseCandidate', [string] $inventory.releaseCandidate.commit,
        '-SourceRunId', [string] $inventory.source.runId,
        '-SourceRunAttempt', [string] $inventory.source.runAttempt,
        '-Environment', [string] $inventory.environment,
        '-Destination', [string] $inventory.destination,
        '-OwnerAuthorizationId', $authorizationId,
        '-JournalPath', $JournalPath,
        '-FlatContainerBaseUrl', "$($Mock.baseUrl)/flatcontainer",
        '-SymbolServerBaseUrl', "$($Mock.baseUrl)/symbols",
        '-SignatureVerification', 'None',
        '-PollAttempts', ([string] $PollAttempts),
        '-PollDelaySeconds', ([string] $PollDelaySeconds),
        '-DotnetPath', (Join-Path $workRoot 'dotnet-stub.cmd'),
        '-ReportDirectory', (Join-Path $dir 'reconcile'),
        '-SkipPlanChecks'
    )
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, [string] $Environment[$key], 'Process')
    }
    try {
        $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    }
    finally {
        foreach ($key in $saved.Keys) {
            [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process')
        }
    }
    $stdoutText = if (Test-Path $stdout) { [string] (Get-Content -Raw $stdout) } else { '' }
    $stderrText = if (Test-Path $stderr) { [string] (Get-Content -Raw $stderr) } else { '' }
    if ($null -eq $stdoutText) { $stdoutText = '' }
    if ($null -eq $stderrText) { $stderrText = '' }
    return [pscustomobject]@{
        name = $Name
        order = $Order
        exitCode = $process.ExitCode
        stdout = $stdoutText.Trim()
        stderr = $stderrText.Trim()
    }
}

function Get-JournalSummary([string] $JournalPath) {
    . (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
    $store = New-PublicationJournalStore -Path $JournalPath
    $journal = Read-PublicationJournal $store
    $operations = @()
    foreach ($operation in @($inventory.packages)) {
        $state = Get-PublicationOperationJournalState $journal $operation.file
        $operations += [ordered]@{
            order = [int] $operation.order
            file = [string] $operation.file
            state = $state.state
            event = $state.event
            attemptPending = $state.attemptPending
            verified = $state.verified
            conflict = $state.conflict
            manual = $state.manual
        }
    }
    return [pscustomobject]@{
        valid = $journal.valid
        error = $journal.error
        records = $journal.records.Count
        lastEvent = if ($journal.records.Count -gt 0) { [string] $journal.records[$journal.records.Count - 1].record.event } else { $null }
        operations = $operations
    }
}

function Get-TransportLines([string] $TransportLog) {
    $lines = New-Object System.Collections.ArrayList
    if (Test-Path -LiteralPath $TransportLog) {
        foreach ($line in Get-Content -LiteralPath $TransportLog) {
            if ($line.Trim().Length -gt 0) { [void] $lines.Add([string] $line) }
        }
    }
    return $lines
}

function Copy-PackageToFeed([string] $Feed, [string] $File) {
    New-Item -ItemType Directory -Force -Path $Feed | Out-Null
    Copy-Item -LiteralPath (Join-Path $artifacts $File) -Destination (Join-Path $Feed $File) -Force
}

# ---------------------------------------------------------------------------
# Shared fixture setup.
# ---------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path (Join-Path $workRoot 'feed'), (Join-Path $workRoot 'symbols'), (Join-Path $workRoot 'symbol-stage') | Out-Null
New-TestAuthorization (Join-Path $workRoot 'authorization.json')
New-TestPlan (Join-Path $workRoot 'plan.json')

. (Join-Path $RepositoryRoot 'scripts/publication/PublicationArchive.ps1')
$symbolIdentityMap = @{}
foreach ($symbol in @($inventory.packages | Where-Object { $_.kind -eq 'symbols' })) {
    $package = @($inventory.packages | Where-Object { $_.kind -eq 'package' -and $_.id -ceq $symbol.id })[0]
    $identities = @(Get-ExpectedSymbolIdentities (Join-Path $artifacts $package.file) (Join-Path $artifacts $symbol.file))
    $symbolIdentityMap[$symbol.file] = $identities
    foreach ($identity in $identities) {
        $bytes = Get-ArchiveEntryBytes (Join-Path $artifacts $symbol.file) "lib/$($identity.tfm)/$($identity.pdbName)"
        $target = Join-Path $workRoot "symbol-stage/$($symbol.id)/$($identity.pdbName)/$($identity.symbolKey)/$($identity.pdbName)"
        $targetDir = Split-Path $target -Parent
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        [System.IO.File]::WriteAllBytes($target, $bytes)
    }
}

$transportStub = @'
@echo off
setlocal
echo %*>>"%DOGWOOD_TRANSPORT_LOG%"
if not "%DOGWOOD_TRANSPORT_MARKER%"=="" echo started>"%DOGWOOD_TRANSPORT_MARKER%"
if not "%DOGWOOD_TRANSPORT_STAGE%"=="" (
  if not exist "%DOGWOOD_TRANSPORT_STAGE%" mkdir "%DOGWOOD_TRANSPORT_STAGE%"
  copy /Y "%~3" "%DOGWOOD_TRANSPORT_STAGE%\" >nul
)
if not "%DOGWOOD_SYMBOL_STAGE_SOURCE%"=="" (
  if not exist "%DOGWOOD_SYMBOL_STAGE_TARGET%" mkdir "%DOGWOOD_SYMBOL_STAGE_TARGET%"
  xcopy /E /I /Y "%DOGWOOD_SYMBOL_STAGE_SOURCE%\*" "%DOGWOOD_SYMBOL_STAGE_TARGET%\" >nul
)
if not "%DOGWOOD_TRANSPORT_SLEEP%"=="" ping -n %DOGWOOD_TRANSPORT_SLEEP% 127.0.0.1 >nul
if not "%DOGWOOD_TRANSPORT_EXIT%"=="" exit /b %DOGWOOD_TRANSPORT_EXIT%
exit /b 0
'@
[System.IO.File]::WriteAllText((Join-Path $workRoot 'dotnet-stub.cmd'), $transportStub + "`r`n", (New-Object System.Text.UTF8Encoding($false)))

$script:controls = @()
function Add-Result([string] $Name, [bool] $Passed, [string] $Detail, $Extra) {
    $evidencePath = $null
    if ($null -ne $Extra) {
        $evidenceDirectory = Join-Path (Split-Path $OutputPath -Parent) 'recovery-evidence'
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        $safeName = $Name -replace '[^A-Za-z0-9._-]', '-'
        $evidencePath = Join-Path $evidenceDirectory "$safeName.json"
        Write-JsonFile $evidencePath $Extra
    }
    $script:controls += [ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
        evidenceFile = $evidencePath
    }
    if ($Passed) { Add-Log "CONTROL PASS $Name" }
    else { Add-Log "CONTROL FAIL $Name :: $Detail" }
}

# ---------------------------------------------------------------------------
# C1: genuine first publication and integrated four-operation campaign.
# ---------------------------------------------------------------------------
$campaign = Join-Path $workRoot 'campaign'
New-Item -ItemType Directory -Force -Path $campaign | Out-Null
$campaignFeed = Join-Path $campaign 'feed'
$campaignSymbols = Join-Path $campaign 'symbols'
New-Item -ItemType Directory -Force -Path $campaignFeed, $campaignSymbols | Out-Null
$campaignJournal = Join-Path $campaign 'journal.jsonl'
Initialize-TestJournal $campaignJournal
$mock = $null
try {
    $mock = Start-MockRegistry -Name 'campaign-mock' -PackageMode 'file' -PackageDirectory $campaignFeed -SymbolMode 'file' -SymbolDirectory $campaignSymbols -RequireChecksum $true
    $transportLog = Join-Path $campaign 'transport.log'
    $commonEnv = @{
        DOGWOOD_TRANSPORT_LOG = $transportLog
        DOGWOOD_TRANSPORT_STAGE = $campaignFeed
        DOGWOOD_SYMBOL_STAGE_TARGET = $campaignSymbols
    }
    $results = @()
    for ($order = 1; $order -le 4; $order++) {
        $operationEnv = $commonEnv.Clone()
        if ($order -eq 2) { $operationEnv['DOGWOOD_SYMBOL_STAGE_SOURCE'] = (Join-Path $workRoot 'symbol-stage/DogwoodSharp') }
        if ($order -eq 4) { $operationEnv['DOGWOOD_SYMBOL_STAGE_SOURCE'] = (Join-Path $workRoot 'symbol-stage/DogwoodSharp.CedarSharp') }
        $operationResult = Invoke-Operation -Name "campaign-op$order" -Order $order -JournalPath $campaignJournal -Mock $mock -Environment $operationEnv -PollAttempts 2 -PollDelaySeconds 0
        $results += $operationResult
        if ($operationResult.exitCode -ne 0) { break }
    }
    $summary = Get-JournalSummary $campaignJournal
    $transport = @(Get-TransportLines $transportLog)
    $allZero = @($results | Where-Object { $_.exitCode -ne 0 }).Count -eq 0
    $allVerified = @($summary.operations | Where-Object { !$_.verified }).Count -eq 0
    $expectedFiles = @(
        'DogwoodSharp.0.1.0-preview.1.nupkg',
        'DogwoodSharp.0.1.0-preview.1.snupkg',
        'DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg',
        'DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg'
    )
    $transportOk = $transport.Count -eq 4
    foreach ($index in 0..3) {
        if ($index -ge $transport.Count) { $transportOk = $false; continue }
        if ($transport[$index] -notmatch [regex]::Escape($expectedFiles[$index])) { $transportOk = $false }
    }
    $noSymbolsOk = $transport.Count -eq 4 -and
        ($transport[0] -match '--no-symbols') -and
        ($transport[2] -match '--no-symbols') -and
        ($transport[1] -notmatch '--no-symbols') -and
        ($transport[3] -notmatch '--no-symbols')
    $noSkipDuplicate = @($transport | Where-Object { $_ -match '--skip-duplicate' }).Count -eq 0
    Add-Result 'genuine-first-publication-four-operations' ($allZero -and $allVerified -and $transportOk -and $noSymbolsOk -and $noSkipDuplicate) "exit=$(@($results | ForEach-Object { $_.exitCode }) -join ',') verified=$allVerified transport=$($transport.Count)" ([ordered]@{ results = $results; journal = $summary; transport = $transport })

    # C2: resume after a previously completed publication does not upload again.
    $resume = Invoke-Operation -Name 'campaign-op1-resume' -Order 1 -JournalPath $campaignJournal -Mock $mock -Environment $commonEnv -PollAttempts 1 -PollDelaySeconds 0
    $transportAfterResume = @(Get-TransportLines $transportLog)
    Add-Result 'resume-after-completed-publication' ($resume.exitCode -eq 0 -and $transportAfterResume.Count -eq 4 -and $resume.stdout -match 'ALREADY_VERIFIED') "exit=$($resume.exitCode) transport=$($transportAfterResume.Count)" ([ordered]@{ result = $resume })
}
finally {
    Stop-MockRegistry $mock
}

# ---------------------------------------------------------------------------
# C3: delayed indexing, response lost and interrupted upload.
# ---------------------------------------------------------------------------
$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'delayed'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    $mock = Start-MockRegistry -Name 'delayed-mock' -PackageMode 'delay404' -PackageDirectory $feed -SymbolMode 'file' -SymbolDirectory $symbols -RequireChecksum $false
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $envMap = @{ DOGWOOD_TRANSPORT_LOG = $transportLog; DOGWOOD_TRANSPORT_STAGE = $feed }
    $delayed = Invoke-Operation -Name 'delayed-op1' -Order 1 -JournalPath $journal -Mock $mock -Environment $envMap -PollAttempts 4 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    Add-Result 'delayed-indexing-becomes-visible' ($delayed.exitCode -eq 0 -and $summary.operations[0].verified) "exit=$($delayed.exitCode) state=$($summary.operations[0].state)" ([ordered]@{ result = $delayed; journal = $summary })
}
finally {
    Stop-MockRegistry $mock
}

$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'response-lost'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    $mock = Start-MockRegistry -Name 'response-lost-mock' -PackageMode 'file' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $envMap = @{ DOGWOOD_TRANSPORT_LOG = $transportLog; DOGWOOD_TRANSPORT_STAGE = $feed; DOGWOOD_TRANSPORT_EXIT = '42' }
    $lost = Invoke-Operation -Name 'response-lost-op1' -Order 1 -JournalPath $journal -Mock $mock -Environment $envMap -PollAttempts 3 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    $events = @(Get-JournalSummary $journal)
    Add-Result 'upload-accepted-response-lost' ($lost.exitCode -eq 0 -and $summary.operations[0].verified) "exit=$($lost.exitCode) state=$($summary.operations[0].state) last=$($summary.lastEvent)" ([ordered]@{ result = $lost; journal = $summary })
}
finally {
    Stop-MockRegistry $mock
}

$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'interrupted'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    $mock = Start-MockRegistry -Name 'interrupted-mock' -PackageMode 'missing' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $envMap = @{ DOGWOOD_TRANSPORT_LOG = $transportLog; DOGWOOD_TRANSPORT_EXIT = '42' }
    $interrupted = Invoke-Operation -Name 'interrupted-op1' -Order 1 -JournalPath $journal -Mock $mock -Environment $envMap -PollAttempts 2 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    $rerun = Invoke-Operation -Name 'interrupted-op1-rerun' -Order 1 -JournalPath $journal -Mock $mock -Environment $envMap -PollAttempts 2 -PollDelaySeconds 0
    $transportAfter = @(Get-TransportLines $transportLog)
    Add-Result 'upload-interrupted-before-response-no-auto-retry' ($interrupted.exitCode -eq 2 -and $rerun.exitCode -eq 2 -and $transportAfter.Count -eq 1 -and $summary.operations[0].state -eq 'publication-outcome-unknown') "first=$($interrupted.exitCode) rerun=$($rerun.exitCode) transport=$($transportAfter.Count) state=$($summary.operations[0].state)" ([ordered]@{ first = $interrupted; rerun = $rerun; journal = $summary })
}
finally {
    Stop-MockRegistry $mock
}

# ---------------------------------------------------------------------------
# C4: runner termination during publication, durable attempt survives.
# ---------------------------------------------------------------------------
$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'runner-killed'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    $mock = Start-MockRegistry -Name 'runner-killed-mock' -PackageMode 'file' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $marker = Join-Path $scenarioDir 'upload-started.txt'
    $envMap = @{ DOGWOOD_TRANSPORT_LOG = $transportLog; DOGWOOD_TRANSPORT_STAGE = $feed; DOGWOOD_TRANSPORT_MARKER = $marker; DOGWOOD_TRANSPORT_SLEEP = '60' }
    $saved = @{}
    foreach ($key in $envMap.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, [string] $envMap[$key], 'Process') }
    try {
        $arguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $RepositoryRoot 'scripts/publication/Invoke-PublicationOperation.ps1'),
            '-Order', '1', '-InventoryPath', $inventoryPath, '-ArtifactDirectory', $artifacts,
            '-AuthorizationPath', (Join-Path $workRoot 'authorization.json'), '-PlanPath', (Join-Path $workRoot 'plan.json'),
            '-ReleaseCandidate', [string] $inventory.releaseCandidate.commit,
            '-SourceRunId', [string] $inventory.source.runId, '-SourceRunAttempt', [string] $inventory.source.runAttempt,
            '-Environment', [string] $inventory.environment, '-Destination', [string] $inventory.destination,
            '-OwnerAuthorizationId', $authorizationId, '-JournalPath', $journal,
            '-FlatContainerBaseUrl', "$($mock.baseUrl)/flatcontainer", '-SymbolServerBaseUrl', "$($mock.baseUrl)/symbols",
            '-SignatureVerification', 'None', '-PollAttempts', '2', '-PollDelaySeconds', '0',
            '-DotnetPath', (Join-Path $workRoot 'dotnet-stub.cmd'),
            '-ReportDirectory', (Join-Path $scenarioDir 'reconcile-killed'), '-SkipPlanChecks'
        )
        $stdout = Join-Path $scenarioDir 'killed.stdout.txt'
        $stderr = Join-Path $scenarioDir 'killed.stderr.txt'
        $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while ([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath $marker)) {
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
        $markerObserved = Test-Path -LiteralPath $marker
        if (!$process.HasExited) { & taskkill.exe /F /T /PID $process.Id 2>&1 | Out-Null }
        $process.WaitForExit(10000) | Out-Null
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
    }
    $afterKill = Get-JournalSummary $journal
    $transportBeforeResume = @(Get-TransportLines $transportLog)
    $resumeEnv = @{ DOGWOOD_TRANSPORT_LOG = $transportLog }
    $resume = Invoke-Operation -Name 'runner-killed-resume' -Order 1 -JournalPath $journal -Mock $mock -Environment $resumeEnv -PollAttempts 2 -PollDelaySeconds 0
    $afterResume = Get-JournalSummary $journal
    $transportAfterResume = @(Get-TransportLines $transportLog)
    Add-Result 'runner-terminated-during-publication' ($markerObserved -and $afterKill.valid -and $afterKill.operations[0].state -eq 'publication-attempt-initiated' -and $resume.exitCode -eq 0 -and $afterResume.operations[0].verified -and $transportAfterResume.Count -eq $transportBeforeResume.Count) "marker=$markerObserved killedState=$($afterKill.operations[0].state) resume=$($resume.exitCode) transport=$($transportBeforeResume.Count)->$($transportAfterResume.Count)" ([ordered]@{ marker = $markerObserved; afterKill = $afterKill; resume = $resume; afterResume = $afterResume })
}
finally {
    Stop-MockRegistry $mock
}

# ---------------------------------------------------------------------------
# C5: 404 after an ambiguous attempt is not actionable.
# ---------------------------------------------------------------------------
$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'ambiguous-404'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    Add-TestJournalRecord $journal '1' 'attempt-initiated' 'publication-attempt-initiated' 'prior-attempt' ([ordered]@{ test = 'ambiguous prior attempt' })
    $mock = Start-MockRegistry -Name 'ambiguous-404-mock' -PackageMode 'file' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $envMap = @{ DOGWOOD_TRANSPORT_LOG = $transportLog }
    $ambiguous = Invoke-Operation -Name 'ambiguous-404-op1' -Order 1 -JournalPath $journal -Mock $mock -Environment $envMap -PollAttempts 3 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    $transport = @(Get-TransportLines $transportLog)
    Add-Result 'http-404-after-ambiguous-attempt-holds' ($ambiguous.exitCode -eq 2 -and $transport.Count -eq 0 -and $summary.operations[0].state -eq 'publication-outcome-unknown') "exit=$($ambiguous.exitCode) transport=$($transport.Count) state=$($summary.operations[0].state)" ([ordered]@{ result = $ambiguous; journal = $summary })

    # Matching becomes visible later; still no upload.
    Copy-PackageToFeed $feed 'DogwoodSharp.0.1.0-preview.1.nupkg'
    $visible = Invoke-Operation -Name 'ambiguous-404-visible' -Order 1 -JournalPath $journal -Mock $mock -Environment $envMap -PollAttempts 2 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    $transportAfter = @(Get-TransportLines $transportLog)
    Add-Result 'matching-package-becomes-visible-during-reconciliation' ($visible.exitCode -eq 0 -and $summary.operations[0].verified -and $transportAfter.Count -eq 0) "exit=$($visible.exitCode) state=$($summary.operations[0].state) transport=$($transportAfter.Count)" ([ordered]@{ result = $visible; journal = $summary })

    # Conflicting package becomes visible.
    $conflictFeed = Join-Path $scenarioDir 'conflict-feed'
    New-Item -ItemType Directory -Force -Path $conflictFeed | Out-Null
    Copy-Item -LiteralPath (Join-Path $artifacts 'DogwoodSharp.0.1.0-preview.1.nupkg') -Destination (Join-Path $conflictFeed 'DogwoodSharp.0.1.0-preview.1.nupkg') -Force
    $zip = [System.IO.Compression.ZipFile]::Open((Join-Path $conflictFeed 'DogwoodSharp.0.1.0-preview.1.nupkg'), [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $zip.CreateEntry('qualification-conflict.txt')
        $writer = New-Object System.IO.StreamWriter($entry.Open())
        try { $writer.Write('conflict') } finally { $writer.Dispose() }
    }
    finally { $zip.Dispose() }
    $conflictJournal = Join-Path $scenarioDir 'conflict-journal.jsonl'
    Initialize-TestJournal $conflictJournal
    Add-TestJournalRecord $conflictJournal '1' 'attempt-initiated' 'publication-attempt-initiated' 'prior-attempt' ([ordered]@{ test = 'conflicting prior attempt' })
    $mock2 = Start-MockRegistry -Name 'conflict-mock' -PackageMode 'file' -PackageDirectory $conflictFeed -SymbolMode 'missing' -SymbolDirectory $symbols
    try {
        $conflict = Invoke-Operation -Name 'ambiguous-conflict' -Order 1 -JournalPath $conflictJournal -Mock $mock2 -Environment $envMap -PollAttempts 1 -PollDelaySeconds 0
        $conflictSummary = Get-JournalSummary $conflictJournal
        Add-Result 'conflicting-package-becomes-visible' ($conflict.exitCode -eq 1 -and $conflictSummary.operations[0].conflict -and @(Get-TransportLines $transportLog).Count -eq 0) "exit=$($conflict.exitCode) state=$($conflictSummary.operations[0].state)" ([ordered]@{ result = $conflict; journal = $conflictSummary })
    }
    finally { Stop-MockRegistry $mock2 }
}
finally {
    Stop-MockRegistry $mock
}

# ---------------------------------------------------------------------------
# C6: HTTP 429, HTTP 5xx and transport failure.
# ---------------------------------------------------------------------------
foreach ($mode in @('status429', 'status503', 'reset')) {
    $mock = $null
    try {
        $scenarioDir = Join-Path $workRoot "transport-$mode"
        New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
        $feed = Join-Path $scenarioDir 'feed'
        $symbols = Join-Path $scenarioDir 'symbols'
        New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
        $journal = Join-Path $scenarioDir 'journal.jsonl'
        Initialize-TestJournal $journal
        $mock = Start-MockRegistry -Name "transport-$mode-mock" -PackageMode $mode -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
        $transportLog = Join-Path $scenarioDir 'transport.log'
        $result = Invoke-Operation -Name "transport-$mode" -Order 1 -JournalPath $journal -Mock $mock -Environment @{ DOGWOOD_TRANSPORT_LOG = $transportLog } -PollAttempts 1 -PollDelaySeconds 0
        $summary = Get-JournalSummary $journal
        $transport = @(Get-TransportLines $transportLog)
        Add-Result "registry-$mode-is-not-absence" ($result.exitCode -eq 2 -and $transport.Count -eq 0 -and $summary.operations[0].state -eq 'registry-unavailable') "exit=$($result.exitCode) transport=$($transport.Count) state=$($summary.operations[0].state)" ([ordered]@{ result = $result; journal = $summary })
    }
    finally { Stop-MockRegistry $mock }
}

# ---------------------------------------------------------------------------
# C7: inconsistent durable journal refuses.
# ---------------------------------------------------------------------------
$scenarioDir = Join-Path $workRoot 'bad-journal'
New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
$feed = Join-Path $scenarioDir 'feed'
$symbols = Join-Path $scenarioDir 'symbols'
New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
$journal = Join-Path $scenarioDir 'journal.jsonl'
Initialize-TestJournal $journal
$line = ([System.IO.File]::ReadAllText($journal)).TrimEnd("`n")
$parts = $line.Split("`t")
$decoded = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parts[2]))
$tampered = $decoded.Replace('"inventoryId":"DOGWOOD-SHARP-PUB-PREP-01-INVENTORY-1"', '"inventoryId":"TAMPERED"')
$parts[2] = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($tampered))
[System.IO.File]::WriteAllText($journal, ($parts -join "`t") + "`n")
$mock = $null
try {
    $mock = Start-MockRegistry -Name 'bad-journal-mock' -PackageMode 'missing' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $bad = Invoke-Operation -Name 'bad-journal-op1' -Order 1 -JournalPath $journal -Mock $mock -Environment @{ DOGWOOD_TRANSPORT_LOG = $transportLog } -PollAttempts 1 -PollDelaySeconds 0
    Add-Result 'inconsistent-journal-refuses' ($bad.exitCode -ne 0 -and @(Get-TransportLines $transportLog).Count -eq 0) "exit=$($bad.exitCode)" ([ordered]@{ result = $bad })
}
finally { Stop-MockRegistry $mock }

# ---------------------------------------------------------------------------
# C8: symbol recovery states.
# ---------------------------------------------------------------------------
function New-SymbolScenario([string] $Name, [string] $SymbolMode, [switch] $SeedAccepted, [switch] $SeedAttempt, [string] $ConflictFile = $null) {
    $scenarioDir = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    Copy-PackageToFeed $feed 'DogwoodSharp.0.1.0-preview.1.nupkg'
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    # Seed operation 1 as publicly verified so operation 2 can run.
    . (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
    $stablePackage = @($inventory.packages | Where-Object { [int] $_.order -eq 1 })[0]
    $store = New-PublicationJournalStore -Path $journal
    $binding = [pscustomobject]@{ inventoryId = [string] $inventory.inventoryId; inventorySha256 = $inventoryHash; authorizationId = $authorizationId; releaseCandidate = [string] $inventory.releaseCandidate.commit }
    foreach ($event in @(
        @{ event = 'attempt-initiated'; state = 'publication-attempt-initiated' },
        @{ event = 'upload-accepted'; state = 'accepted-by-publication-endpoint-but-not-yet-visible' },
        @{ event = 'published-verified'; state = 'published-and-verified-matching' }
    )) {
        Add-PublicationJournalRecord -Store $store -Binding $binding -Event $event.event -State $event.state `
            -OperationOrder '1' -OperationKind 'package' -OperationId ([string] $stablePackage.id) -OperationVersion ([string] $stablePackage.version) `
            -OperationFile ([string] $stablePackage.file) -OperationSha256 ([string] $stablePackage.sha256) -OperationSize ([string] $stablePackage.size) `
            -AttemptId 'seed' -Detail ([ordered]@{ seed = $true }) | Out-Null
    }
    if ($SeedAccepted) { Add-TestJournalRecord $journal '2' 'upload-accepted' 'accepted-by-publication-endpoint-but-not-yet-visible' 'seed' ([ordered]@{ seed = $true }) }
    if ($SeedAttempt) { Add-TestJournalRecord $journal '2' 'attempt-initiated' 'publication-attempt-initiated' 'seed' ([ordered]@{ seed = $true }) }
    $mock = Start-MockRegistry -Name "$Name-mock" -PackageMode 'file' -PackageDirectory $feed -SymbolMode $SymbolMode -SymbolDirectory $symbols -RequireChecksum $true -SymbolConflictFile $ConflictFile
    return [pscustomobject]@{ dir = $scenarioDir; journal = $journal; mock = $mock; transportLog = (Join-Path $scenarioDir 'transport.log') }
}

$symbolScenarios = @(
    @{ name = 'symbols-accepted'; mode = 'missing'; accepted = $true; attempt = $false; expectedState = 'symbol-indexing-pending'; expectedExit = 2 },
    @{ name = 'symbols-outcome-unknown'; mode = 'missing'; accepted = $false; attempt = $true; expectedState = 'symbol-upload-outcome-unknown'; expectedExit = 2 }
)
foreach ($scenario in $symbolScenarios) {
    $fixture = $null
    try {
        $fixture = New-SymbolScenario -Name $scenario.name -SymbolMode $scenario.mode -SeedAccepted:$scenario.accepted -SeedAttempt:$scenario.attempt
        $result = Invoke-Operation -Name "$($scenario.name)-op2" -Order 2 -JournalPath $fixture.journal -Mock $fixture.mock -Environment @{ DOGWOOD_TRANSPORT_LOG = $fixture.transportLog } -PollAttempts 2 -PollDelaySeconds 0
        $summary = Get-JournalSummary $fixture.journal
        Add-Result $scenario.name ($result.exitCode -eq $scenario.expectedExit -and $summary.operations[1].state -eq $scenario.expectedState -and @(Get-TransportLines $fixture.transportLog).Count -eq 0) "exit=$($result.exitCode) state=$($summary.operations[1].state)" ([ordered]@{ result = $result; journal = $summary })
    }
    finally { if ($fixture) { Stop-MockRegistry $fixture.mock } }
}

# Symbols available and verified.
$fixture = $null
try {
    $fixture = New-SymbolScenario -Name 'symbols-available' -SymbolMode 'file'
    Copy-Item -Path (Join-Path $workRoot 'symbol-stage/DogwoodSharp/*') -Destination (Join-Path $fixture.dir 'symbols') -Recurse -Force
    $result = Invoke-Operation -Name 'symbols-available-op2' -Order 2 -JournalPath $fixture.journal -Mock $fixture.mock -Environment @{ DOGWOOD_TRANSPORT_LOG = $fixture.transportLog } -PollAttempts 2 -PollDelaySeconds 0
    $summary = Get-JournalSummary $fixture.journal
    Add-Result 'symbols-available-and-verified' ($result.exitCode -eq 0 -and $summary.operations[1].verified -and @(Get-TransportLines $fixture.transportLog).Count -eq 0) "exit=$($result.exitCode) state=$($summary.operations[1].state)" ([ordered]@{ result = $result; journal = $summary })
}
finally { if ($fixture) { Stop-MockRegistry $fixture.mock } }

# Symbol content conflict.
$fixture = $null
try {
    $fixture = New-SymbolScenario -Name 'symbols-conflict' -SymbolMode 'conflict'
    Copy-Item -Path (Join-Path $workRoot 'symbol-stage/DogwoodSharp/*') -Destination (Join-Path $fixture.dir 'symbols') -Recurse -Force
    $result = Invoke-Operation -Name 'symbols-conflict-op2' -Order 2 -JournalPath $fixture.journal -Mock $fixture.mock -Environment @{ DOGWOOD_TRANSPORT_LOG = $fixture.transportLog } -PollAttempts 1 -PollDelaySeconds 0
    $summary = Get-JournalSummary $fixture.journal
    Add-Result 'symbols-inconsistent-conflict' ($result.exitCode -eq 1 -and $summary.operations[1].conflict -and @(Get-TransportLines $fixture.transportLog).Count -eq 0) "exit=$($result.exitCode) state=$($summary.operations[1].state)" ([ordered]@{ result = $result; journal = $summary })
}
finally { if ($fixture) { Stop-MockRegistry $fixture.mock } }

# Genuine first symbol publication: upload accepted and indexed by the stub.
$fixture = $null
try {
    $fixture = New-SymbolScenario -Name 'symbols-first-publication' -SymbolMode 'file'
    $targetSymbols = Join-Path $fixture.dir 'symbols'
    $result = Invoke-Operation -Name 'symbols-first-publication-op2' -Order 2 -JournalPath $fixture.journal -Mock $fixture.mock -Environment @{
        DOGWOOD_TRANSPORT_LOG = $fixture.transportLog
        DOGWOOD_SYMBOL_STAGE_SOURCE = (Join-Path $workRoot 'symbol-stage/DogwoodSharp')
        DOGWOOD_SYMBOL_STAGE_TARGET = $targetSymbols
    } -PollAttempts 2 -PollDelaySeconds 0
    $summary = Get-JournalSummary $fixture.journal
    $transport = @(Get-TransportLines $fixture.transportLog)
    Add-Result 'symbol-first-publication-verified' ($result.exitCode -eq 0 -and $summary.operations[1].verified -and $transport.Count -eq 1 -and $transport[0] -match 'DogwoodSharp\.0\.1\.0-preview\.1\.snupkg' -and $transport[0] -notmatch '--no-symbols') "exit=$($result.exitCode) state=$($summary.operations[1].state) transport=$($transport.Count)" ([ordered]@{ result = $result; journal = $summary; transport = $transport })
}
finally { if ($fixture) { Stop-MockRegistry $fixture.mock } }

# ---------------------------------------------------------------------------
# C9: dependent adapter refuses publication before core package is verified.
# ---------------------------------------------------------------------------
$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'dependency-guard'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    $mock = Start-MockRegistry -Name 'dependency-guard-mock' -PackageMode 'missing' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $result = Invoke-Operation -Name 'dependency-guard-op3' -Order 3 -JournalPath $journal -Mock $mock -Environment @{ DOGWOOD_TRANSPORT_LOG = (Join-Path $scenarioDir 'transport.log') } -PollAttempts 1 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    Add-Result 'adapter-refused-before-core-verified' ($result.exitCode -ne 0 -and @(Get-TransportLines (Join-Path $scenarioDir 'transport.log')).Count -eq 0) "exit=$($result.exitCode)" ([ordered]@{ result = $result; journal = $summary })
}
finally { Stop-MockRegistry $mock }

# ---------------------------------------------------------------------------
# C10: interruption after operation 3 (adapter package).
# ---------------------------------------------------------------------------
$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'op3-response-lost'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    Copy-PackageToFeed $feed 'DogwoodSharp.0.1.0-preview.1.nupkg'
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    Seed-VerifiedOperation $journal 1
    $mock = Start-MockRegistry -Name 'op3-response-lost-mock' -PackageMode 'file' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $result = Invoke-Operation -Name 'op3-response-lost' -Order 3 -JournalPath $journal -Mock $mock -Environment @{ DOGWOOD_TRANSPORT_LOG = $transportLog; DOGWOOD_TRANSPORT_STAGE = $feed; DOGWOOD_TRANSPORT_EXIT = '42' } -PollAttempts 3 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    $transport = @(Get-TransportLines $transportLog)
    Add-Result 'operation-3-response-lost-recovers' ($result.exitCode -eq 0 -and $summary.operations[2].verified -and $transport.Count -eq 1) "exit=$($result.exitCode) state=$($summary.operations[2].state) transport=$($transport.Count)" ([ordered]@{ result = $result; journal = $summary })
}
finally { Stop-MockRegistry $mock }

# ---------------------------------------------------------------------------
# C11: interruption after operation 4 (adapter symbols).
# ---------------------------------------------------------------------------
$mock = $null
try {
    $scenarioDir = Join-Path $workRoot 'op4-symbol-pending'
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    $feed = Join-Path $scenarioDir 'feed'
    $symbols = Join-Path $scenarioDir 'symbols'
    New-Item -ItemType Directory -Force -Path $feed, $symbols | Out-Null
    Copy-PackageToFeed $feed 'DogwoodSharp.0.1.0-preview.1.nupkg'
    Copy-PackageToFeed $feed 'DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg'
    $journal = Join-Path $scenarioDir 'journal.jsonl'
    Initialize-TestJournal $journal
    Seed-VerifiedOperation $journal 1
    Seed-VerifiedOperation $journal 3
    Add-TestJournalRecord $journal '4' 'upload-accepted' 'accepted-by-publication-endpoint-but-not-yet-visible' 'seed' ([ordered]@{ seed = $true })
    $mock = Start-MockRegistry -Name 'op4-symbol-pending-mock' -PackageMode 'file' -PackageDirectory $feed -SymbolMode 'missing' -SymbolDirectory $symbols
    $transportLog = Join-Path $scenarioDir 'transport.log'
    $result = Invoke-Operation -Name 'op4-symbol-pending' -Order 4 -JournalPath $journal -Mock $mock -Environment @{ DOGWOOD_TRANSPORT_LOG = $transportLog } -PollAttempts 2 -PollDelaySeconds 0
    $summary = Get-JournalSummary $journal
    Add-Result 'operation-4-symbol-indexing-pending' ($result.exitCode -eq 2 -and $summary.operations[3].state -eq 'symbol-indexing-pending' -and @(Get-TransportLines $transportLog).Count -eq 0) "exit=$($result.exitCode) state=$($summary.operations[3].state)" ([ordered]@{ result = $result; journal = $summary })
}
finally { Stop-MockRegistry $mock }

# ---------------------------------------------------------------------------
# Summary.
# ---------------------------------------------------------------------------
$failed = @($script:controls | Where-Object { !$_.passed })
$resultObject = [ordered]@{
    schemaVersion = 1
    commission = 'DOGWOOD-SHARP-PUB-REMED-01'
    executedAtUtc = [DateTime]::UtcNow.ToString('o')
    inventorySha256 = $inventoryHash
    total = $script:controls.Count
    passed = @($script:controls | Where-Object { $_.passed }).Count
    failed = $failed.Count
    controls = $script:controls
}
Write-JsonFile $OutputPath $resultObject
$logDir = Split-Path $LogPath -Parent
if ($logDir) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
[System.IO.File]::WriteAllText($LogPath, $script:log.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Add-Log "RECOVERY_CONTROLS=$($resultObject.passed)/$($resultObject.total)"
if ($failed.Count -ne 0) { exit 1 }
exit 0



