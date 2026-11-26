# DOGWOOD-SHARP-D3-IMPL-01-R5-R2 — ED-06 Retirement Record

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R2
**Date**: 26 November 2026
**Status**: Owner-approved retirement, subject to verification
**Baseline**: `b536ff8` (R5-R1 commission commit)

---

## 1. Original purpose and exact boundaries of ED-06

ED-06 was approved in the R4 qualification record as an authorized difference between the
pinned Dogwood reference and D3. It covers exactly two cases from the R4 differential
corpus:

| Case ID | Type | Encoding | Original Reference | D3 |
|---------|------|----------|-------------------|-----|
| `R3-C23-08-ipaddr-typed-context` | ipaddr | explicit `__extn` escape | Deny (clean) | Allow (clean) |
| `R3-C23-09-datetime-typed-context` | datetime | explicit `__extn` escape | Deny (clean) | Allow (clean) |

**Approved difference fields** (exactly three):
- `decision`
- `determining`
- `cleanAllow`

**Original rationale** (from R4 record): The pinned Dogwood value model has no `ipaddr` or
`datetime` variant, so the reference cannot represent these typed context values. The
reference therefore degrades the explicit `__extn` escape to a plain record before Cedar
evaluation, producing a clean Deny. D3 preserves the typed value through CedarSharp's
schema-directed JSON authorization parsing, producing a clean Allow. The owner approved
this representability ceiling as a known, bounded discrepancy.

---

## 2. The defect in the original reference conversion path

The defect is in `dogwood-language/src/api.rs` in the `value_to_expr` function (lines
~2020-2040 in the pinned revision `929f49e`). The function matches on the Dogwood
`Value` enum, which has variants for `Bool`, `Long`, `String`, `Decimal`, `Set`, `Record`,
and `Entity` — but **no variants for `IpAddr` or `DateTime`**.

When the JSON parser (`json_to_value`) encounters an explicit `__extn` escape like:

```json
{ "ip": { "__extn": { "fn": "ip", "arg": "192.168.1.1" } } }
```

the `cedar_escape_to_value` function returns `None` because the inner object has a
literal key `"__extn"` but the function only recognizes `__entity`. The object then
falls through to the generic `Value::Object` branch and becomes a plain record:

```rust
Value::Object(map) => {
    let pairs = map.iter().map(|(k, v)| (k.clone(), value_to_expr(v)));
    RestrictedExpression::new_record(pairs)
}
```

The typed value is **lost at this boundary** — Cedar receives a plain record
`{ "fn": "ip", "arg": "192.168.1.1" }` instead of an `ipaddr` extension value.

