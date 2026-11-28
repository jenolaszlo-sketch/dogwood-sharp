[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $OutputPath,
    [string] $LogPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$RepositoryRoot) { $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')) }
if (!$OutputPath) { $OutputPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/github-journal-controls.json' }
if (!$LogPath) { $LogPath = Join-Path $RepositoryRoot 'docs/reviews/dogwood-sharp-pub-remed-01/evidence/github-journal-controls.log' }
$work = Join-Path $RepositoryRoot ('artifacts/publication-github-journal/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$script:controls = @()
$script:log = New-Object System.Text.StringBuilder

function Add-Log([string] $Text) {
    [void] $script:log.AppendLine($Text)
    Write-Host $Text
}

function Write-JsonFile([string] $Path, $Object) {
    $dir = Split-Path $Path -Parent
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, (($Object | ConvertTo-Json -Depth 12) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

function Add-Control([string] $Name, [bool] $Passed, [string] $Detail, $Evidence) {
    $evidencePath = $null
    if ($null -ne $Evidence) {
        $evidenceDirectory = Join-Path (Split-Path $OutputPath -Parent) 'github-journal-evidence'
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        $safeName = $Name -replace '[^A-Za-z0-9._-]', '-'
        $evidencePath = Join-Path $evidenceDirectory "$safeName.json"
        Write-JsonFile $evidencePath $Evidence
    }
    $script:controls += [ordered]@{ name = $Name; passed = $Passed; detail = $Detail; evidenceFile = $evidencePath }
    if ($Passed) { Add-Log "CONTROL PASS $Name" } else { Add-Log "CONTROL FAIL $Name :: $Detail" }
}

$driver = Join-Path $work 'github-journal-driver.ps1'
@'
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RepositoryRoot,
    [Parameter(Mandatory)] [string] $OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:GITHUB_TOKEN = 'fixture-token'
$script:refExists = $false
$script:fileContent = $null
$script:fileSha = $null
$script:putCount = 0
$script:postCount = 0
$script:contentsGets = 0
$script:simulateConcurrent = $false

function New-NotFound {
    $exception = New-Object System.Exception('Not Found')
    $exception | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = [System.Net.HttpStatusCode]::NotFound })
    return (New-Object System.Management.Automation.ErrorRecord($exception, 'NotFound', [System.Management.Automation.ErrorCategory]::ObjectNotFound, $null))
}

function Invoke-RestMethod {
    [CmdletBinding()]
    param(
        [string] $Method,
        [string] $Uri,
        [hashtable] $Headers,
        $Body,
        [string] $ContentType
    )
    if ($Uri -match '/git/ref/heads/') {
        if ($Method -eq 'GET') {
            if (!$script:refExists) { throw (New-NotFound) }
            return [pscustomobject]@{ ref = 'refs/heads/publication-state'; object = [pscustomobject]@{ sha = ('a' * 40) } }
        }
        throw 'Unexpected ref method.'
    }
    if ($Uri -match '/git/refs$') {
        if ($Method -ne 'POST') { throw 'Unexpected ref creation method.' }
        $parsed = $Body | ConvertFrom-Json
        if ([string] $parsed.sha -notmatch '^[0-9a-f]{40}$') { throw 'Invalid base sha.' }
        $script:refExists = $true
        $script:postCount++
        return [pscustomobject]@{ ref = [string] $parsed.ref }
    }
    if ($Uri -match '/contents/') {
        if ($Method -eq 'GET') {
            $script:contentsGets++
            if ($script:simulateConcurrent -and $script:contentsGets -eq 2) {
                $script:fileSha = 'sha-external'
            }
            if ($null -eq $script:fileContent) { throw (New-NotFound) }
            return [pscustomobject]@{ content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script:fileContent)); sha = $script:fileSha }
        }
        if ($Method -eq 'PUT') {
            $parsed = $Body | ConvertFrom-Json
            $suppliedSha = if ($null -ne $parsed.PSObject.Properties['sha']) { [string] $parsed.sha } else { $null }
            if ($suppliedSha -and $suppliedSha -cne [string] $script:fileSha) {
                $exception = New-Object System.Exception('Conflict')
                $exception | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = [System.Net.HttpStatusCode]::Conflict })
                throw $exception
            }
            $script:fileContent = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string] $parsed.content))
            $script:putCount++
            $script:fileSha = "sha-$($script:putCount)"
            return [pscustomobject]@{ commit = [pscustomobject]@{ sha = "commit-$($script:putCount)" } }
        }
    }
    throw "Unexpected mock API request: $Method $Uri"
}

