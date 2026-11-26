#!/bin/bash
set -euo pipefail

# Write all output to a log file first, then cat it at the end
# Use trap to ensure log is printed even on early exit
LOG_FILE="apply-patch.log"
exec > "$LOG_FILE" 2>&1

# Trap to always print log on exit
trap 'cat "$LOG_FILE"' EXIT

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

# Apply changes manually
apiPath="$workDir/dogwood-language/src/api.rs"
content=$(cat "$apiPath")

# Find value_to_expr function - try multiple possible signatures
insertMarkers=(
  'fn value_to_expr(v: &Value) -> RestrictedExpression {'
  'fn value_to_expr(v: &Value) -> RestrictedExpression{'
  'fn value_to_expr(v: &Value) -> RestrictedExpression'
)

insertLine=""
for marker in "${insertMarkers[@]}"; do
  insertLine=$(awk -v m="$marker" 'index($0,m){print NR; exit}' <<< "$content")
  if [ -n "$insertLine" ] && [ "$insertLine" != "0" ]; then
    break
  fi
done

if [ -z "$insertLine" ] || [ "$insertLine" = "0" ]; then
  printf 'Could not find value_to_expr function in api.rs\n'
  printf 'Debug: searching for value_to_expr...\n'
  awk '/value_to_expr/ {print NR ": " $0; count++} count >= 20 {exit}' <<< "$content"
  exit 1
fi

# Calculate byte offset using awk to avoid broken pipe from head
byteOffset=$(awk -v n="$((insertLine-1))" 'NR <= n {sum += length($0) + 1} END {print sum}' <<< "$content")

# Insert reserved_extn_expr function
newFunction='
/// DOGWOOD-SHARP-D3-IMPL-01-R5-R1 corrected-reference patch: recognize
/// Cedar'\''s reserved `__extn` escape as carried by the runtime value model and
/// return the corresponding extension-constructor expression.
///
/// The pinned value model has no ipaddr or datetime variant, so an `__extn`
/// record for those constructors previously degraded to a plain record here
/// and the typed value was lost at this boundary — the R5 typed-context root
/// cause. Cedar reserves the single-key `__extn` record unconditionally (with
/// or without schema direction; `CedarValueJson::ExtnEscape`), so recognizing
/// exactly that reserved shape here cannot misread a genuine record: a record
/// attribute literally named `__extn` with a different inner shape keeps the
/// pre-patch record behavior, exactly as Cedar'\''s own JSON parser treats it.
///
/// Only the single-argument `ip` and `datetime` constructors are modeled.
/// `decimal` already has a dedicated `Value` variant, so an explicit decimal
/// escape reaches this function only as a `Value::Decimal`, never as a raw
/// object. Every other function name keeps the pre-patch record behavior.
///
/// The constructor argument is deliberately not validated here:
/// `Context::from_pairs` evaluates the resulting restricted expression
/// eagerly, so an invalid argument fails closed at context build with the
/// Cedar evaluation error, matching the fail-closed behavior of Cedar'\''s own
/// schema-directed JSON authorization parsing.
fn reserved_extn_expr(map: &BTreeMap<String, Value>) -> Option<RestrictedExpression> {
    if map.len() != 1 { return None; }
    let fields = match map.get("__extn")? { Value::Object(fields) => fields, _ => return None };
    if fields.len() != 2 { return None; }
    let func = match fields.get("fn") { Some(Value::String(func)) => func.as_str(), _ => return None };
    let argument = match fields.get("arg") { Some(Value::String(argument)) => argument.as_str(), _ => return None };
    match func {
        "ip" => Some(RestrictedExpression::new_ip(argument)),
        "datetime" => Some(RestrictedExpression::new_datetime(argument)),
        _ => None,
    }
}

'

newContent="${content:0:$byteOffset}$newFunction${content:$byteOffset}"

# Find Value::Object arm in value_to_expr
callMarker='Value::Object(map) => {'
callLine=$(awk -v m="$callMarker" 'index($0,m){print NR; exit}' <<< "$newContent")
if [ -z "$callLine" ] || [ "$callLine" = "0" ]; then
  printf 'Could not find Value::Object arm in value_to_expr\n'
  printf '%s\n' "$newContent" | awk '/Value::Object/ {print NR ": " $0; count++} count >= 20 {exit}'
  exit 1
fi

callLineContent=$(sed -n "${callLine}p" <<< "$newContent")
bracePos=$(grep -ob "{" <<< "$callLineContent" | head -1 | cut -d: -f1)
if [ -z "$bracePos" ]; then
  printf 'Could not find opening brace for Value::Object arm\n'
  exit 1
fi

braceByteOffset=$(awk -v n="$((callLine-1))" 'NR <= n {sum += length($0) + 1} END {print sum}' <<< "$newContent")
braceByteOffset=$((braceByteOffset + bracePos))

insertCall='
// Corrected-reference patch: honor the reserved `__extn` escape
// before degrading the object to a plain record. See
// `reserved_extn_expr`.
if let Some(expr) = reserved_extn_expr(map) {
    return expr;
}
'

finalContent="${newContent:0:$((braceByteOffset+1))}$insertCall${newContent:$((braceByteOffset+1))}"
printf '%s\n' "$finalContent" > "$apiPath"
printf 'Manual patch applied successfully\n'

# Verify - use grep directly on file to avoid pipe issues
if ! grep -q "fn reserved_extn_expr" "$apiPath"; then
  printf 'Patched api.rs missing reserved_extn_expr function\n'
  awk '/reserved_extn_expr/ {print NR ": " $0; count++} count >= 20 {exit}' "$apiPath"
  exit 1
fi
if ! grep -q "reserved_extn_expr(map)" "$apiPath"; then
  printf 'Patched api.rs missing call to reserved_extn_expr in value_to_expr\n'
  awk '/reserved_extn_expr\(map\)/ {print NR ": " $0; count++} count >= 20 {exit}' "$apiPath"
  exit 1
fi
printf 'Patched api.rs verified: reserved_extn_expr function and call site present\n'
printf 'patched_root=%s\n' "$workDir"

# Output the patched_root for GITHUB_OUTPUT
grep '^patched_root=' "$LOG_FILE" | tail -1 >> "$GITHUB_OUTPUT"