#!/bin/bash
set -euo pipefail

bridgeDir="native/dogwoodsharp-bridge"
targetArg=""
if [ "${RID}" = "linux-x64" ]; then targetArg="--target x86_64-unknown-linux-gnu"; fi
if [ "${RID}" = "osx-arm64" ]; then targetArg="--target aarch64-apple-darwin"; fi

export CARGO_TARGET_DIR="r5r3-work/bridge-target"
cargo build --locked --release $targetArg --manifest-path "$bridgeDir/Cargo.toml"

libName=""
if [ "${RID}" = "linux-x64" ]; then libName="libdogwoodsharp_bridge.so"; fi
if [ "${RID}" = "osx-arm64" ]; then libName="libdogwoodsharp_bridge.dylib"; fi
if [ "${RID}" = "win-x64" ]; then libName="dogwoodsharp_bridge.dll"; fi

libPath="r5r3-work/bridge-target/${RID}/release/$libName"
if [ ! -f "$libPath" ]; then libPath="r5r3-work/bridge-target/release/$libName"; fi
if [ ! -f "$libPath" ]; then echo "Bridge library not found at $libPath"; exit 1; fi

sha=$(sha256sum "$libPath" | cut -d' ' -f1)
echo "D2 bridge built: $libPath (SHA256: $sha)"
echo "path=$libPath" >> $GITHUB_OUTPUT
echo "sha256=$sha" >> $GITHUB_OUTPUT