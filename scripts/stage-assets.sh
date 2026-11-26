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

cat > "$probeRuntime/dogwoodsharp-native.json" <<EOF
{
  "manifestVersion": 1,
  "file": "$(if [ "$OSTYPE" = "msys" ] || [ "$OSTYPE" = "cygwin" ]; then echo "dogwoodsharp_bridge.dll"; else echo "libdogwoodsharp_bridge.so"; fi)",
  "sha256": "$bridgeSha",
  "cargoLockSha256": "$(sha256sum native/dogwoodsharp-bridge/Cargo.lock | cut -d' ' -f1)",
  "target": "$rid",
  "abiVersion": 1,
  "dogwoodVersion": "1.1.2",
  "dogwoodRevision": "${DOGWOOD_PINNED_REV}",
  "cedarVersion": "4.11.0",
  "rustVersion": "1.99.1"
}
EOF

if [ -n "$csSha" ]; then
  cat > "$probeRuntime/cedarsharp-native.json" <<EOF
{
  "file": "$(if [ "$OSTYPE" = "msys" ] || [ "$OSTYPE" = "cygwin" ]; then echo "cedarsharp_native.dll"; else echo "libcedarsharp_native.so"; fi)",
  "sha256": "$csSha",
  "target": "$rid"
}
EOF
fi