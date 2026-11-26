#!/bin/bash
set -euo pipefail

# Write all output to a log file, then explicitly print it at the end
LOG_FILE="apply-patch.log"

# Use a subshell to capture all output
(
source="${1}"
workDir="r5r3-work/dogwood-patched"

if [ ! -d "$source" ]; then
  printf 'Dogwood source not found at %s\n' "$source"
  exit 1
fi

# Clean and copy
if [ -d "$workDir" ]; then rm -rf "$workDir"; fi
mkdir -p "$workDir"
rsync -a --exclude=target --exclude=.git "$source/" "$workDir/"
if [ $? -ne 0 ]; then printf 'Failed to copy Dogwood tree\n'; exit 1; fi

# Apply the official patch file
patchFile="docs/reviews/d3-impl-01-r5-r1/evidence/differential/corrected-oracle/dogwood-typed-context.patch"
if [ ! -f "$patchFile" ]; then
  printf 'Patch file not found at %s\n' "$patchFile"
  exit 1
fi

cd "$workDir"
if ! patch -p1 < "../../$patchFile"; then
  printf 'Failed to apply patch\n'
  exit 1
fi
cd - > /dev/null

# Verify - check that reserved_extn_expr function exists
apiPath="$workDir/dogwood-language/src/api.rs"
if ! grep -q "fn reserved_extn_expr" "$apiPath"; then
  printf 'Patched api.rs missing reserved_extn_expr function\n'
  exit 1
fi
if ! grep -q "reserved_extn_expr(map)" "$apiPath"; then
  printf 'Patched api.rs missing call to reserved_extn_expr in value_to_expr\n'
  exit 1
fi
printf 'Patched api.rs verified: reserved_extn_expr function and call site present\n'
printf 'patched_root=%s\n' "$workDir"

# Output the patched_root for GITHUB_OUTPUT
grep '^patched_root=' "$LOG_FILE" | tail -1 >> "$GITHUB_OUTPUT"

) > "$LOG_FILE" 2>&1

# Explicitly print the log file to stdout for GitHub Actions visibility
cat "$LOG_FILE"