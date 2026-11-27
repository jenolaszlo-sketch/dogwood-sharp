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
# Regenerate lockfile with all cedar-policy* crates pinned to 4.11.0 to match NativeBridge constant and bridge manifest
# First update all Cargo.toml files to require exactly 4.11.0 for cedar-policy* crates
for f in "$csDir/Cargo.toml" "$csRoot/Cargo.toml" "$csRoot"/*/Cargo.toml; do
  if [ -f "$f" ]; then
    # Match any version pattern for cedar-policy* crates and pin to 4.11.0
    sed -i 's/cedar-policy-core = { version = "[^"]*" }/cedar-policy-core = { version = "=4.11.0" }/' "$f"
    sed -i 's/cedar-policy = { version = "[^"]*" }/cedar-policy = { version = "=4.11.0" }/' "$f"
    sed -i 's/cedar-policy-formatter = { version = "[^"]*" }/cedar-policy-formatter = { version = "=4.11.0" }/' "$f"
    sed -i 's/cedar-policy-core = "=[^"]*"/cedar-policy-core = "=4.11.0"/' "$f"
    sed -i 's/cedar-policy = "=[^"]*"/cedar-policy = "=4.11.0"/' "$f"
    sed -i 's/cedar-policy-formatter = "=[^"]*"/cedar-policy-formatter = "=4.11.0"/' "$f"
  fi
done
# Also delete ALL existing Cargo.lock files to force regeneration with pinned versions
find "$csRoot" -name "Cargo.lock" -delete
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