[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $VerifiedPlan,
    [Parameter(Mandatory)] [string] $LivePlan
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Refuse([string] $Message) {
    [Console]::Error.WriteLine("PUBLICATION_REFUSED: $Message")
    exit 1
}

function Read-Plan([string] $Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        Refuse("Publication plan is missing: $Path")
    }
    try {
        return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    }
    catch {
        Refuse("Publication plan is not valid JSON: $Path")
    }
}

$verified = Read-Plan $VerifiedPlan
$live = Read-Plan $LivePlan

foreach ($field in @(
    'inventorySha256',
    'authorizationId',
    'releaseCandidate',
    'releaseCandidateTree',
    'sourceRepository',
    'sourceRunId',
    'sourceRunAttempt',
    'sourceWorkflowPath',
    'sourceArtifactName',
    'sourceArtifactId',
    'sourceArtifactDigest',
    'destination',
    'environment'
)) {
    if ([string] $verified.$field -cne [string] $live.$field) {
        Refuse("Verified and live publication plans differ on $field.")
    }
}

$verifiedPackages = @($verified.packages | ForEach-Object { "$($_.file):$($_.sha256):$($_.order)" } | Sort-Object -CaseSensitive)
$livePackages = @($live.packages | ForEach-Object { "$($_.file):$($_.sha256):$($_.order)" } | Sort-Object -CaseSensitive)
if (($verifiedPackages -join "`n") -cne ($livePackages -join "`n")) {
    Refuse('Verified and live publication plans differ on the package inventory.')
}
if ((@($verified.publishOrder) -join ',') -cne (@($live.publishOrder) -join ',')) {
    Refuse('Verified and live publication plans differ on the publish order.')
}
if (($live.dryRun -ne $false) -or ($live.credentialsRequested -ne $false) -or ($live.uploadPerformed -ne $false)) {
    Refuse('Live publication plan does not have the expected pre-publication state.')
}
if (($live.rebuilt -ne $false) -or ($live.repacked -ne $false)) {
    Refuse('Live publication plan indicates a rebuild or repack.')
}

Write-Host 'PUBLICATION_PLAN_CROSSCHECK=VERIFIED'
exit 0
