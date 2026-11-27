#!/bin/bash
set -euo pipefail

# GNU coreutils are not guaranteed on macOS. Provide sha256sum/realpath shims.
if ! command -v sha256sum >/dev/null 2>&1; then
  sha256sum() { shasum -a 256 "$@"; }
fi
if ! command -v realpath >/dev/null 2>&1; then
  realpath() { python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1"; }
fi

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

# Instead of sed, write the entire Cargo.toml from a template to avoid TOML parsing issues
# Use the patched dogwood-language path
# Escape the path for TOML (replace backslashes and escape quotes)
escapedPatchedDogwood=$(printf '%s' "$patchedDogwood" | sed 's/\\/\\\\/g; s/"/\\"/g')

cat > "$bridgeCargo" <<EOF
[package]
name = "dogwoodsharp-bridge"
version = "0.1.0"
edition = "2024"
rust-version = "1.99.1"
publish = false
license = "Apache-2.0"

[lib]
crate-type = ["cdylib", "rlib"]

[dependencies]
cedar-policy-core = { version = "=4.11.0", default-features = false }
dogwood-language = { path = "$escapedPatchedDogwood", default-features = false }
serde = { version = "1", features = ["derive"] }
serde_json = "1"

[profile.release]
panic = "unwind"
EOF

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
# Generate lockfile and pin cedar-policy-core to exactly 4.11.0 to match NativeBridge constant
cargo generate-lockfile --manifest-path "$bridgeCargo"
cargo update --manifest-path "$bridgeCargo" -p cedar-policy-core --precise 4.11.0
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