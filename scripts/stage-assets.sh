#!/bin/bash
set -euo pipefail

# GNU coreutils are not guaranteed on macOS. Provide sha256sum/realpath shims.
if ! command -v sha256sum >/dev/null 2>&1; then
  sha256sum() { shasum -a 256 "$@"; }
fi
if ! command -v realpath >/dev/null 2>&1; then
  realpath() { python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1"; }
fi

rid="${RID}"
tfm="${TFM}"
probeRuntime="docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/bin/Release/$tfm/runtimes/$rid/native"

# Determine target triple and asset file names from RID before copying. The asset
# names are platform-specific: a macOS job must stage .dylib, not .so.
if [ "${RID}" = "linux-x64" ]; then
  target="x86_64-unknown-linux-gnu"
  libName="libdogwoodsharp_bridge.so"
  csLibName="libcedarsharp_native.so"
elif [ "${RID}" = "osx-arm64" ]; then
  target="aarch64-apple-darwin"
  libName="libdogwoodsharp_bridge.dylib"
  csLibName="libcedarsharp_native.dylib"
elif [ "${RID}" = "win-x64" ]; then
  target="x86_64-pc-windows-msvc"
  libName="dogwoodsharp_bridge.dll"
  csLibName="cedarsharp_native.dll"
else
  target="$rid"
  libName="libdogwoodsharp_bridge.so"
  csLibName="libcedarsharp_native.so"
fi

mkdir -p "$probeRuntime"

if [ -n "${BRIDGE_PATH}" ]; then
  dest="$probeRuntime/$libName"
  cp "${BRIDGE_PATH}" "$dest"
  echo "Staged D2 bridge to $dest"
fi

if [ -n "${CEDARSHARP_PATH}" ]; then
  dest="$probeRuntime/$csLibName"
  cp "${CEDARSHARP_PATH}" "$dest"
  echo "Staged CedarSharp native to $dest"
fi

bridgeSha="${BRIDGE_SHA256}"
csSha="${CEDARSHARP_SHA256}"
cargoLockSha="$(sha256sum native/dogwoodsharp-bridge/Cargo.lock | cut -d' ' -f1)"

# Create dogwoodsharp-native.json with exact field order expected by NativeBridge
# The Dogwood D2 bridge was built with cedarVersion 4.11.0 and rustVersion 1.99.1
cat > "$probeRuntime/dogwoodsharp-native.json" <<EOF
{
  "manifestVersion": 1,
  "file": "$libName",
  "sha256": "$bridgeSha",
  "cargoLockSha256": "$cargoLockSha",
  "rid": "$rid",
  "target": "$target",
  "abiVersion": 1,
  "bridgeVersion": "0.1.0",
  "dogwoodVersion": "1.1.2",
  "dogwoodRevision": "929f49e7305534ac61ef06cf8e47269c596dc851",
  "cedarVersion": "4.11.0",
  "rustVersion": "1.99.1",
  "dogwoodFeatures": [],
  "cedarFeatures": ["datetime", "decimal", "ipaddr"]
}
EOF
echo "Created dogwoodsharp-native.json"
cat "$probeRuntime/dogwoodsharp-native.json"

if [ -n "$csSha" ]; then
  cat > "$probeRuntime/cedarsharp-native.json" <<EOF
{
  "file": "$csLibName",
  "sha256": "$csSha",
  "rid": "$rid",
  "target": "$target",
  "abiVersion": 1,
  "bridgeVersion": "0.1.0",
  "sdkVersion": "4.13.0",
  "cedarVersion": "4.13.0",
  "rustVersion": "1.94.0",
  "languageVersion": "4.5",
  "features": ["datetime", "decimal", "ipaddr"]
}
EOF
  echo "Created cedarsharp-native.json"
  cat "$probeRuntime/cedarsharp-native.json"

  # Print the identity the staged library actually reports. CedarSharp's C#
  # loader fails closed with a single opaque message when this differs from the
  # constants compiled into the package, so surface the raw reply in the job log.
  if [ "${RID}" != "win-x64" ] && command -v python3 >/dev/null 2>&1; then
    python3 - "$probeRuntime/$csLibName" <<'PY'
import ctypes, sys
lib = ctypes.CDLL(sys.argv[1])

class Buffer(ctypes.Structure):
    _fields_ = [("data", ctypes.POINTER(ctypes.c_ubyte)), ("len", ctypes.c_size_t)]

lib.cedarsharp_call_v1.restype = ctypes.c_uint32
lib.cedarsharp_call_v1.argtypes = [ctypes.c_uint32, ctypes.c_char_p, ctypes.c_size_t, ctypes.POINTER(Buffer)]
lib.cedarsharp_free_v1.argtypes = [Buffer]
out = Buffer()
payload = b"{}"
status = lib.cedarsharp_call_v1(0, payload, len(payload), ctypes.byref(out))
identity = ctypes.string_at(out.data, out.len).decode("utf-8") if out.data else "<empty>"
lib.cedarsharp_free_v1(out)
print(f"CedarSharp native identity (status {status}): {identity}")
PY
  fi
fi