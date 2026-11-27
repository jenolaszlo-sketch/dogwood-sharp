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
# Build CedarSharp native with cedar-policy 4.13.0 to match CedarSharp C# interop's ExpectedSdk constant
# First update all Cargo.toml files to require exactly 4.13.0 for cedar-policy* crates
for f in "$csDir/Cargo.toml" "$csRoot/Cargo.toml" "$csRoot"/*/Cargo.toml; do
  if [ -f "$f" ]; then
    # Match any version pattern for cedar-policy* crates and pin to 4.13.0
    # Handle table format with features: cedar-policy = { version = "=X.Y.Z", default-features = false, features = [...] }
    sed -i 's/cedar-policy-core = { version = "[^"]*",/cedar-policy-core = { version = "=4.13.0",/' "$f"
    sed -i 's/cedar-policy = { version = "[^"]*",/cedar-policy = { version = "=4.13.0",/' "$f"
    sed -i 's/cedar-policy-formatter = { version = "[^"]*",/cedar-policy-formatter = { version = "=4.13.0",/' "$f"
    # Handle inline format: cedar-policy-core = "=X.Y.Z"
    sed -i 's/cedar-policy-core = "=[^"]*"/cedar-policy-core = "=4.13.0"/' "$f"
    sed -i 's/cedar-policy = "=[^"]*"/cedar-policy = "=4.13.0"/' "$f"
    sed -i 's/cedar-policy-formatter = "=[^"]*"/cedar-policy-formatter = "=4.13.0"/' "$f"
    # Handle version without =: cedar-policy = "X.Y.Z"
    sed -i 's/cedar-policy-core = "[0-9.]*"/cedar-policy-core = "=4.13.0"/' "$f"
    sed -i 's/cedar-policy = "[0-9.]*"/cedar-policy = "=4.13.0"/' "$f"
    sed -i 's/cedar-policy-formatter = "[0-9.]*"/cedar-policy-formatter = "=4.13.0"/' "$f"
  fi
done
# Debug: show the modified Cargo.toml files
echo "=== Modified cedar-sharp/native Cargo.toml ==="
cat "$csDir/Cargo.toml"
if [ -f "$csRoot/Cargo.toml" ]; then
  echo "=== Modified cedar-sharp Cargo.toml ==="
  cat "$csRoot/Cargo.toml"
fi
for f in "$csRoot"/*/Cargo.toml; do
  if [ -f "$f" ]; then
    echo "=== Modified $f ==="
    cat "$f"
  fi
done

# Also delete ALL existing Cargo.lock files to force regeneration with pinned versions
find "$csRoot" -name "Cargo.lock" -delete
# Generate lockfile, then force pin all cedar-policy* crates to exactly 4.13.0
cargo generate-lockfile --manifest-path "$csDir/Cargo.toml"
cargo update --manifest-path "$csDir/Cargo.toml" -p cedar-policy-core --precise 4.13.0
cargo update --manifest-path "$csDir/Cargo.toml" -p cedar-policy --precise 4.13.0
cargo update --manifest-path "$csDir/Cargo.toml" -p cedar-policy-formatter --precise 4.13.0
# Also pin transitive dependencies that might pull in newer versions
cargo update --manifest-path "$csDir/Cargo.toml" -p cedar-policy-mcp-schema-generator --precise 0.6.1 2>/dev/null || true

# Set environment variables for CedarSharp native build to match C# interop expectations
# Use environment variables if provided, otherwise default to expected values
export CEDARSHARP_RUST_VERSION="${CEDARSHARP_RUST_VERSION:-1.94.0}"
export CEDARSHARP_LANGUAGE_VERSION="${CEDARSHARP_LANGUAGE_VERSION:-4.5}"
# Use target from RID if not explicitly provided
export CEDARSHARP_TARGET="${CEDARSHARP_TARGET:-}"
if [ -z "$CEDARSHARP_TARGET" ]; then
  if [ "${RID}" = "linux-x64" ]; then
    export CEDARSHARP_TARGET="x86_64-unknown-linux-gnu"
  elif [ "${RID}" = "osx-arm64" ]; then
    export CEDARSHARP_TARGET="aarch64-apple-darwin"
  elif [ "${RID}" = "win-x64" ]; then
    export CEDARSHARP_TARGET="x86_64-pc-windows-msvc"
  else
    export CEDARSHARP_TARGET="${RID}"
  fi
fi
# Debug: log the target
echo "CEDARSHARP_TARGET: ${CEDARSHARP_TARGET:-unset}"
echo "CEDARSHARP_RUST_VERSION: ${CEDARSHARP_RUST_VERSION:-unset}"
echo "CEDARSHARP_LANGUAGE_VERSION: ${CEDARSHARP_LANGUAGE_VERSION:-unset}"

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