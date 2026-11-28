[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WorkflowPath,
    [string] $RepositoryRoot,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$RepositoryRoot) { $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')) }
if (!$OutputPath) {
    $OutputPath = Join-Path ([System.IO.Path]::GetTempPath()) ('dogwoodsharp-input-safety-' + [Guid]::NewGuid().ToString('N') + '.json')
}

$failures = @()
$controls = @()

function Add-Control([string] $Name, [bool] $Passed, [string] $Detail) {
    $script:controls += [ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    }
    if (!$Passed) { $script:failures += "$Name`: $Detail" }
}

# 1. Static workflow controls.
$workflowText = [System.IO.File]::ReadAllText((Join-Path $RepositoryRoot $WorkflowPath))
$lines = $workflowText -split "`r?`n"
$insideRun = $false
$runIndent = 0
$runBlocks = @()
$currentRun = New-Object System.Collections.Generic.List[string]
foreach ($line in $lines) {
    if ($line -match '^(\s*)run:\s*[|>]\s*$') {
        $insideRun = $true
        $runIndent = $Matches[1].Length
        $currentRun = New-Object System.Collections.Generic.List[string]
        continue
    }
    if ($insideRun) {
        if ($line.Trim().Length -eq 0) { $currentRun.Add(''); continue }
        $indent = $line.Length - $line.TrimStart().Length
        if ($indent -le $runIndent) {
            $runBlocks += ($currentRun -join "`n")
            $insideRun = $false
        }
        else {
            $currentRun.Add($line)
        }
    }
}
if ($insideRun) { $runBlocks += ($currentRun -join "`n") }

$expressionRuns = @($runBlocks | Where-Object { $_ -match '\$\{\{' })
Add-Control 'no-github-expression-in-run-script' ($expressionRuns.Count -eq 0) ("run blocks containing expressions: " + $expressionRuns.Count)

$dynamicExecution = @($lines | Where-Object { $_ -match '(?i)\b(Invoke-Expression|iex|eval)\b' })
Add-Control 'no-dynamic-expression-evaluation' ($dynamicExecution.Count -eq 0) ("dynamic execution lines: " + $dynamicExecution.Count)

$skipDuplicate = @($lines | Where-Object { $_ -match '--skip-duplicate' })
Add-Control 'no-skip-duplicate' ($skipDuplicate.Count -eq 0) ("skip-duplicate lines: " + $skipDuplicate.Count)

$requiredBindings = @(
    'RELEASE_CANDIDATE: ${{ inputs.release_candidate }}',
    'SOURCE_RUN_ID: ${{ inputs.source_run_id }}',
    'SOURCE_RUN_ATTEMPT: ${{ inputs.source_run_attempt }}',
    'OWNER_AUTHORIZATION_ID: ${{ inputs.owner_authorization_id }}',
    'DRY_RUN: ${{ inputs.dry_run }}',
    'PUBLICATION_ENVIRONMENT: ${{ inputs.publication_environment }}',
    'DESTINATION: ${{ inputs.destination }}'
)
$missingBindings = @($requiredBindings | Where-Object { !$workflowText.Contains($_) })
Add-Control 'all-dispatch-inputs-bound-to-environment' ($missingBindings.Count -eq 0) ("missing bindings: " + ($missingBindings -join '; '))

# 2. Original independent expression reproduction against the baseline pattern.
$childPrefix = @('-NoProfile')
if ($env:OS -eq 'Windows_NT') { $childPrefix += @('-ExecutionPolicy', 'Bypass') }
$malicious = '$( [Console]::WriteLine("INPUT_INJECTION_REPRODUCED"); "58b0a3332391d85cdf19fd7b794d79516d2ecdfe")'
$baselineScript = '$releaseCandidate = "' + $malicious + '"' + "`n" + 'Write-Output ("BOUND=" + $releaseCandidate)'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('dogwoodsharp-input-safety-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    $baselinePath = Join-Path $work 'baseline-reproducer.ps1'
    [System.IO.File]::WriteAllText($baselinePath, $baselineScript + "`n", (New-Object System.Text.UTF8Encoding($false)))
    $stdoutPath = Join-Path $work 'baseline.stdout.txt'
    $stderrPath = Join-Path $work 'baseline.stderr.txt'
    $process = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList ($childPrefix + @('-File', $baselinePath)) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $baselineOutput = [string] (Get-Content -Raw $stdoutPath)
    Add-Control 'baseline-expression-execution-reproduced' ($baselineOutput -match 'INPUT_INJECTION_REPRODUCED') $baselineOutput.Trim()

    # 3. Successor validation rejects the same input and all adversarial variants.
    $inputScript = Join-Path $PSScriptRoot 'Test-PublicationInputs.ps1'
    $validCandidate = '58b0a3332391d85cdf19fd7b794d79516d2ecdfe'
    $variants = @(
        [ordered]@{ name = 'original-expression'; candidate = $malicious; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'command-substitution'; candidate = '$(Write-Output pwned)' + $validCandidate; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'backtick-substitution'; candidate = '`Write-Output pwned`' + $validCandidate; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'quote-breakout'; candidate = '" ; Write-Output pwned; #'; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'multiline'; candidate = $validCandidate + "`n" + 'Write-Output pwned'; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'uppercase-commit'; candidate = $validCandidate.ToUpperInvariant(); runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'short-commit'; candidate = '58b0a333'; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'non-hex-commit'; candidate = ('z' * 40); runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'run-id-expression'; candidate = $validCandidate; runId = '$(Write-Output pwned)'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'run-id-negative'; candidate = $validCandidate; runId = '-1'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'attempt-zero'; candidate = $validCandidate; runId = '46053558029'; attempt = '0'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'authorization-expression'; candidate = $validCandidate; runId = '46053558029'; attempt = '1'; auth = '$(Write-Output pwned)'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'environment-mismatch'; candidate = $validCandidate; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication-evil'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'destination-expression'; candidate = $validCandidate; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://evil.example/v3/index.json'; dryRun = 'true' },
        [ordered]@{ name = 'dry-run-expression'; candidate = $validCandidate; runId = '46053558029'; attempt = '1'; auth = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'; env = 'nuget-publication'; destination = 'https://api.nuget.org/v3/index.json'; dryRun = 'true; Write-Output pwned' }
    )

    $variantResults = @()
    foreach ($variant in $variants) {
        $saved = @{}
        foreach ($name in @('RELEASE_CANDIDATE', 'SOURCE_RUN_ID', 'SOURCE_RUN_ATTEMPT', 'OWNER_AUTHORIZATION_ID', 'DRY_RUN', 'PUBLICATION_ENVIRONMENT', 'DESTINATION')) {
            $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        }
        $env:RELEASE_CANDIDATE = $variant.candidate
        $env:SOURCE_RUN_ID = $variant.runId
        $env:SOURCE_RUN_ATTEMPT = $variant.attempt
        $env:OWNER_AUTHORIZATION_ID = $variant.auth
        $env:DRY_RUN = $variant.dryRun
        $env:PUBLICATION_ENVIRONMENT = $variant.env
        $env:DESTINATION = $variant.destination
        $out = Join-Path $work ("$($variant.name).stdout.txt")
        $err = Join-Path $work ("$($variant.name).stderr.txt")
        $child = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList ($childPrefix + @('-File', $inputScript)) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
        $combined = [string] (Get-Content -Raw $out) + [string] (Get-Content -Raw $err)
        foreach ($name in $saved.Keys) {
            if ($null -eq $saved[$name]) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
            else { Set-Item "Env:$name" $saved[$name] }
        }
        $refused = ($child.ExitCode -ne 0) -and ($combined -match 'PUBLICATION_INPUT_REFUSED')
        $inert = $combined -notmatch 'pwned|INPUT_INJECTION_REPRODUCED'
        $variantResults += [ordered]@{
            name = $variant.name
            exitCode = $child.ExitCode
            refused = $refused
            inert = $inert
            output = $combined.Trim()
        }
        Add-Control ("successor-rejects-$($variant.name)") ($refused -and $inert) ("exit=$($child.ExitCode) output=$($combined.Trim())")
    }

    # 4. Valid inputs remain accepted.
    $saved = @{}
    foreach ($name in @('RELEASE_CANDIDATE', 'SOURCE_RUN_ID', 'SOURCE_RUN_ATTEMPT', 'OWNER_AUTHORIZATION_ID', 'DRY_RUN', 'PUBLICATION_ENVIRONMENT', 'DESTINATION')) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    $env:RELEASE_CANDIDATE = $validCandidate
    $env:SOURCE_RUN_ID = '46053558029'
    $env:SOURCE_RUN_ATTEMPT = '1'
    $env:OWNER_AUTHORIZATION_ID = 'DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1'
    $env:DRY_RUN = 'true'
    $env:PUBLICATION_ENVIRONMENT = 'nuget-publication'
    $env:DESTINATION = 'https://api.nuget.org/v3/index.json'
    $out = Join-Path $work 'valid.stdout.txt'
    $err = Join-Path $work 'valid.stderr.txt'
    $child = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList ($childPrefix + @('-File', $inputScript)) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    $combined = [string] (Get-Content -Raw $out) + [string] (Get-Content -Raw $err)
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
        else { Set-Item "Env:$name" $saved[$name] }
    }
    Add-Control 'successor-accepts-valid-inputs' ($child.ExitCode -eq 0 -and $combined -match 'PUBLICATION_INPUTS=VALID') ("exit=$($child.ExitCode) output=$($combined.Trim())")
}
finally {
    try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop } catch { }
}

$result = [ordered]@{
    schemaVersion = 1
    workflowPath = $WorkflowPath
    staticControls = @($controls | Where-Object { $_.name -in @('no-github-expression-in-run-script', 'no-dynamic-expression-evaluation', 'no-skip-duplicate', 'all-dispatch-inputs-bound-to-environment') })
    executionControls = @($controls | Where-Object { $_.name -notin @('no-github-expression-in-run-script', 'no-dynamic-expression-evaluation', 'no-skip-duplicate', 'all-dispatch-inputs-bound-to-environment') })
    total = $controls.Count
    passed = @($controls | Where-Object { $_.passed }).Count
    failed = $failures.Count
    failures = $failures
    controls = $controls
}
$json = $result | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputPath), $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "WORKFLOW_INPUT_SAFETY=$($result.passed)/$($result.total)"
if ($failures.Count -ne 0) { exit 1 }
exit 0
