$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    cargo run --locked --manifest-path research/interop-spike/Cargo.toml > research/spike-output.json
    if ($LASTEXITCODE -ne 0) { throw 'Upstream spike failed.' }
    dotnet restore research/CedarSharp.Spike --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Spike package restore failed.' }
    foreach ($framework in @('net8.0', 'net10.0')) {
        dotnet run --project research/CedarSharp.Spike -c Release -f $framework --no-restore -- research/spike-output.json
        if ($LASTEXITCODE -ne 0) { throw "CedarSharp comparison failed: $framework" }
    }
} finally { Pop-Location }
