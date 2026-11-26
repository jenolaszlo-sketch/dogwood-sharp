#!/bin/bash
set -euo pipefail

csDir="r5r3-work/cedar-sharp/native"
if [ ! -d "$csDir" ]; then
  mkdir -p "$(dirname "$csDir")"
  git clone https://github.com/jenolaszlo-sketch/cedar-sharp.git "$csDir"
  if [ $? -ne 0 ]; then echo "Failed to clone CedarSharp repo"; exit 1; fi
fi

targetArg=""
if [ "${RID}" = "linux-x64" ]; then targetArg="--target x86_64-unknown-linux-gnu"; fi
if [ "${RID}" = "osx-arm64" ]; then targetArg="--target aarch64-apple-darwin"; fi

export CARGO_TARGET_DIR="r5r3-work/cedarsharp-target"
cargo build --locked --release $targetArg --manifest-path "$csDir/Cargo.toml"

libName=""
if [ "${RID}" = "linux-x64" ]; then libName="libcedarsharp_native.so"; fi
if [ "${RID}" = "osx-arm64" ]; then libName="libcedarsharp_native.dylib"; fi
if [ "${RID}" = "win-x64" ]; then libName="cedarsharp_native.dll"; fi

libPath="r5r3-work/cedarsharp-target/${RID}/release/$libName"
if [ ! -f "$libPath" ]; then libPath="r5r3-work/cedarsharp-target/release/$libName"; fi
if [ ! -f "$libPath" ]; then echo "CedarSharp native not found at $libPath"; exit 1; fi

sha=$(sha256sum "$libPath" | cut -d' ' -f1)
echo "CedarSharp native built: $libPath (SHA256: $sha)"
echo "path=$libPath" >> $GITHUB_OUTPUT
echo "sha256=$sha" >> $GITHUB_OUTPUT