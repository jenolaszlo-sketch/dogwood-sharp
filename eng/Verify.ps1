$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet restore DogwoodSharp.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build DogwoodSharp.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet format DogwoodSharp.slnx --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Formatting failed.' }
    foreach ($framework in @('net8.0', 'net10.0')) {
        dotnet test tests/DogwoodSharp.Tests -c Release -f $framework --no-build
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $framework" }
        dotnet run --project samples/DogwoodSharp.FoundationSmoke -c Release -f $framework --no-build
        if ($LASTEXITCODE -ne 0) { throw "Smoke failed: $framework" }
    }
    cargo fmt --manifest-path native/dogwoodsharp-bridge/Cargo.toml --check
    if ($LASTEXITCODE -ne 0) { throw 'Rust formatting failed.' }
    cargo clippy --locked --manifest-path native/dogwoodsharp-bridge/Cargo.toml --all-targets -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw 'Rust lint failed.' }
    cargo test --locked --manifest-path native/dogwoodsharp-bridge/Cargo.toml
    if ($LASTEXITCODE -ne 0) { throw 'Rust scaffold tests failed.' }
    cargo build --locked --release --manifest-path native/dogwoodsharp-bridge/Cargo.toml
    if ($LASTEXITCODE -ne 0) { throw 'Rust build failed.' }
} finally { Pop-Location }
