#!/bin/bash
set -euo pipefail

bridgeDir="native/dogwoodsharp-bridge"
targetArg=""
if [ "${RID}" = "linux-x64" ]; then targetArg="--target x86_64-unknown-linux-gnu"; fi
if [ "${RID}" = "osx-arm64" ]; then targetArg="--target aarch64-apple-darwin"; fi

# Patch the bridge's Cargo.toml to use the patched dogwood-language (with typed-context fix)
patchedDogwood="$(realpath r5r3-work/dogwood-patched/dogwood-language)"
echo "Patched dogwood-language path: $patchedDogwood"
if [ ! -d "$patchedDogwood" ]; then
    echo "ERROR: Patched dogwood-language not found at $patchedDogwood"
    exit 1
fi

bridgeCargo="$bridgeDir/Cargo.toml"
echo "Original bridge Cargo.toml:"
cat "$bridgeCargo"

# Use sed to replace the dogwood-language dependency with the patched path
# The original line has the full dependency spec on one line including default-features = false
# Add a comma after the closing brace to maintain valid TOML
sed -i "s|dogwood-language = { git = \"https://github.com/dogwood-policy/dogwood\", rev = \"929f49e7305534ac61ef06cf8e47269c596dc851\", default-features = false }|dogwood-language = { path = \"$patchedDogwood\" },|" "$bridgeCargo"

echo "Modified bridge Cargo.toml:"
cat "$bridgeCargo"

# Verify the patch was applied
if grep -q "path = \"$patchedDogwood\"" "$bridgeCargo"; then
    echo "SUCCESS: Bridge Cargo.toml patched successfully"
else
    echo "ERROR: Failed to patch bridge Cargo.toml"
    exit 1
fi

export CARGO_TARGET_DIR="r5r3-work/bridge-target"
cargo build --release $targetArg --manifest-path "$bridgeCargo"

libName=""
target=""
if [ "${RID}" = "linux-x64" ]; then libName="libdogwoodsharp_bridge.so"; target="x86_64-unknown-linux-gnu"; fi
if [ "${RID}" = "osx-arm64" ]; then libName="libdogwoodsharp_bridge.dylib"; target="aarch64-apple-darwin"; fi
if [ "${RID}" = "win-x64" ]; then libName="dogwoodsharp_bridge.dll"; fi

if [ -n "$target" ]; then
    libPath="r5r3-work/bridge-target/$target/release/$libName"
else
    libPath="r5r3-work/bridge-target/${RID}/release/$libName"
fi
if [ ! -f "$libPath" ]; then libPath="r5r3-work/bridge-target/release/$libName"; fi
if [ ! -f "$libPath" ]; then echo "Bridge library not found at $libPath"; exit 1; fi

sha=$(sha256sum "$libPath" | cut -d' ' -f1)
echo "D2 bridge built: $libPath (SHA256: $sha)"
echo "path=$libPath" >> $GITHUB_OUTPUT
echo "sha256=$sha" >> $GITHUB_OUTPUT