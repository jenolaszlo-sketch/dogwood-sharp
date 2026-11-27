#!/bin/bash
set -euo pipefail

csRoot="r5r3-work/cedar-sharp"
csDir="r5r3-work/cedar-sharp/native"
if [ ! -d "$csRoot" ]; then
  git clone https://github.com/jenolaszlo-sketch/cedar-sharp.git "$csRoot"
  if [ $? -ne 0 ]; then echo "Failed to clone CedarSharp repo"; exit 1; fi
fi

targetArg=""
if [ "${RID}" = "linux-x64" ]; then targetArg="--target x86_64-unknown-linux-gnu"; fi
if [ "${RID}" = "osx-arm64" ]; then targetArg="--target aarch64-apple-darwin"; fi

export CARGO_TARGET_DIR="r5r3-work/cedarsharp-target"
# Regenerate lockfile with cedar-policy-core pinned to 4.11.0 to match NativeBridge constant and bridge manifest
# First update the Cargo.toml to require exactly 4.11.0 for cedar-policy-core
sed -i 's/cedar-policy-core = { version = "=[0-9.]*" }/cedar-policy-core = { version = "=4.11.0" }/' "$csDir/Cargo.toml"
# Also update the root Cargo.toml if it has cedar-policy-core
if [ -f "$csRoot/Cargo.toml" ]; then
  sed -i 's/cedar-policy-core = { version = "=[0-9.]*" }/cedar-policy-core = { version = "=4.11.0" }/' "$csRoot/Cargo.toml"
fi
cargo generate-lockfile --manifest-path "$csDir/Cargo.toml"
cargo build --locked --release $targetArg --manifest-path "$csDir/Cargo.toml"

libName=""
target=""
if [ "${RID}" = "linux-x64" ]; then libName="libcedarsharp_native.so"; target="x86_64-unknown-linux-gnu"; fi
if [ "${RID}" = "osx-arm64" ]; then libName="libcedarsharp_native.dylib"; target="aarch64-apple-darwin"; fi
if [ "${RID}" = "win-x64" ]; then libName="cedarsharp_native.dll"; fi

if [ -n "$target" ]; then
    libPath="r5r3-work/cedarsharp-target/$target/release/$libName"
else
    libPath="r5r3-work/cedarsharp-target/${RID}/release/$libName"
fi
if [ ! -f "$libPath" ]; then libPath="r5r3-work/cedarsharp-target/release/$libName"; fi
if [ ! -f "$libPath" ]; then echo "CedarSharp native not found at $libPath"; exit 1; fi

sha=$(sha256sum "$libPath" | cut -d' ' -f1)
echo "CedarSharp native built: $libPath (SHA256: $sha)"
echo "path=$libPath" >> $GITHUB_OUTPUT
echo "sha256=$sha" >> $GITHUB_OUTPUT