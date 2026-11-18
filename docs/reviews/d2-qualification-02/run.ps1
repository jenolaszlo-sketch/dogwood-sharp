param(
    [Parameter(Mandatory)][ValidateSet('win-x64','linux-x64','osx-arm64')]
    [string] $RuntimeIdentifier,
    [Parameter(Mandatory)][string] $Repository,
    [switch] $UseExisting,
    [switch] $JitOnly,
    [switch] $Sensitivity
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
$baseline = '8ff7344432be652d907598cf1d3b862a5d527222'
$baselineTree = '273ba9a8d1b16449690a118fe66e7dcfffc1f5bc'
$r1 = 'd0dbf762ab1c0d12a3e2ed21e0e338f09170c314'
$reviewRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$out = (New-Item -ItemType Directory -Force "$reviewRoot/artifacts/d2-qual-02/$RuntimeIdentifier").FullName
$python = if ($IsWindows) { 'python' } else { 'python3' }

function Save-Source([string] $Revision, [string] $Destination) {
    New-Item -ItemType Directory -Force $Destination | Out-Null
    git archive --format=tar "--output=$Destination/source.tar" $Revision src/DogwoodSharp Directory.Build.props Directory.Packages.props global.json
    tar -xf "$Destination/source.tar" -C $Destination
}

function Build-Mutant([string] $Name, [string] $Original, [string] $Replacement) {
    $directory = Join-Path $out $Name
    Save-Source $baseline $directory
    $path = "$directory/src/DogwoodSharp/NativeBridge.cs"
    $source = [IO.File]::ReadAllText($path)
    if ([regex]::Matches($source, [regex]::Escape($Original)).Count -ne 1) {
        throw "Mutation target mismatch for $Name"
    }
    [IO.File]::WriteAllText($path, $source.Replace($Original, $Replacement))
    dotnet build "$directory/src/DogwoodSharp" -c Release -f net10.0 --no-incremental | Out-Host
    "$directory/src/DogwoodSharp/bin/Release/net10.0/DogwoodSharp.dll"
}

Push-Location (Resolve-Path -LiteralPath $Repository).Path
try {
    $root = (Get-Location).Path
    if ((git rev-parse HEAD) -cne $baseline -or (git rev-parse 'HEAD^{tree}') -cne $baselineTree -or (git status --porcelain)) {
        throw 'A clean checkout of the commissioned R2 revision is required.'
    }
    Remove-Item Env:Configuration,Env:Platform -ErrorAction SilentlyContinue
    if (!$UseExisting) {
        ./eng/Verify.ps1 -RuntimeIdentifier $RuntimeIdentifier *>&1 | Tee-Object "$out/producer.log"
        if (git status --porcelain -- src/DogwoodSharp/packages.lock.json) {
            git restore -- src/DogwoodSharp/packages.lock.json
        }
        ./eng/Verify-D1OnD2.ps1 -RuntimeIdentifier $RuntimeIdentifier -Valgrind:$IsLinux *>&1 | Tee-Object "$out/d1.log"
        ./eng/Verify-D2R2.ps1 -RuntimeIdentifier $RuntimeIdentifier *>&1 | Tee-Object "$out/archived-d2.log"
    }
    if (git status --porcelain) { throw 'The producer checkout changed during verification.' }

    $relative = "runtimes/$RuntimeIdentifier/native"
    $asset = (Resolve-Path "artifacts/native/$relative").Path
    $manifestPath = Join-Path $asset 'dogwoodsharp-native.json'
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json -AsHashtable
    $image = Join-Path $asset $manifest.file
    $hash = (Get-FileHash $image).Hash.ToLowerInvariant()
    $lock = (Get-FileHash 'native/dogwoodsharp-bridge/Cargo.lock').Hash.ToLowerInvariant()
    if ($hash -cne $manifest.sha256 -or $lock -cne $manifest.cargoLockSha256) { throw 'Native manifest mismatch.' }
    $symbols = if ($IsWindows) {
        dumpbin /exports $image | Where-Object { $_ -match '^\s+\d+\s+[0-9a-f]+\s+[0-9a-f]+\s+' }
    } elseif ($IsMacOS) {
        nm -gU $image
    } else {
        nm -D --defined-only $image
    }
    $names = @($symbols | Select-String '(?<![A-Za-z0-9])dogwoodsharp_[A-Za-z0-9_]+\b' -AllMatches |
        ForEach-Object { $_.Matches.Value } | Sort-Object -Unique)
    if (($names -join ',') -cne 'dogwoodsharp_abi_version,dogwoodsharp_call_v1,dogwoodsharp_free_v1') {
        throw "Unexpected bridge export set $names"
    }
    $names | Set-Content "$out/exports.log"
    Copy-Item $manifestPath "$out/manifest.json"

    $oracle = (New-Item -ItemType Directory -Force "$out/oracle/src").Parent.FullName
    $oldOracle = 'docs/reviews/d2-qualification/harness/oracle'
    Copy-Item "$oldOracle/Cargo.toml","$oldOracle/Cargo.lock" $oracle
    Add-Content "$oracle/Cargo.toml" ([Environment]::NewLine + '[[bin]]' + [Environment]::NewLine + 'name = "q2-schema-oracle"' + [Environment]::NewLine + 'path = "src/main.rs"')
    Copy-Item "$PSScriptRoot/schema-oracle.rs" "$oracle/src/main.rs"
    $target = (Resolve-Path 'native/dogwoodsharp-bridge/target').Path
    cargo fmt --manifest-path "$oracle/Cargo.toml" --check
    cargo build --manifest-path "$oracle/Cargo.toml" --locked --release --target-dir $target
    $suffix = if ($IsWindows) { '.exe' } else { '' }
    $fixtures = (New-Item -ItemType Directory -Force "$out/fixtures").FullName
    $archived = (Resolve-Path "artifacts/d2-r2/corpus/$RuntimeIdentifier/cases.json").Path
    & $python "$PSScriptRoot/prepare.py" $asset "$target/release/q2-schema-oracle$suffix" $archived $fixtures |
        Tee-Object "$out/schema-oracle.log"

    $substitute = Join-Path (New-Item -ItemType Directory -Force "$out/substitute").FullName $manifest.file
    rustc --edition 2024 --crate-type cdylib "$PSScriptRoot/transport.rs" -o $substitute
    $project = "$PSScriptRoot/managed/Probe.csproj"
    $link = "-p:Q2BridgeRoot=$root/src/DogwoodSharp/bin"
    dotnet build $project -c Release $link
    $legacy = Join-Path $out 'r1'
    Save-Source $r1 $legacy
    dotnet build "$legacy/src/DogwoodSharp" -c Release
    if ($Sensitivity) {
        $guard = Build-Mutant 'guard-disposable' ' != AssemblyLoadContext.Default)' ' is null)'
        $free = Build-Mutant 'free-disposable' 'free(&output);' 'if (output.Length <= MaximumLoweringResponseBytes) { free(&output); }'
    }
    foreach ($framework in 'net8.0','net10.0') {
        $reference = "$legacy/src/DogwoodSharp/bin/Release/$framework/DogwoodSharp.dll"
        $arguments = @('--r1', $reference)
        if ($Sensitivity -and $framework -eq 'net10.0') {
            $arguments += @('--guard-mutant',$guard,'--free-mutant',$free)
        }
        & $python "$PSScriptRoot/drive.py" $asset "$PSScriptRoot/managed/bin/Release/$framework" $substitute $fixtures "$out/jit-$framework" @arguments |
            Tee-Object "$out/jit-$framework.log"
        if (!$JitOnly) {
            $publish = "$out/publish-$framework"
            dotnet publish $project -c Release -f $framework -r $RuntimeIdentifier -p:PublishAot=true $link -o $publish
            & $python "$PSScriptRoot/drive.py" $asset $publish $substitute $fixtures "$out/aot-$framework" --aot |
                Tee-Object "$out/aot-$framework.log"
        }
    }

    if ($IsLinux -and !$UseExisting) {
        $memory = (New-Item -ItemType Directory -Force "$out/memory").FullName
        cc -std=c11 -D_GNU_SOURCE -O2 -Wall -Wextra -Werror "$PSScriptRoot/ownership.c" -ldl -pthread -o "$memory/probe"
        & $python "$PSScriptRoot/memory.py" "$memory/probe" $image $memory | Tee-Object "$out/memory.log"
    }

    if (git status --porcelain) { throw 'The qualification changed the production checkout.' }
    [ordered]@{
        production = (git rev-parse HEAD)
        tree = (git rev-parse 'HEAD^{tree}')
        harness = (git -C $reviewRoot rev-parse HEAD)
        r1 = $r1
        rid = $RuntimeIdentifier
        runner = "$env:ImageOS $env:ImageVersion"
        nativeSha256 = $hash
        lockSha256 = $lock
        oracleLockSha256 = (Get-FileHash "$oracle/Cargo.lock").Hash.ToLowerInvariant()
        rust = (rustc --version)
        sdk = (dotnet --version)
        jitOnly = [bool]$JitOnly
        reusedAssets = [bool]$UseExisting
        mutationControls = [bool]$Sensitivity
    } | ConvertTo-Json | Set-Content "$out/provenance.json" -Encoding utf8
    if (!$UseExisting) {
        New-Item -ItemType Directory -Force "$out/regression" | Out-Null
        Copy-Item 'artifacts/test-results/*.trx' "$out/regression"
        Copy-Item "artifacts/d2-r1-d1-regression/qualification-$RuntimeIdentifier.json" "$out/regression"
        Copy-Item 'artifacts/d2-r1-d1-regression/logs/native-selftest.log' "$out/regression"
        Copy-Item "artifacts/d2-r1-d1-regression/reports/$RuntimeIdentifier" "$out/regression/d1" -Recurse -Force
        $archivedLogs = New-Item -ItemType Directory -Force "$out/regression/archived-d2"
        Get-ChildItem "artifacts/d2-r2/corpus/$RuntimeIdentifier" -File |
            Where-Object Extension -In '.log','.json' |
            Copy-Item -Destination $archivedLogs.FullName
    }
}
finally {
    Pop-Location
}
