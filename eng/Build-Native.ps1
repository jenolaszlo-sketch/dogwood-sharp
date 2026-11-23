param([Parameter(Mandatory)][string] $RuntimeIdentifier)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

$platforms = @{
    'win-x64' = @('x86_64-pc-windows-msvc', 'dogwoodsharp_bridge.dll')
    'linux-x64' = @('x86_64-unknown-linux-gnu', 'libdogwoodsharp_bridge.so')
    'osx-arm64' = @('aarch64-apple-darwin', 'libdogwoodsharp_bridge.dylib')
}
if (!$platforms.ContainsKey($RuntimeIdentifier)) { throw "Unsupported RID $RuntimeIdentifier" }
$target, $file = $platforms[$RuntimeIdentifier]
$revision = '929f49e7305534ac61ef06cf8e47269c596dc851'
$cedarFeatures = @('datetime', 'decimal', 'ipaddr')
$bridge = 'native/dogwoodsharp-bridge'

Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $compiler = rustc --version --verbose
    $compiler | Write-Host
    if ($compiler -cnotcontains 'release: 1.99.1' -or $compiler -cnotcontains "host: $target") {
        throw "Expected pinned Rust 1.99.1 running on $target."
    }

    $graph = cargo metadata --locked --manifest-path "$bridge/Cargo.toml" --format-version 1 --filter-platform $target |
        ConvertFrom-Json -AsHashtable
    $dogwood = @($graph.packages | Where-Object name -CEQ 'dogwood-language')
    if ($dogwood.Count -ne 1 -or $dogwood[0].version -cne '1.1.2' -or
        $dogwood[0].source -cne "git+https://github.com/dogwood-policy/dogwood?rev=$revision#$revision") {
        throw 'The locked Dogwood dependency differs from the D1 pin.'
    }
    $dogwoodNode = $graph.resolve.nodes | Where-Object id -CEQ $dogwood[0].id
    if (@($dogwoodNode.features).Count -ne 0) { throw 'D1 requires an empty Dogwood feature set.' }
    foreach ($name in @('cedar-policy', 'cedar-policy-core', 'cedar-policy-formatter')) {
        $package = @($graph.packages | Where-Object name -CEQ $name)
        if ($package.Count -ne 1 -or $package[0].version -cne '4.11.0') {
            throw "Unexpected version for $name."
        }
        $node = $graph.resolve.nodes | Where-Object id -CEQ $package[0].id
        Write-Host "$name $($package[0].version): $($node.features -join ', ')"
        if ($name -in @('cedar-policy', 'cedar-policy-core')) {
            $extensions = @($node.features | Where-Object { $_ -cne 'default' } | Sort-Object -CaseSensitive)
            if (($extensions -join ',') -cne ($cedarFeatures -join ',')) {
                throw 'The resolved Cedar feature set differs from the D1 profile.'
            }
        } elseif (@($node.features).Count -ne 0) {
            throw 'The Cedar formatter feature set differs from the D1 profile.'
        }
    }

    cargo build --locked --release --manifest-path "$bridge/Cargo.toml"
    $output = "artifacts/native/runtimes/$RuntimeIdentifier/native"
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $image = Join-Path $output $file
    Copy-Item -LiteralPath (Join-Path $graph.target_directory "release/$file") -Destination $image
    $lockText = [System.IO.File]::ReadAllText("$bridge/Cargo.lock", (New-Object System.Text.UTF8Encoding($false, $true)))
    $canonicalLockText = $lockText.Replace("`r`n", "`n").Replace("`r", "`n")
    $lockHasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $canonicalLockHash = ([BitConverter]::ToString($lockHasher.ComputeHash(
            (New-Object System.Text.UTF8Encoding($false)).GetBytes($canonicalLockText)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $lockHasher.Dispose()
    }

    $manifest = [ordered]@{
        manifestVersion = 1
        rid = $RuntimeIdentifier
        file = $file
        sha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash.ToLowerInvariant()
        cargoLockSha256 = $canonicalLockHash
        abiVersion = 1
        bridgeVersion = '0.1.0'
        dogwoodRevision = $revision
        dogwoodVersion = '1.1.2'
        cedarVersion = '4.11.0'
        rustVersion = '1.99.1'
        target = $target
        dogwoodFeatures = @()
        cedarFeatures = $cedarFeatures
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'dogwoodsharp-native.json') -Encoding utf8
    $manifest | ConvertTo-Json | Write-Host
} finally { Pop-Location }
