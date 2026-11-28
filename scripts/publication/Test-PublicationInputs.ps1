[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Refuse([string] $Field, [string] $Reason) {
    [Console]::Error.WriteLine("PUBLICATION_INPUT_REFUSED: $Field $Reason")
    exit 1
}

function Get-RequiredInput([string] $Name) {
    $value = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if ($null -eq $value) { Refuse $Name 'is missing.' }
    return [string] $value
}

function Assert-SingleLine([string] $Name, [string] $Value) {
    if ($Value.Length -eq 0) { Refuse $Name 'is empty.' }
    if ($Value.Length -gt 512) { Refuse $Name 'is too long.' }
    if ($Value -match "[`r`n`t]") { Refuse $Name 'contains a line break or tab.' }
    foreach ($character in $Value.ToCharArray()) {
        if ([int] $character -lt 32 -or [int] $character -eq 127) {
            Refuse $Name 'contains a control character.'
        }
    }
}

$releaseCandidate = Get-RequiredInput 'RELEASE_CANDIDATE'
Assert-SingleLine 'RELEASE_CANDIDATE' $releaseCandidate
if ($releaseCandidate -cnotmatch '^[0-9a-f]{40}$') {
    Refuse 'RELEASE_CANDIDATE' 'is not a lowercase 40-character commit SHA.'
}

$sourceRunId = Get-RequiredInput 'SOURCE_RUN_ID'
Assert-SingleLine 'SOURCE_RUN_ID' $sourceRunId
if ($sourceRunId -cnotmatch '^[1-9][0-9]{0,19}$') {
    Refuse 'SOURCE_RUN_ID' 'is not a positive decimal run identifier.'
}

$sourceRunAttempt = Get-RequiredInput 'SOURCE_RUN_ATTEMPT'
Assert-SingleLine 'SOURCE_RUN_ATTEMPT' $sourceRunAttempt
if ($sourceRunAttempt -cnotmatch '^[1-9][0-9]{0,5}$') {
    Refuse 'SOURCE_RUN_ATTEMPT' 'is not a positive decimal attempt identifier.'
}

$ownerAuthorizationId = Get-RequiredInput 'OWNER_AUTHORIZATION_ID'
Assert-SingleLine 'OWNER_AUTHORIZATION_ID' $ownerAuthorizationId
if ($ownerAuthorizationId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
    Refuse 'OWNER_AUTHORIZATION_ID' 'is not an allowed authorization identifier.'
}

$dryRun = Get-RequiredInput 'DRY_RUN'
Assert-SingleLine 'DRY_RUN' $dryRun
if ($dryRun -cnotin @('true', 'false')) {
    Refuse 'DRY_RUN' 'is not exactly true or false.'
}

$environment = Get-RequiredInput 'PUBLICATION_ENVIRONMENT'
Assert-SingleLine 'PUBLICATION_ENVIRONMENT' $environment
if ($environment -cne 'nuget-publication') {
    Refuse 'PUBLICATION_ENVIRONMENT' 'is not the approved environment.'
}

$destination = Get-RequiredInput 'DESTINATION'
Assert-SingleLine 'DESTINATION' $destination
if ($destination -cne 'https://api.nuget.org/v3/index.json') {
    Refuse 'DESTINATION' 'is not the approved NuGet v3 destination.'
}

if ($env:GITHUB_ENV) {
    $lines = @(
        "VALIDATED_RELEASE_CANDIDATE=$releaseCandidate",
        "VALIDATED_SOURCE_RUN_ID=$sourceRunId",
        "VALIDATED_SOURCE_RUN_ATTEMPT=$sourceRunAttempt",
        "VALIDATED_OWNER_AUTHORIZATION_ID=$ownerAuthorizationId",
        "VALIDATED_DRY_RUN=$dryRun",
        "VALIDATED_PUBLICATION_ENVIRONMENT=$environment",
        "VALIDATED_DESTINATION=$destination"
    )
    [System.IO.File]::AppendAllText($env:GITHUB_ENV, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host 'PUBLICATION_INPUTS=VALID'
Write-Host "RELEASE_CANDIDATE=$releaseCandidate"
Write-Host "SOURCE_RUN_ID=$sourceRunId"
Write-Host "SOURCE_RUN_ATTEMPT=$sourceRunAttempt"
Write-Host "DRY_RUN=$dryRun"
exit 0
