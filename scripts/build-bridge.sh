#!/bin/bash
set -euo pipefail

bridgeDir="native/dogwoodsharp-bridge"
targetArg=""
if [ "${RID}" = "linux-x64" ]; then targetArg="--target x86_64-unknown-linux-gnu"; fi
if [ "${RID}" = "osx-arm64" ]; then targetArg="--target aarch64-apple-darwin"; fi

# Patch the bridge's Cargo.toml to use the patched dogwood-language (with typed-context fix)
patchedDogwood="$(realpath r5r3-work/dogwood-patched/dogwood-language)"
bridgeCargo="$bridgeDir/Cargo.toml"
# Use sed to replace the dogwood-language dependency with the patched path
sed -i "s|dogwood-language = { git = \"https://github.com/dogwood-policy/dogwood\", rev = \"929f49e7305534ac61ef06cf8e47269c596dc851\"|dogwood-language = { path = \"$patchedDogwood\"|" "$bridgeCargo"

export CARGO_TARGET_DIR="r5r3-work/bridge-target"
cargo build --locked --release $targetArg --manifest-path "$bridgeCargo"

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