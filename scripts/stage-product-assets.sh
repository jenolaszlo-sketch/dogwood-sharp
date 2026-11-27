#!/bin/bash
set -euo pipefail

# Stage the *shipped product* native pair for the Q3-R1 shipped-subject
# campaign. Unlike stage-assets.sh, this script never builds or rewrites the
# product bridge or the CedarSharp native: it copies the immutable artifacts
# produced by eng/Build-Native.ps1 and the published CedarSharp package,
# verifies the manifest hashes, and records the resident identities.

if ! command -v sha256sum >/dev/null 2>&1; then
  sha256sum() { shasum -a 256 "$@"; }
fi

rid="${RID}"
tfm="${TFM}"
bridgePath="${BRIDGE_PATH}"
bridgeManifest="${BRIDGE_MANIFEST}"
csExpected="${CEDARSHARP_EXPECTED_SHA256}"
probeRuntime="docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/bin/Release/$tfm/runtimes/$rid/native"
outFile="artifacts/r5r3-shipped/${rid}/${tfm}/product-assets.json"
mkdir -p "$probeRuntime" "$(dirname "$outFile")"

case "$rid" in
  linux-x64) libName="libdogwoodsharp_bridge.so"; csLibName="libcedarsharp_native.so"; target="x86_64-unknown-linux-gnu" ;;
  osx-arm64) libName="libdogwoodsharp_bridge.dylib"; csLibName="libcedarsharp_native.dylib"; target="aarch64-apple-darwin" ;;
  win-x64) libName="dogwoodsharp_bridge.dll"; csLibName="cedarsharp_native.dll"; target="x86_64-pc-windows-msvc" ;;
  *) echo "unsupported rid $rid"; exit 1 ;;
esac

if [ ! -f "$bridgePath" ]; then echo "missing product bridge at $bridgePath"; exit 1; fi
if [ ! -f "$bridgeManifest" ]; then echo "missing product bridge manifest at $bridgeManifest"; exit 1; fi

bridgeSha="$(sha256sum "$bridgePath" | cut -d' ' -f1 | tr 'A-F' 'a-f')"
manifestSha="$(sha256sum "$bridgeManifest" | cut -d' ' -f1 | tr 'A-F' 'a-f')"

node - "$bridgeManifest" "$bridgeSha" "$libName" "$rid" "$target" <<'NODE'
const fs = require('fs');
const [manifestPath, actualSha, libName, rid, target] = process.argv.slice(2);
const m = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const fail = msg => { console.error(`product manifest check failed: ${msg}`); process.exit(1); };
if (m.manifestVersion !== 1) fail('manifestVersion');
if (m.file !== libName) fail(`file ${m.file} != ${libName}`);
if (m.rid !== rid) fail(`rid ${m.rid} != ${rid}`);
if (m.target !== target) fail(`target ${m.target} != ${target}`);
if (String(m.sha256).toLowerCase() !== actualSha) fail(`sha256 ${m.sha256} != ${actualSha}`);
if (m.abiVersion !== 1) fail('abiVersion');
if (m.bridgeVersion !== '0.1.0') fail('bridgeVersion');
if (m.dogwoodRevision !== '929f49e7305534ac61ef06cf8e47269c596dc851') fail('dogwoodRevision');
if (m.dogwoodVersion !== '1.1.2') fail('dogwoodVersion');
if (m.cedarVersion !== '4.11.0') fail('cedarVersion');
if (m.rustVersion !== '1.99.1') fail('rustVersion');
if (m.cargoLockSha256 !== 'ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa') fail('cargoLockSha256 is not the committed shipped lock');
NODE

cp "$bridgePath" "$probeRuntime/$libName"
cp "$bridgeManifest" "$probeRuntime/dogwoodsharp-native.json"

# The CedarSharp native comes from the restored published 1.0.0 package.
if [ ! -f "$probeRuntime/$csLibName" ]; then echo "missing published CedarSharp native $probeRuntime/$csLibName"; exit 1; fi
if [ ! -f "$probeRuntime/cedarsharp-native.json" ]; then echo "missing CedarSharp package manifest"; exit 1; fi
csSha="$(sha256sum "$probeRuntime/$csLibName" | cut -d' ' -f1 | tr 'A-F' 'a-f')"
csManifestSha="$(sha256sum "$probeRuntime/cedarsharp-native.json" | cut -d' ' -f1 | tr 'A-F' 'a-f')"
if [ -n "$csExpected" ] && [ "$csSha" != "$(printf '%s' "$csExpected" | tr 'A-F' 'a-f')" ]; then
  echo "CedarSharp native hash $csSha != expected $csExpected"
  exit 1
fi

node - "$probeRuntime/cedarsharp-native.json" "$csSha" "$csLibName" "$rid" "$target" <<'NODE'
const fs = require('fs');
const [manifestPath, actualSha, libName, rid, target] = process.argv.slice(2);
const m = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const fail = msg => { console.error(`CedarSharp package manifest check failed: ${msg}`); process.exit(1); };
if (m.file !== libName) fail(`file ${m.file} != ${libName}`);
if (m.rid !== rid) fail(`rid ${m.rid} != ${rid}`);
if (m.target !== target) fail(`target ${m.target} != ${target}`);
if (String(m.sha256).toLowerCase() !== actualSha) fail(`sha256 ${m.sha256} != ${actualSha}`);
if (m.sdkVersion !== '4.13.0') fail('sdkVersion');
if (m.bridgeVersion !== '0.1.0') fail('bridgeVersion');
if (m.rustVersion !== '1.94.0') fail('rustVersion');
if (m.features.join(',') !== 'datetime,decimal,ipaddr') fail('features');
NODE

cat > "$outFile" <<EOF
{
  "rid": "$rid",
  "tfm": "$tfm",
  "target": "$target",
  "productBridge": {
    "path": "$bridgePath",
    "sha256": "$bridgeSha",
    "manifestPath": "$bridgeManifest",
    "manifestSha256": "$manifestSha",
    "cargoLockSha256": "ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa"
  },
  "cedarSharpNative": {
    "path": "$probeRuntime/$csLibName",
    "sha256": "$csSha",
    "manifestPath": "$probeRuntime/cedarsharp-native.json",
    "manifestSha256": "$csManifestSha",
    "package": "CedarSharp 1.0.0"
  },
  "stagedRuntime": "$probeRuntime"
}
EOF
echo "Product assets staged: $outFile"
cat "$outFile"
echo "product_assets=$outFile" >> "${GITHUB_OUTPUT:-/dev/null}"