The same defect affects the implicit `{ "fn": "...", "arg": "..." }` encoding and the
bare string encoding (which the reference's JSON parser treats as a `String`).

`decimal` already had a dedicated `Value::Decimal` variant, so explicit `__extn`
decimal escapes were correctly handled — this is why the R5 baseline showed agreement
on `R3-C23-01-decimal-typed-value-match-explicit-escape`.

---

## 3. Evidence establishing representation equivalence

The R5-R1 commission established authoritative Cedar semantics through direct engine
evaluation using the `cedar-adjudicator` harness, which evaluates independently
constructed minimal Cedar policies and schema-directed JSON requests against the
underlying Cedar engine itself — **bypassing the Dogwood conversion path entirely**.

### Adjudication results (57 cases, 0 engine-version divergences)

| Type | Valid Encodings | Decision | Natural JSON |
|------|-----------------|----------|--------------|
| `ipaddr("192.168.1.1")` | explicit `__extn`, implicit `{fn,arg}`, bare string | **Allow** | `{"__extn":{"fn":"ip","arg":"192.168.1.1"}}` |
| `datetime("2024-01-01T00:00:00Z")` | explicit `__extn`, implicit `{fn,arg}`, bare string | **Allow** | `{"__extn":{"fn":"datetime","arg":"..."}}` |
| `decimal("1.5")` | explicit `__extn`, implicit `{fn,arg}`, bare string | **Allow** | `{"__extn":{"fn":"decimal","arg":"1.5"}}` |
| Invalid `ipaddr`/`datetime`/`decimal` | any spelling | **context-parse failure** | N/A |

**Key finding**: All three JSON spellings of one typed argument are **semantically
equivalent** and produce **byte-identical Cedar natural JSON**. The Cedar engine's
schema-directed JSON parser (`Context::from_json_value`) treats all three spellings as
the same typed value.

### Differential results against corrected reference

| Case | Type | Encoding | ORIGINAL Ref | CORRECTED Ref | D3 | Authority |
|------|------|----------|--------------|---------------|-----|-----------|
| `R3-C23-08-...-implicit-escape` | ipaddr match | implicit | Deny | **Allow** | Allow | Allow |
| `R3-C23-08-...-short-string` | ipaddr match | bare | Deny | **Allow** | Allow | Allow |
| `R3-C23-09-...-implicit-escape` | datetime match | implicit | Deny | **Allow** | Allow | Allow |
| `R3-C23-09-...-short-string` | datetime match | bare | Deny | **Allow** | Allow | Allow |
| `R3-C23-01-...-implicit-escape` | decimal match | implicit | Deny | **Allow** | Allow | Allow |
| `R3-C23-01-...-short-string` | decimal match | bare | Deny | **Allow** | Allow | Allow |

The corrected reference **agrees with D3 and the authoritative Cedar semantics** on all
six formerly-exempted alternate encodings. The original reference's degradation of
`__extn` objects to records is the **sole source** of the ED-06 discrepancy.

---

## 4. Corrected reference behavior

The R5-R1 correction adds a single function `reserved_extn_expr` to
`dogwood-language/src/api.rs` that recognizes the Cedar-reserved single-key `__extn`
shape and returns the corresponding `RestrictedExpression` for `ip` and `datetime`
constructors:

```rust
fn reserved_extn_expr(map: &BTreeMap<String, Value>) -> Option<RestrictedExpression> {
    if map.len() != 1 { return None; }
    let fields = match map.get("__extn")? { Value::Object(f) => f, _ => return None };
    if fields.len() != 2 { return None; }
    let func = fields.get("fn").and_then(|v| v.as_str())?;
    let argument = fields.get("arg").and_then(|v| v.as_str())?;
    match func {
        "ip" => Some(RestrictedExpression::new_ip(argument)),
        "datetime" => Some(RestrictedExpression::new_datetime(argument)),
        _ => None,
    }
}
```

This function is called in `value_to_expr` **before** degrading the object to a plain
record. The argument is not validated here — `Context::from_pairs` evaluates the
resulting restricted expression eagerly, so an invalid argument fails closed at context
build with the Cedar evaluation error, matching Cedar's own schema-directed JSON parser.

### Verified identities

| Artifact | SHA-256 |
|----------|---------|
| Patch | `76714faa4cbbc12866565736f1a6d7b8d4ef02ca53bd90c111eb035a3ee46b32` |
| Patched `api.rs` | `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3` |
| Files changed | `dogwood-language/src/api.rs` only |

---

## 5. Why ED-06 is no longer needed

1. **The representability ceiling is eliminated**: The corrected reference now produces
   Allow on the explicit `__extn` escape for both `ipaddr` and `datetime`, exactly as
   D3 does and as the authoritative Cedar semantics require.

2. **The approved divergence no longer exists**: ED-06 authorized a specific difference
   (Deny vs Allow on the explicit escape). That difference has been resolved by fixing
   the reference defect. The two ED-06 cases now **agree** (Allow on both sides).

3. **The broader representation-equivalence principle is established**: The
   commission's direct Cedar adjudication proves that all three JSON spellings are
   semantically equivalent. Retaining ED-06 would imply the explicit escape is
   specially privileged, which contradicts the authoritative semantics.

4. **No replacement compatibility exemption is needed**: The corrected reference and
   D3 already agree on all representations. The differential expectations for the
   formerly-exempted cases are now ordinary `agreement` expectations.

---

## 6. Impact on active differential expectations

### Original-reference register (preserved for historical reproducibility)
- ED-06 is **carried forward verbatim** from the frozen R4 register
- The two ED-06 cases still expect `approved-difference` classification
- The original reference still produces Deny on these cases
- This register is used only for historical verification, not for qualification

### Corrected-reference register (candidate for Q3 qualification)
- ED-06 is **removed** from the corrected-reference register
- The two ED-06 cases are reclassified to `agreement` with `expectedDifferenceIds: []`
- The corrected reference produces Allow; D3 produces Allow
- The formerly-exempted alternate-encoding cases (6 cases) are now ordinary `agreement`
  expectations (validated by the 124-case corrected-reference run: 124 passed, 0 failed)

### Three-way record impact
The R5-R1 `three-way-record.json` classifies the six alternate-encoding cases as
`reference defect`. Under ED-06 retirement, they remain `reference defect` relative to
the original reference, but become `agreement` relative to the corrected reference and
authoritative Cedar.

---

## 7. Verification of formerly-exempted cases

The following cases were previously covered by ED-06 approval and are now subject to
ordinary differential assertions against the corrected reference:

| Case ID | Type | Encoding | Corrected Ref | D3 | Status |
|---------|------|----------|---------------|-----|--------|
| `R3-C23-08-ipaddr-typed-context-match-implicit-escape` | ipaddr | implicit `{fn,arg}` | Allow | Allow | ✓ agreement |
| `R3-C23-08-ipaddr-typed-context-match-short-string` | ipaddr | bare string | Allow | Allow | ✓ agreement |
| `R3-C23-09-datetime-typed-context-match-implicit-escape` | datetime | implicit `{fn,arg}` | Allow | Allow | ✓ agreement |
| `R3-C23-09-datetime-typed-context-match-short-string` | datetime | bare string | Allow | Allow | ✓ agreement |
| `R3-C23-01-decimal-typed-value-match-implicit-escape` | decimal | implicit `{fn,arg}` | Allow | Allow | ✓ agreement |
| `R3-C23-01-decimal-typed-value-match-short-string` | decimal | bare string | Allow | Allow | ✓ agreement |

All six cases pass against the corrected reference on both `net8.0` and `net10.0`.

---

## 8. Historical preservation

The following are **preserved without modification**:

- R4 register (`docs/reviews/d3-impl-01/evidence/differential/r4/differences.json`) — ED-06 verbatim
- R5 register (`docs/reviews/d3-impl-01-r5/evidence/register.json`) — ED-06 verbatim
- R5-R1 original-reference register (`docs/reviews/d3-impl-01-r5-r1/evidence/register.json`) — ED-06 verbatim
- All frozen review records and qualification reports
- All manifest entries and evidence files

The retirement is recorded **only** in:
- This R5-R2 retirement record
- The R5-R2 corrected-reference register (ED-06 absent)
- The R5-R2 differential expectations (formerly-exempted cases now `agreement`)

---

## 9. Owner approval record

The owner has accepted the R5-R1 investigation and producer evidence as a basis for
continued remediation, including:

> - Retire ED-06 from the active qualification contract, subject to verification of the
>   reported semantic adjudication evidence.
> - Preserve ED-06 as a historical decision.
> - Adopt the corrected reference as the candidate differential comparison path,
>   subject to independent Q3 qualification.
> - Preserve the original pinned reference for historical reproducibility.

This record documents the technical justification and executed verification supporting
that approval.

---

## 10. No D3 qualification implied

This retirement record **does not declare D3 qualified**. The following remain
outstanding for Q3 readiness:

- Four pending differential execution cells (Linux x64 / .NET 8&10, macOS ARM64 / .NET 8&10)
- OQ-01 investigation and owner contract decision
- QD3Q-05 reachability reconciliation
- QD3Q-12 retained gaps
- Complete six-cell matrix with cross-platform execution evidence

---

*This retirement record is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R2 commission
evidence package. It is machine-checked against the committed R5-R1 evidence and the
direct Cedar adjudication authority table.*