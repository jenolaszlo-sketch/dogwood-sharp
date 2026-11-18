param(
    [string] $RuntimeIdentifier,
    [switch] $SkipAot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

if (!$RuntimeIdentifier) {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    $RuntimeIdentifier = switch ("$IsWindows,$IsLinux,$IsMacOS,$architecture") {
        'True,False,False,X64' { 'win-x64' }
        'False,True,False,X64' { 'linux-x64' }
        'False,False,True,Arm64' { 'osx-arm64' }
        default { throw "No D1 native asset for this host ($architecture)." }
    }
}

$smoke = 'samples/DogwoodSharp.FoundationSmoke'
$tests = 'tests/DogwoodSharp.Tests'
$bridge = 'native/dogwoodsharp-bridge/Cargo.toml'
$d2diff = 'native/dogwoodsharp-d2-diff/Cargo.toml'
$d2fixture = 'tests/DogwoodSharp.D2Conformance/lowering-v1.json'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet --info
    ./eng/Build-Native.ps1 -RuntimeIdentifier $RuntimeIdentifier
    cargo fmt --manifest-path $bridge --check
    cargo clippy --locked --release --manifest-path $bridge --all-targets -- -D warnings
    cargo test --locked --release --manifest-path $bridge -- --nocapture

    dotnet restore DogwoodSharp.slnx --locked-mode
    dotnet build DogwoodSharp.slnx -c Release --no-restore
    dotnet format DogwoodSharp.slnx --verify-no-changes --no-restore

    $relative = "runtimes/$RuntimeIdentifier/native"
    $asset = "artifacts/native/$relative"
    $binary = (Get-Content -Raw -LiteralPath "$asset/dogwoodsharp-native.json" | ConvertFrom-Json).file

    # The D2 differential resolves its own upstream graph, so it needs a locked
    # build before it can load the freshly produced bridge image.
    cargo build --release --locked --manifest-path $d2diff
    $d2diffBinary = Join-Path (Resolve-Path 'native/dogwoodsharp-d2-diff/target/release') `
        $(if ($IsWindows) { 'dogwoodsharp-d2-diff.exe' } else { 'dogwoodsharp-d2-diff' })
    & $d2diffBinary verify $d2fixture --library "$asset/$binary"
    if ($LASTEXITCODE -ne 0) {
        throw 'The D2 differential runner reported differences from the pinned upstream parser.'
    }

    New-Item -ItemType Directory -Force -Path artifacts/test-assets | Out-Null
    rustc --edition 2024 --crate-type cdylib "$tests/invalid_abi.rs" -o "artifacts/test-assets/$binary"

    foreach ($framework in @('net8.0', 'net10.0')) {
        foreach ($project in @($tests, $smoke)) {
            $destination = "$project/bin/Release/$framework/$relative"
            New-Item -ItemType Directory -Force -Path $destination | Out-Null
            Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $asset -File).FullName -Destination $destination
        }
        $destination = "$tests/bin/Release/$framework/test-assets"
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Copy-Item -LiteralPath "artifacts/test-assets/$binary" -Destination $destination
        dotnet test $tests -c Release -f $framework --no-build --logger "trx;LogFileName=$framework.trx" --results-directory artifacts/test-results
        dotnet run --project $smoke -c Release -f $framework --no-build

        if (!$SkipAot) {
            $out = "artifacts/aot/$RuntimeIdentifier/$framework"
            dotnet publish $smoke -c Release -f $framework -r $RuntimeIdentifier -o $out `
                -p:PublishAot=true -p:RestorePackagesWithLockFile=false `
                "-p:NuGetLockFilePath=$(Join-Path (Get-Location) "$out/packages.lock.json")"
            $destination = "$out/$relative"
            New-Item -ItemType Directory -Force -Path $destination | Out-Null
            Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $asset -File).FullName -Destination $destination
            $suffix = if ($IsWindows) { '.exe' } else { '' }
            & "./$out/DogwoodSharp.FoundationSmoke$suffix"
        }
    }
} finally { Pop-Location }
