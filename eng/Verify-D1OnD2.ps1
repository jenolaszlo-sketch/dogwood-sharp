#!/usr/bin/env pwsh
# Apply version-aware controls to a disposable copy of the historical D1 harness.
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [switch] $Valgrind
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

function Replace-Control([string] $Text, [string] $Original, [string] $Adapted) {
    if ([regex]::Matches($Text, [regex]::Escape($Original)).Count -ne 1) {
        throw "Expected exactly one historical control matching $Original"
    }
    return $Text.Replace($Original, $Adapted)
}

$root = Split-Path $PSScriptRoot -Parent
$historical = 'docs/reviews/d1-qualification/harness'
$output = 'artifacts/d2-r1-d1-regression'
# The original runner and project references depend on the directory depth.
$copy = Join-Path $root "$output/source/harness"
Push-Location $root
try {
    $tracked = @(git ls-files -- "$historical/")
    if ($tracked.Count -eq 0) { throw 'The preserved D1 harness is missing.' }
    foreach ($file in $tracked) {
        $destination = Join-Path $copy $file.Substring($historical.Length + 1)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file -Destination $destination -Force
    }

    $probe = Join-Path $copy 'native/abi_probe.c'
    $source = [IO.File]::ReadAllText($probe)
    $source = Replace-Control $source '    reject("reject_operation_two", ABI_VERSION, 2, "{}", 2, 3);' @'
    reject("reject_unsupported_operation_nine", ABI_VERSION, 9, "{}", 2, 3);
    reject("operation_two_requires_d2_request", ABI_VERSION, 2, "{}", 2, 5);
    reject("operation_two_rejects_empty_input", ABI_VERSION, 2, "", 0, 1);
    {
        const char request[] = "{\"source\":\"\",\"policySchema\":\"\",\"distincter\":\"compat\"}";
        dw_buffer buffer = {NULL, 0};
        uint32_t status = call(ABI_VERSION, 2, request, sizeof request - 1u, &buffer);
        check(status == 0 && body_contains(&buffer, "\"validationPassed\":true") &&
              body_contains(&buffer, "\"artifactsExported\":false") &&
              !body_contains(&buffer, "\"cedarPolicyText\""), "operation_two_validates_without_export");
        release(&buffer);
        check(slot_empty(&buffer), "operation_two_free_resets");
    }
'@
    $source = Replace-Control $source '    reject("reject_operation_precedes_input", ABI_VERSION, 2, "", 0, 3);' @'
    reject("reject_operation_precedes_input", ABI_VERSION, 9, "", 0, 3);
    reject("reject_unknown_operation_precedes_request_ceiling", ABI_VERSION, 9, NULL, REQUEST_LIMIT + 1u, 3);
'@
    [IO.File]::WriteAllText($probe, $source)

    $differential = Join-Path $copy 'differential.py'
    $source = [IO.File]::ReadAllText($differential)
    $source = Replace-Control $source '        ("wire-operation-2", 2, b''{}'', 3, None),' @'
        ("wire-operation-9", 9, b'{}', 3, None),
        ("wire-operation-2-envelope", 2, b'{}', 5, None),
'@
    [IO.File]::WriteAllText($differential, $source)

    $managed = Join-Path $copy 'managed/Program.cs'
    $source = [IO.File]::ReadAllText($managed)
    $source = Replace-Control $source '        Ensure(exported.SequenceEqual(expectedTypes),' @'
        var d2Types = new[]
        {
            "DogwoodSharp.DogwoodContextFieldCollision",
            "DogwoodSharp.DogwoodFailureCategory",
            "DogwoodSharp.DogwoodLoweredPolicySet",
            "DogwoodSharp.DogwoodSchema",
            "DogwoodSharp.DogwoodStage",
            "DogwoodSharp.DogwoodStageError",
            "DogwoodSharp.DogwoodValidationResult",
            "DogwoodSharp.PolicyRuleMapping",
            "DogwoodSharp.PolicyValidationCategory",
            "DogwoodSharp.PolicyValidationFinding",
        };
        Ensure(exported.SequenceEqual(expectedTypes.Concat(d2Types).OrderBy(name => name, StringComparer.Ordinal)),
'@
    $d1Members = 'new[] { "GetNativeVersion", "MaximumSourceBytes", "Parse" }'
    $d2Members = 'new[] { "GetNativeVersion", "Lower", "MaximumSourceBytes", "Parse", "Validate" }'
    $source = Replace-Control $source $d1Members $d2Members
    [IO.File]::WriteAllText($managed, $source)

    $options = @{
        RuntimeIdentifier = $RuntimeIdentifier
        AssetsRoot = Join-Path $root 'artifacts/native'
        OutputRoot = $output
        SkipNativeBuild = $true
        Valgrind = $Valgrind
    }
    & (Join-Path $copy 'run-all.ps1') @options
}
finally {
    Pop-Location
}