. (Join-Path $RepositoryRoot 'scripts/publication/PublicationState.ps1')
$store = New-PublicationJournalStore -Repository 'fixture/repository' -Branch 'publication-state' -GitHubPath 'publication-state/journal.jsonl' -RefBaseSha ('b' * 40)
$binding = [pscustomobject]@{
    inventoryId = 'FIXTURE-INVENTORY'
    inventorySha256 = ('c' * 64)
    authorizationId = 'FIXTURE-AUTH'
    releaseCandidate = ('d' * 40)
}
$init = Add-PublicationJournalRecord -Store $store -Binding $binding -Event 'journal-initialized' -State 'binding-verified' -Detail @{ test = $true }
$operation = @{ order = '1'; kind = 'package'; id = 'Fixture'; version = '1.0.0'; file = 'Fixture.1.0.0.nupkg'; sha256 = ('e' * 64); size = '123' }
$attempt = Add-PublicationJournalRecord -Store $store -Binding $binding -Event 'attempt-initiated' -State 'publication-attempt-initiated' `
    -OperationOrder $operation.order -OperationKind $operation.kind -OperationId $operation.id -OperationVersion $operation.version `
    -OperationFile $operation.file -OperationSha256 $operation.sha256 -OperationSize $operation.size -AttemptId 'attempt-1' -Detail @{ test = $true }
$journal = Read-PublicationJournal $store
$result = [ordered]@{
    refCreated = $script:refExists
    postCount = $script:postCount
    putCount = $script:putCount
    valid = $journal.valid
    records = $journal.records.Count
    lastEvent = if ($journal.records.Count -gt 0) { [string] $journal.records[$journal.records.Count - 1].record.event } else { $null }
    bindingValid = (Test-PublicationJournalBinding $journal $binding).valid
    initHash = $init.hash
    attemptHash = $attempt.hash
    contentLength = $journal.content.Length
}
# Concurrent-write refusal: change the mock file sha between the write-ahead read and the commit.
$script:simulateConcurrent = $true
$script:contentsGets = 0
$concurrentRefused = $false
try {
    Add-PublicationJournalRecord -Store $store -Binding $binding -Event 'reconciliation-observed' -State 'published-and-verified-matching' -Detail @{ test = $true } | Out-Null
}
catch { $concurrentRefused = $true }
$result['concurrentWriteRefused'] = $concurrentRefused
$json = $result | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($OutputPath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
'@ | Set-Content -LiteralPath $driver -Encoding utf8

$driverOutput = Join-Path $work 'driver-result.json'
$stdout = Join-Path $work 'driver.stdout.txt'
$stderr = Join-Path $work 'driver.stderr.txt'
$process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $driver, '-RepositoryRoot', $RepositoryRoot, '-OutputPath', $driverOutput) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$stderrText = [string] (Get-Content -Raw $stderr)
if ($null -eq $stderrText) { $stderrText = '' }
$driverResult = if (Test-Path $driverOutput) { Get-Content -Raw $driverOutput | ConvertFrom-Json } else { $null }
Add-Control 'github-journal-write-ahead-and-readback' ($process.ExitCode -eq 0 -and $null -ne $driverResult -and $driverResult.refCreated -and $driverResult.putCount -eq 2 -and $driverResult.valid -and $driverResult.records -eq 2 -and $driverResult.lastEvent -eq 'attempt-initiated' -and $driverResult.bindingValid) "exit=$($process.ExitCode) result=$($driverResult | ConvertTo-Json -Compress)" ([ordered]@{ result = $driverResult; stderr = $stderrText.Trim() })
Add-Control 'github-journal-refuses-concurrent-write' ($null -ne $driverResult -and $driverResult.concurrentWriteRefused) "concurrentRefused=$($driverResult.concurrentWriteRefused)" ([ordered]@{ result = $driverResult })

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
Add-Log "GITHUB_JOURNAL_CONTROLS=$($resultObject.passed)/$($resultObject.total)"
if ($failed.Count -ne 0) { exit 1 }
exit 0
