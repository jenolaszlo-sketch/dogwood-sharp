#!/bin/bash
set -euo pipefail

# Negative control for the shipped-subject campaign: replace the staged
# shipped D2 bridge with the qualification-only (patched-reference) bridge,
# keep the shipped manifest, and require the probe to reject the substitution
# before any authorization decision.

rid="${RID}"
tfm="${TFM}"
probeDir="${PROBE_DIR}"
oracle="${ORACLE_PATH}"
fixtures="${FIXTURES}"
register="${REGISTER}"
wrongBridge="${WRONG_BRIDGE}"
outDir="artifacts/r5r3-shipped/${rid}/${tfm}/negative-control"
mkdir -p "$outDir"

case "$rid" in
  linux-x64) libName="libdogwoodsharp_bridge.so" ;;
  osx-arm64) libName="libdogwoodsharp_bridge.dylib" ;;
  win-x64) libName="dogwoodsharp_bridge.dll" ;;
  *) echo "unsupported rid $rid"; exit 1 ;;
esac

copyDir="r5r3-work/negative-control-${rid}-${tfm}"
rm -rf "$copyDir"
cp -a "$probeDir" "$copyDir"
cp "$wrongBridge" "$copyDir/runtimes/$rid/native/$libName"

runner="$copyDir/PrepProbe"
if [ -f "$copyDir/PrepProbe.exe" ]; then runner="$copyDir/PrepProbe.exe"; fi

set +e
"$runner" differential "$fixtures" "$oracle" "$register" \
  "$outDir/observations.json" "$outDir/lifecycle.json" \
  "r5r3-work/negative-control-scratch-${rid}-${tfm}" \
  > "$outDir/console.log" 2>&1
code=$?
set -e

result=pass
if [ "$code" -eq 0 ]; then
  echo "negative control failed: substituted bridge was accepted"
  result=fail
fi
if ! grep -q "SHA-256 differs from the manifest\|Incompatible native bridge identity\|Native version discovery failed" "$outDir/console.log" "$outDir/observations.json" 2>/dev/null; then
  echo "negative control failed: expected native rejection message not found"
  result=fail
fi

cat > "$outDir/negative-control.json" <<EOF
{
  "rid": "$rid",
  "tfm": "$tfm",
  "result": "$result",
  "exitCode": $code,
  "substitutedBridge": "$wrongBridge",
  "expectedRejection": "native asset hash/identity rejection before authorization",
  "console": "negative-control/console.log"
}
EOF
cat "$outDir/negative-control.json"
if [ "$result" != "pass" ]; then exit 1; fi
