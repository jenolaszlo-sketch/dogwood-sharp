#!/bin/bash
set -euo pipefail

rid="${RID}"
tfm="${TFM}"
probeRuntime="docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/bin/Release/$tfm/runtimes/$rid/native"
mkdir -p "$probeRuntime"

if [ -n "${BRIDGE_PATH}" ]; then
  dest="$probeRuntime/$(if [ "$OSTYPE" = "msys" ] || [ "$OSTYPE" = "cygwin" ]; then echo "dogwoodsharp_bridge.dll"; else echo "libdogwoodsharp_bridge.so"; fi)"
  cp "${BRIDGE_PATH}" "$dest"
  echo "Staged D2 bridge to $dest"
fi

if [ -n "${CEDARSHARP_PATH}" ]; then
  dest="$probeRuntime/$(if [ "$OSTYPE" = "msys" ] || [ "$OSTYPE" = "cygwin" ]; then echo "cedarsharp_native.dll"; else echo "libcedarsharp_native.so"; fi)"
  cp "${CEDARSHARP_PATH}" "$dest"
  echo "Staged CedarSharp native to $dest"
fi

bridgeSha="${BRIDGE_SHA256}"
csSha="${CEDARSHARP_SHA256}"
cargoLockSha="$(sha256sum native/dogwoodsharp-bridge/Cargo.lock | cut -d' ' -f1)"

# Determine target triple from RID
if [ "${RID}" = "linux-x64" ]; then
  target="x86_64-unknown-linux-gnu"
elif [ "${RID}" = "osx-arm64" ]; then
  target="aarch64-apple-darwin"
elif [ "${RID}" = "win-x64" ]; then
  target="x86_64-pc-windows-msvc"
else
  target="$rid"
fi

# Create dogwoodsharp-native.json using Python for reliable JSON
python3 -c "
import json
manifest = {
    'manifestVersion': 1,
    'file': 'libdogwoodsharp_bridge.so',
    'sha256': '$bridgeSha',
    'cargoLockSha256': '$cargoLockSha',
    'rid': '$rid',
    'target': '$target',
    'abiVersion': 1,
    'dogwoodVersion': '1.1.2',
    'dogwoodRevision': '929f49e7305534ac61ef06cf8e47269c596dc851',
    'cedarVersion': '4.11.0',
    'rustVersion': '1.99.1',
    'dogwoodFeatures': [],
    'cedarFeatures': ['datetime', 'decimal', 'ipaddr']
}
with open('$probeRuntime/dogwoodsharp-native.json', 'w') as f:
    json.dump(manifest, f, indent=2)
"
echo "Created dogwoodsharp-native.json"

if [ -n "$csSha" ]; then
  python3 -c "
import json
manifest = {
    'file': 'libcedarsharp_native.so',
    'sha256': '$csSha',
    'target': '$rid'
}
with open('$probeRuntime/cedarsharp-native.json', 'w') as f:
    json.dump(manifest, f, indent=2)
  "
  echo "Created cedarsharp-native.json"
fi