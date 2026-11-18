param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [switch] $UseExistingProducerAssets
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
$baseline = 'd0dbf762ab1c0d12a3e2ed21e0e338f09170c314'
$tree = '21ffc920e01a0c9875e33c572a947aff502d9b25'
$originalD2 = '8f394da2b501ea496ffa96856357f87fd9538c45'
$root = (Resolve-Path "$PSScriptRoot/../../..").Path

function Assert-QualifiedTree {
    if ((git rev-parse "$baseline^{tree}") -ne $tree) {
        throw 'The qualification baseline commit has an unexpected tree.'
    }
    $unexpected = @(git diff --name-only $baseline HEAD | Where-Object {
        $_ -notlike 'docs/reviews/d2-qualification/*' -and $_ -ne '.github/workflows/d2-qualification.yml'
    })
    if ($unexpected.Count -or (git status --porcelain)) {
        throw "Qualification requires a clean tree with only authorized changes: $unexpected"
    }
}

function Stage-Asset([string] $Destination) {
    $dir = New-Item -ItemType Directory -Force -Path (Join-Path $Destination $relative)
    Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $asset -File).FullName -Destination $dir.FullName
    $dir.FullName
}

Push-Location $root
try {
    Assert-QualifiedTree
    Remove-Item Env:Platform -ErrorAction SilentlyContinue
    Remove-Item Env:Configuration -ErrorAction SilentlyContinue
    $out = (New-Item -ItemType Directory -Path "artifacts/d2-qualification/$RuntimeIdentifier" -Force).FullName
    $revision = git rev-parse HEAD
    $qualificationTree = git rev-parse 'HEAD^{tree}'
    $imageOS = [Environment]::GetEnvironmentVariable('ImageOS')
    $imageVersion = [Environment]::GetEnvironmentVariable('ImageVersion')
    $python = if ($IsWindows) { 'python' } else { 'python3' }
    $provenance = [ordered]@{
        baseline = $baseline
        baselineTree = $tree
        checkout = $revision
        checkoutTree = $qualificationTree
        rid = $RuntimeIdentifier
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        runnerImage = "$imageOS $imageVersion"
        rust = (& rustc --version --verbose) -join "`n"
        dotnet = (& dotnet --info) -join "`n"
        python = (& $python --version)
        bridgeLock = (Get-FileHash native/dogwoodsharp-bridge/Cargo.lock).Hash.ToLowerInvariant()
    }

    if (!$UseExistingProducerAssets) {
        ./eng/Verify.ps1 -RuntimeIdentifier $RuntimeIdentifier *>&1 | Tee-Object -FilePath "$out/producer.log"
        if (git status --porcelain -- src/DogwoodSharp/packages.lock.json) {
            git restore -- src/DogwoodSharp/packages.lock.json
        }
        ./eng/Verify-D1OnD2.ps1 -RuntimeIdentifier $RuntimeIdentifier -Valgrind:$IsLinux *>&1 |
            Tee-Object -FilePath "$out/d1-regression.log"
    }

    $relative = "runtimes/$RuntimeIdentifier/native"
    $asset = (Resolve-Path "artifacts/native/$relative").Path
    $manifest = Get-Content -Raw -LiteralPath "$asset/dogwoodsharp-native.json" | ConvertFrom-Json -AsHashtable
    $image = Join-Path $asset $manifest.file
    if ((Get-FileHash -LiteralPath $image).Hash.ToLowerInvariant() -cne $manifest.sha256 -or
        $provenance.bridgeLock -cne $manifest.cargoLockSha256) {
        throw 'The qualification asset or lock does not match the producer manifest.'
    }

    $bridge = 'native/dogwoodsharp-bridge/Cargo.toml'
    $oracle = Join-Path $PSScriptRoot 'harness/oracle/Cargo.toml'
    $target = (Resolve-Path 'native/dogwoodsharp-bridge/target').Path
    $provenance['oracleLock'] = (Get-FileHash -LiteralPath (Join-Path (Split-Path $oracle -Parent) 'Cargo.lock')).Hash.ToLowerInvariant()
    $provenance['nativeImage'] = $manifest.sha256
    $provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$out/provenance.json" -Encoding utf8

    cargo metadata --locked --format-version 1 --filter-platform $manifest.target --manifest-path $bridge |
        Set-Content -LiteralPath "$out/bridge-graph.json" -Encoding utf8
    cargo metadata --locked --format-version 1 --filter-platform $manifest.target --manifest-path $oracle |
        Set-Content -LiteralPath "$out/oracle-graph.json" -Encoding utf8
    $originalGraph = Join-Path $out 'original-d2-dependency-graph'
    New-Item -ItemType Directory -Path "$originalGraph/src" -Force | Out-Null
    foreach ($file in @('Cargo.toml', 'Cargo.lock')) {
        git show "${originalD2}:native/dogwoodsharp-bridge/$file" |
            Set-Content -LiteralPath "$originalGraph/$file" -Encoding utf8
    }
    Set-Content -LiteralPath "$originalGraph/src/lib.rs" -Value '' -Encoding utf8
    cargo metadata --locked --format-version 1 --filter-platform $manifest.target --manifest-path "$originalGraph/Cargo.toml" |
        Set-Content -LiteralPath "$out/original-d2-graph.json" -Encoding utf8
    & $python "$PSScriptRoot/harness/graph.py" "$out/bridge-graph.json" "$out/oracle-graph.json" "$out/original-d2-graph.json" |
        Tee-Object -FilePath "$out/graph.log"
    cargo fmt --manifest-path $oracle --check
    cargo clippy --locked --release --all-targets --manifest-path $oracle --target-dir $target -- -D warnings
    cargo build --locked --release --manifest-path $oracle --target-dir $target
    $suffix = if ($IsWindows) { '.exe' } else { '' }
    $oracleImage = Join-Path $target "release/dogwood-d2-qualification$suffix"
    & $python -B "$PSScriptRoot/harness/qualify.py" $oracleImage $image $out |
        Tee-Object -FilePath "$out/independent.log"

    if ($IsLinux) {
        $suite = cargo test --locked --release --manifest-path $bridge --no-run --message-format=json |
            ForEach-Object { ConvertFrom-Json -AsHashtable -InputObject $_ } |
            Where-Object { $_.ContainsKey('profile') -and $_.profile.test -and $_.executable } |
            ForEach-Object { $_.executable }
        $vargs = @('--error-exitcode=1', '--leak-check=full', '--errors-for-leak-kinds=definite,indirect')
        foreach ($test in @('version_and_repeated_output_lifecycle', 'd2_repeated_output_lifecycle')) {
            valgrind @vargs "--log-file=$out/valgrind-$test.log" $suite --exact "tests::$test"
        }
        cc -std=c11 -D_GNU_SOURCE -O2 -Wall -Wextra -Werror "$PSScriptRoot/harness/ownership.c" -ldl -o "$out/ownership"
        valgrind @vargs "--log-file=$out/valgrind-ownership.log" "$out/ownership" $image |
            Tee-Object -FilePath "$out/ownership.log"
    }

    $project = Join-Path $PSScriptRoot 'harness/managed/Consumer.csproj'
    $fixtures = Join-Path $out 'cases.json'
    dotnet build $project -c Release
    $identityPath = Join-Path $out 'identity.json'
    $transportData = @{
        'invalid-json' = [byte[]]@(0x7b)
        'invalid-utf8' = [byte[]]@(0xff)
        'oversized' = [byte[]]::new(8 * 1024 * 1024 + 1)
    }
    foreach ($framework in @('net8.0', 'net10.0')) {
        $consumer = (Resolve-Path "$PSScriptRoot/harness/managed/bin/Release/$framework").Path
        $null = Stage-Asset $consumer
        dotnet "$consumer/Consumer.dll" corpus $fixtures | Tee-Object -FilePath "$out/jit-$framework.log"
        dotnet "$consumer/Consumer.dll" decoder $fixtures | Tee-Object -FilePath "$out/decoder-$framework.log"

        $fake = (New-Item -ItemType Directory -Force "$out/fake/$framework").FullName
        Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $consumer -File).FullName -Destination $fake
        $fakeAsset = Stage-Asset $fake
        $fakeImage = Join-Path $fakeAsset $manifest.file
        rustc --edition 2024 --crate-type cdylib "$PSScriptRoot/harness/transport.rs" -o $fakeImage
        $fakeManifest = $manifest.Clone()
        $fakeManifest.sha256 = (Get-FileHash -LiteralPath $fakeImage).Hash.ToLowerInvariant()
        $fakeManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$fakeAsset/dogwoodsharp-native.json" -Encoding utf8
        $env:D2Q_IDENTITY = $identityPath
        try {
            foreach ($test in ($transportData.Keys | Sort-Object)) {
                $payload = Join-Path $out "$test.bin"
                [IO.File]::WriteAllBytes($payload, $transportData[$test])
                $env:D2Q_REPLY = $payload
                dotnet "$fake/Consumer.dll" transport $test | Tee-Object -FilePath "$out/transport-$framework.log"
            }
        }
        finally {
            Remove-Item -Path Env:D2Q_IDENTITY,Env:D2Q_REPLY -ErrorAction SilentlyContinue
        }
        $publish = Join-Path $out "aot/$framework"
        dotnet publish $project -c Release -f $framework -r $RuntimeIdentifier -p:PublishAot=true -o $publish
        $null = Stage-Asset $publish
        & "$publish/Consumer$suffix" corpus $fixtures | Tee-Object -FilePath "$out/aot-$framework.log"
    }
    Assert-QualifiedTree
}
finally {
    Pop-Location
}
