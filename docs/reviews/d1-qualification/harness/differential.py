#!/usr/bin/env python3
"""Independent differential and wire-contract runner for DogwoodSharp D1.

Generates a corpus that is not derived from the producer fixture file, asks the
pinned upstream parser for expected outcomes (oracle), executes every request
through the independent C ABI consumer, and compares the wire response with the
contract in docs/architecture/native-interop.md.

Usage:
  python differential.py --probe <abi_probe> --oracle <oracle> --work <dir>
                         --report <dir> --target <rust-target>
"""

import argparse
import json
import struct
import subprocess
import sys
from pathlib import Path

SOURCE_LIMIT = 1024 * 1024
REQUEST_LIMIT = 6 * SOURCE_LIMIT + 64
RESPONSE_LIMIT = 2 * SOURCE_LIMIT
ABI = 1

STATUS_NAMES = {
    0: "ok",
    1: "invalid-argument",
    2: "unsupported-abi",
    3: "unsupported-operation",
    4: "invalid-utf8",
    5: "invalid-envelope",
    6: "limit",
    7: "panic",
    8: "internal-error",
}


def source_cases():
    """Small independent sources; expected outcome comes from the oracle."""
    cases = []

    def add(name, source):
        cases.append((name, source))

    # Basic policy and syntax coverage.
    add("empty", "")
    add("whitespace", " \t\r\n")
    add("line-comment", "// no policies here\n")
    add("block-comment", "/* block comment */")
    add("permit", "permit(principal, action, resource);")
    add("forbid", "forbid(principal, action, resource);")
    add("permit-forbid", "permit(principal, action, resource);\nforbid(principal, action, resource);")
    add("scoped-principal", 'permit(principal == User::"alice", action, resource);')
    add("scoped-action", 'permit(principal, action == App::Action::"Read", resource);')
    add("action-in", 'permit(principal, action in [App::Action::"Read", App::Action::"Write"], resource);')
    add("scoped-resource", 'permit(principal, action, resource == Doc::"report");')
    add("annotations", '@id("allow_all")\n@note("independent")\npermit(principal, action, resource);')
    add("when-comparison", "permit(principal, action, resource) when { context.count > 3 };")
    add("unless", "permit(principal, action, resource) unless { context.blocked };")
    add("when-unless", "permit(principal, action, resource) when { context.ok } unless { context.blocked };")
    add("logical", "permit(principal, action, resource) when { (1 < 2 && 2 <= 2) || false };")
    add("arithmetic", "permit(principal, action, resource) when { 1 + 2 * 3 - 4 == 3 };")
    add("if-then-else", 'permit(principal, action, resource) when { if context.ok then "a" else "b" == "a" };')
    add("set-literal", 'permit(principal, action, resource) when { "a" in ["a", "b"] };')
    add("record-literal", 'permit(principal, action, resource) when { { "a": 1 }.a == 1 };')
    add("string-unicode", 'permit(principal, action, resource) when { "héllo 😀" == "héllo 😀" };')
    add("string-escapes", 'permit(principal, action, resource) when { "a\\n\\t\\"b\\\\c" == "a\\n\\t\\"b\\\\c" };')
    add("nul-in-source", "permit(principal, action, res\u0000ource);")
    add("emoji-source", "😀")
    add("u2028", "// line\u2028separator\npermit(principal, action, resource);")
    add("crlf", "permit(principal, action, resource);\r\nforbid(principal, action, resource);\r\n")

    # Scope containment: D1 is a parser only. Undeclared entities/actions,
    # unknown attributes, and type-inconsistent expressions must follow the
    # pinned parser's acceptance instead of schema lowering or evaluation;
    # unknown functions must fail exactly as the pinned parser does.
    add("scope-unknown-entity", 'permit(principal == Ghost::"x", action, resource);')
    add("scope-unknown-attribute", "permit(principal, action, resource) when { context.no_such_attribute };")
    add("scope-type-mismatch", "permit(principal, action, resource) when { 1 + true };")
    add("scope-unknown-function", "permit(principal, action, resource) when { mystery(1) };")
    add("scope-action-group", 'permit(principal, action in Ghost::Action::"Read", resource);')

    # Temporal and macro forms.
    add("temporal-formerly",
        'permit(principal, action, resource) when temporal { '
        'formerly within 1h App::Action::"Login"::request{} };')
    add("temporal-exists",
        'permit(principal, action, resource) when temporal { '
        'exists (u: String). formerly within 1h App::Action::"Login"::request{input.user: u} };')
    add("temporal-count",
        'permit(principal, action, resource) when temporal { '
        'count for (t: Timepoint). where (formerly within 1h tp(t)) > 2 };')
    add("cedar-macro",
        'def cedar positive(?x) { ?x > 0 };\n'
        'permit(principal, action, resource) when { positive(context.n) };')
    add("temporal-macro",
        'def temporal seen(?u) { formerly within 2h App::Action::"Login"::request{input.user: ?u} };\n'
        'permit(principal, action, resource) when temporal { seen(context.user) };')
    add("macro-arity",
        'def cedar one(?x) { ?x == 1 };\n'
        'permit(principal, action, resource) when { one() };')
    add("macro-recursive",
        'def cedar loop(?x) { loop(?x) };\n'
        'permit(principal, action, resource) when { loop(true) };')

    # Invalid sources.
    add("malformed-eof", "permit(")
    add("invalid-effect", "allow(principal, action, resource);")
    add("missing-semicolon", "permit(principal, action, resource)")
    add("unclosed-brace", "permit(principal, action, resource) when { true;")
    add("trailing-junk", "permit(principal, action, resource); junk")
    add("multiple-errors", "allow(principal, action, resource);\npermit(principal, action, resource);\nforbid(")
    add("bom-valid", "\ufeffpermit(principal, action, resource);")
    add("bom-invalid", "\ufeffpermit(")
    add("bom-mid", "permit(\ufeffprincipal, action, resource);")
    add("unicode-before-error", "// é😀\npermit(")
    add("literal-backslash-u", r"\ud800")
    add("high-codepoint", "\U0010ffff")

    # Bounded source-nesting probes (documented default is 32).
    for depth in (30, 31, 32, 33, 34):
        add(f"depth-parens-{depth}",
            "permit(principal, action, resource) when { "
            + "(" * depth + "true" + ")" * depth + " };")
    for depth in (31, 32, 33):
        add(f"depth-lists-{depth}",
            "permit(principal, action, resource) when { "
            + "[" * depth + "1" + "]" * depth + " };")
    add("depth-records-33",
        "permit(principal, action, resource) when { "
        + '{"a":' * 33 + "1" + "}" * 33 + " };")

    # Macro-expansion node-budget probes (documented default is 1,000,000).
    for level in (10, 16, 18, 19, 20):
        calls = "true"
        for _ in range(level):
            calls = f"d({calls})"
        add(f"macro-budget-{level}",
            "def cedar d(?x) { ?x && ?x };\n"
            "permit(principal, action, resource) when { " + calls + " };")

    # Valid stress and diagnostic-volume probes.
    add("many-policies", "permit(principal, action, resource);" * 1000)
    for count in (100, 1000, 5000, 10000, 20000, 40000):
        add(f"many-errors-{count}", "permit(" * count)

    # Response-ceiling probes. A malformed 1 MiB string token is echoed whole in
    # the diagnostic message; JSON escaping of control characters and
    # backslashes expands the reply past the documented 2 MiB ceiling.
    add("limit-response-ctrl", '"' + "\u0001" * (SOURCE_LIMIT - 2) + '"')
    add("limit-response-backslash",
        '"' + "\\\\" * ((SOURCE_LIMIT - 2) // 2) + '"')

    return cases


def identity_json_validate(body, target):
    expected = {
        "abiVersion": 1,
        "bridgeVersion": "0.1.0",
        "dogwoodRevision": "929f49e7305534ac61ef06cf8e47269c596dc851",
        "dogwoodVersion": "1.1.2",
        "cedarVersion": "4.11.0",
        "rustVersion": "1.99.1",
        "target": target,
        "dogwoodFeatures": [],
        "cedarFeatures": ["datetime", "decimal", "ipaddr"],
    }
    if list(body.keys()) != list(expected.keys()):
        # Field order is not part of the contract; compare as a set and values.
        if set(body.keys()) != set(expected.keys()):
            return "identity fields differ: " + repr(sorted(body.keys()))
    for name, value in expected.items():
        if body.get(name) != value or type(body.get(name)) is not type(value):
            return f"identity field {name} is {body.get(name)!r}, expected {value!r}"
    return None


def is_int(value):
    return isinstance(value, int) and not isinstance(value, bool)


def parse_body_validate(body, source_bytes):
    if not isinstance(body, dict):
        return "parse response is not an object"
    if set(body.keys()) != {"success", "policyCount", "diagnostics"}:
        return "parse response fields differ: " + repr(sorted(body.keys()))
    success = body["success"]
    if not isinstance(success, bool):
        return "success is not a boolean"
    diagnostics = body["diagnostics"]
    if not isinstance(diagnostics, list):
        return "diagnostics is not an array"
    if success:
        if not is_int(body["policyCount"]) or body["policyCount"] < 0:
            return "successful policyCount is not a nonnegative integer"
        if diagnostics:
            return "successful parse has diagnostics"
        return None
    if body["policyCount"] is not None:
        return "failed policyCount is not null"
    if not diagnostics:
        return "failed parse has no diagnostics"
    for index, diagnostic in enumerate(diagnostics):
        if not isinstance(diagnostic, dict):
            return f"diagnostic {index} is not an object"
        if set(diagnostic.keys()) != {"category", "message", "location"}:
            return f"diagnostic {index} fields differ: {sorted(diagnostic.keys())}"
        if diagnostic["category"] not in ("syntax", "macro"):
            return f"diagnostic {index} category is {diagnostic['category']!r}"
        if not isinstance(diagnostic["message"], str):
            return f"diagnostic {index} message is not a string"
        location = diagnostic["location"]
        if location is None:
            continue
        if not isinstance(location, dict) or set(location.keys()) != {"offset", "length"}:
            return f"diagnostic {index} location shape differs"
        offset = location["offset"]
        length = location["length"]
        if not is_int(offset) or not is_int(length) or offset < 0 or length < 0:
            return f"diagnostic {index} location is not nonnegative integers"
        if offset > source_bytes:
            return f"diagnostic {index} offset is past EOF"
        eof_marker = offset == source_bytes and length == 1
        if not eof_marker and length > source_bytes - offset:
            return f"diagnostic {index} length is past EOF"
    return None


def duplicate_rejecting_object(pairs):
    seen = {}
    for key, value in pairs:
        if key in seen:
            raise ValueError(f"duplicate object key {key!r}")
        seen[key] = value
    return seen


def decode_json(body):
    text = body.decode("utf-8", errors="strict")
    return json.loads(
        text,
        object_pairs_hook=duplicate_rejecting_object,
        parse_constant=lambda value: (_ for _ in ()).throw(ValueError(f"non-finite number {value}")),
    )


def request_for_source(source, ensure_ascii=True):
    encoded = json.dumps(source, ensure_ascii=ensure_ascii)
    return ('{"source":' + encoded + "}").encode("utf-8")


def run_command(command, log_path):
    log_path.parent.mkdir(parents=True, exist_ok=True)
    with log_path.open("wb") as log:
        completed = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
    if completed.returncode != 0:
        print(f"command failed ({completed.returncode}): {' '.join(map(str, command))}", file=sys.stderr)
        print(f"log: {log_path}", file=sys.stderr)
        sys.exit(2)


def read_probe(path):
    data = path.read_bytes()
    if len(data) < 12 or data[:4] != b"D1QP":
        raise ValueError("probe result has no D1QP header")
    version, count = struct.unpack_from("<II", data, 4)
    if version != 1:
        raise ValueError(f"probe result version {version}")
    offset = 12
    results = {}
    order = []
    for _ in range(count):
        if offset + 4 > len(data):
            raise ValueError("truncated probe id length")
        (id_length,) = struct.unpack_from("<I", data, offset)
        offset += 4
        if offset + id_length + 12 > len(data):
            raise ValueError("truncated probe record")
        case_id = data[offset:offset + id_length].decode("utf-8", errors="strict")
        offset += id_length
        status, body_length = struct.unpack_from("<II", data, offset)
        offset += 8
        if offset + body_length + 4 > len(data):
            raise ValueError(f"truncated body for {case_id}")
        body = data[offset:offset + body_length]
        offset += body_length
        (empty,) = struct.unpack_from("<I", data, offset)
        offset += 4
        results[case_id] = {"status": status, "body": body, "empty": bool(empty)}
        order.append(case_id)
    if offset != len(data):
        raise ValueError(f"probe result has {len(data) - offset} trailing bytes")
    return results, order


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", required=True)
    parser.add_argument("--library", required=True)
    parser.add_argument("--oracle", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--target", required=True)
    args = parser.parse_args()

    work = Path(args.work).resolve()
    report_dir = Path(args.report).resolve()
    work.mkdir(parents=True, exist_ok=True)
    report_dir.mkdir(parents=True, exist_ok=True)
    requests = work / "requests"
    requests.mkdir(exist_ok=True)

    cases = source_cases()
    ids = [name for name, _ in cases]
    if len(ids) != len(set(ids)):
        raise ValueError("duplicate source case id")

    cases_document = {"cases": [{"id": name, "source": source} for name, source in cases]}
    cases_path = work / "oracle-cases.json"
    cases_path.write_text(json.dumps(cases_document, ensure_ascii=True), encoding="utf-8")
    oracle_report_path = work / "oracle-report.json"
    run_command([args.oracle, str(cases_path), str(oracle_report_path)], work / "oracle.log")
    oracle_report = json.loads(oracle_report_path.read_text(encoding="utf-8"))
    oracle = {case["id"]: case for case in oracle_report["cases"]}
    if set(oracle) != set(ids):
        raise ValueError("oracle report case set differs from input")

    # (id, operation, request bytes, expected status, oracle id or None)
    wire = []
    for name, source in cases:
        wire.append((name, 1, request_for_source(source), oracle[name]["wireStatus"], name))
        # Some sources are also sent with raw (non-escaped) UTF-8.
        if any(ord(ch) > 0x7f for ch in source):
            wire.append((name + "-raw-utf8", 1, request_for_source(source, ensure_ascii=False),
                         oracle[name]["wireStatus"], name))
    # One source containing a leading BOM is additionally sent with raw UTF-8 BOM bytes.
    wire.append(("bom-valid-raw-bytes", 1,
                 b'{"source":"\xef\xbb\xbfpermit(principal, action, resource);"}', 0, "bom-valid"))

    # Envelope and raw-wire rejections, plus precedence/protocol probes.
    raw = [
        ("wire-empty-request", 1, b"", 1, None),
        ("wire-whitespace-request", 1, b" \t\r\n", 5, None),
        ("wire-open-brace", 1, b"{", 5, None),
        ("wire-empty-object-parse", 1, b"{}", 5, None),
        ("wire-array", 1, b"[]", 5, None),
        ("wire-null", 1, b"null", 5, None),
        ("wire-string", 1, b'"x"', 5, None),
        ("wire-number", 1, b"1", 5, None),
        ("wire-source-true", 1, b'{"source":true}', 5, None),
        ("wire-source-object", 1, b'{"source":{}}', 5, None),
        ("wire-source-array", 1, b'{"source":[]}', 5, None),
        ("wire-source-null", 1, b'{"source":null}', 5, None),
        ("wire-source-case", 1, b'{"Source":"x"}', 5, None),
        ("wire-duplicate-source", 1, b'{"source":"a","source":"b"}', 5, None),
        ("wire-extra-source-field", 1, b'{"source":"x","extra":0}', 5, None),
        ("wire-trailing-value", 1, b'{"source":"x"} {"source":"y"}', 5, None),
        ("wire-trailing-comma", 1, b'{"source":"x",}', 5, None),
        ("wire-lone-high", 1, b'{"source":"\\ud800"}', 5, None),
        ("wire-lone-low", 1, b'{"source":"\\udc00"}', 5, None),
        ("wire-high-then-letter", 1, b'{"source":"\\ud800\\u0041"}', 5, None),
        ("wire-low-then-high", 1, b'{"source":"\\udc00\\ud800"}', 5, None),
        ("wire-valid-pair", 1, b'{"source":"\\ud83d\\ude00"}', 0, "emoji-source"),
        ("wire-literal-backslash-u", 1, b'{"source":"\\\\ud800"}', 0, "literal-backslash-u"),
        ("wire-invalid-utf8", 1, b'{"source":"\xff"}', 4, None),
        ("wire-invalid-utf8-outside", 1, b'\xff', 4, None),
        ("wire-raw-nul-in-string", 1, b'{"source":"a\x00b"}', 5, None),
        ("wire-raw-newline-in-string", 1, b'{"source":"a\nb"}', 5, None),
        ("wire-raw-tab-in-string", 1, b'{"source":"a\tb"}', 5, None),
        ("wire-bom-prefix", 1, b'\xef\xbb\xbf{"source":""}', 5, None),
        ("wire-invalid-utf8-shape", 1, b'{', 5, None),
        ("wire-json-whitespace-inside", 1,
         b'{' + b' ' * 10000 + b'"source":"x"' + b' ' * 10000 + b'}', 0, None),
        ("wire-unknown-nested-64", 1,
         b'{"source":"x","a":' + b'[' * 64 + b']' * 64 + b'}', 5, None),
        ("wire-unknown-nested-200", 1,
         b'{"source":"x","a":' + b'[' * 200 + b']' * 200 + b'}', 5, None),
        ("wire-operation-2", 2, b'{}', 3, None),
        ("wire-operation-max", 0xffffffff, b'{}', 3, None),
        ("wire-identity", 0, b'{}', 0, None),
        ("wire-identity-whitespace", 0, b'  {  }  ', 0, None),
        ("wire-identity-unknown", 0, b'{"x":1}', 5, None),
        ("wire-identity-extra-field", 0, b'{"abiVersion":1}', 5, None),
        ("wire-identity-duplicate", 0, b'{"x":1,"x":2}', 5, None),
    ]
    wire.extend(raw)

    # Byte-ceiling and decoded-source-ceiling probes. These are generated at
    # run time so no multi-megabyte fixture is stored in the repository.
    exact_comment = b"//" + b"a" * (SOURCE_LIMIT - 2)
    over_comment = b"//" + b"a" * (SOURCE_LIMIT - 1)
    multibyte_exact = b"//" + "\u00e9".encode("utf-8") * ((SOURCE_LIMIT - 2) // 2)
    multibyte_over = b"//" + "\u00e9".encode("utf-8") * ((SOURCE_LIMIT - 2) // 2 + 1)
    escaped_exact = b'{"source":"' + b"\\u0041" * SOURCE_LIMIT + b'"}'
    escaped_over = b'{"source":"' + b"\\u0041" * (SOURCE_LIMIT + 1) + b'"}'
    request_exact = b"{" + b" " * (REQUEST_LIMIT - 2) + b"}"
    request_over = b"{" + b" " * (REQUEST_LIMIT - 1) + b"}"
    wire.extend([
        ("limit-source-exact", 1, request_for_source(exact_comment.decode("ascii")), 0, None),
        ("limit-source-over", 1, request_for_source(over_comment.decode("ascii")), 6, None),
        ("limit-source-multibyte-exact", 1, request_for_source(multibyte_exact.decode("utf-8")), 0, None),
        ("limit-source-multibyte-over", 1, request_for_source(multibyte_over.decode("utf-8")), 6, None),
        ("limit-escaped-exact", 1, escaped_exact, 0, None),
        ("limit-escaped-over", 1, escaped_over, 6, None),
        ("limit-request-exact", 0, request_exact, 0, None),
        ("limit-request-over", 0, request_over, 6, None),
    ])

    # Write requests and the C probe manifest.
    manifest_lines = []
    for index, (name, operation, request, expected, oracle_id) in enumerate(wire):
        path = requests / f"{index:04d}-{name}.bin"
        path.write_bytes(request)
        manifest_lines.append(f"{name}\t{operation}\t{path.as_posix()}")
    manifest_path = work / "manifest.tsv"
    manifest_path.write_text("\n".join(manifest_lines) + "\n", encoding="utf-8")
    results_path = work / "abi-results.bin"
    run_command([args.probe, "probe", args.library, str(manifest_path), str(results_path)], work / "probe.log")
    results, order = read_probe(results_path)

    failures = []
    warnings = []
    status_counts = {}
    response_sizes = []
    message_mismatches = []
    wire_length_mismatches = []

    wire_by_id = {name: (operation, request, expected, oracle_id)
                  for name, operation, request, expected, oracle_id in wire}
    if set(order) != set(wire_by_id):
        failures.append({"id": "<all>", "kind": "case-set",
                         "detail": f"probe returned {len(order)} cases, expected {len(wire_by_id)}"})

    for case_id in order:
        operation, request, expected, oracle_id = wire_by_id[case_id]
        result = results[case_id]
        status = result["status"]
        status_counts[str(status)] = status_counts.get(str(status), 0) + 1
        if status != expected:
            failures.append({"id": case_id, "kind": "status",
                             "detail": f"expected {expected} ({STATUS_NAMES.get(expected)}), "
                                       f"got {status} ({STATUS_NAMES.get(status)})"})
            continue
        if status != 0:
            if result["body"]:
                failures.append({"id": case_id, "kind": "error-body",
                                 "detail": f"status {status} returned {len(result['body'])} body bytes"})
            if not result["empty"]:
                failures.append({"id": case_id, "kind": "error-slot",
                                 "detail": f"status {status} left a nonempty output slot"})
            continue
        if result["empty"]:
            failures.append({"id": case_id, "kind": "ok-slot", "detail": "status 0 left an empty slot"})
        if not result["body"]:
            failures.append({"id": case_id, "kind": "empty-body", "detail": "status 0 returned no body"})
            continue
        if len(result["body"]) > RESPONSE_LIMIT:
            failures.append({"id": case_id, "kind": "response-limit",
                             "detail": f"status 0 body is {len(result['body'])} bytes"})
        response_sizes.append(len(result["body"]))
        try:
            body = decode_json(result["body"])
        except (UnicodeDecodeError, ValueError) as error:
            failures.append({"id": case_id, "kind": "body-json", "detail": str(error)})
            continue

        if operation == 0:
            problem = identity_json_validate(body, args.target)
            if problem:
                failures.append({"id": case_id, "kind": "identity", "detail": problem})
            continue

        source = None
        if oracle_id is not None:
            source = next(value for name, value in cases if name == oracle_id)
        source_bytes = len(source.encode("utf-8")) if source is not None else len(request)
        problem = parse_body_validate(body, source_bytes)
        if problem:
            failures.append({"id": case_id, "kind": "parse-shape", "detail": problem})
            continue
        if oracle_id is None:
            continue
        expected_case = oracle[oracle_id]
        expected_success = expected_case["outcome"] == "ok"
        if body["success"] != expected_success:
            failures.append({"id": case_id, "kind": "outcome",
                             "detail": f"success={body['success']} but oracle outcome is "
                                       f"{expected_case['outcome']}"})
            continue
        if expected_success:
            if body["policyCount"] != expected_case["policyCount"]:
                failures.append({"id": case_id, "kind": "policy-count",
                                 "detail": f"policyCount={body['policyCount']} but oracle says "
                                           f"{expected_case['policyCount']}"})
        else:
            expected_diagnostics = expected_case["diagnostics"]
            actual_diagnostics = body["diagnostics"]
            if len(actual_diagnostics) != len(expected_diagnostics):
                failures.append({"id": case_id, "kind": "diagnostic-count",
                                 "detail": f"{len(actual_diagnostics)} diagnostics but oracle says "
                                           f"{len(expected_diagnostics)}"})
                continue
            for index, (actual, wanted) in enumerate(zip(actual_diagnostics, expected_diagnostics)):
                if actual["category"] != wanted["category"]:
                    failures.append({"id": case_id, "kind": "diagnostic-category",
                                     "detail": f"diagnostic {index}: {actual['category']} vs {wanted['category']}"})
                if actual["location"] != wanted["location"]:
                    failures.append({"id": case_id, "kind": "diagnostic-location",
                                     "detail": f"diagnostic {index}: {actual['location']} vs {wanted['location']}"})
                if actual["message"] != wanted["message"]:
                    message_mismatches.append({"id": case_id, "index": index})
        if body["success"] and expected_success and len(result["body"]) != expected_case["wireBytes"]:
            wire_length_mismatches.append({
                "id": case_id,
                "actual": len(result["body"]),
                "oracle": expected_case["wireBytes"],
            })

    # Depth, macro-budget and scope-containment transitions, reported from the
    # oracle and probe. The scope entries show that D1 stays a parser: unknown
    # entities, unknown attributes and type-mismatched expressions are accepted
    # like the pinned parser, while unknown functions fail the same way.
    transitions = {}
    for prefix in ("depth-parens", "depth-lists", "macro-budget", "scope"):
        entries = []
        for case_id in sorted(oracle):
            if case_id.startswith(prefix + "-"):
                entries.append({
                    "case": case_id,
                    "outcome": oracle[case_id]["outcome"],
                    "diagnostics": len(oracle[case_id]["diagnostics"]),
                    "wireStatus": oracle[case_id]["wireStatus"],
                    "wireBytes": oracle[case_id]["wireBytes"],
                })
        transitions[prefix] = entries

    maximum_wire = max((case["wireBytes"] for case in oracle.values()), default=0)
    ceiling_exercised = any(case["wireStatus"] == 6 for case in oracle.values()) or \
        any(entry[3] == 6 for entry in wire if entry[4] is None)

    report = {
        "target": args.target,
        "dogwoodRevision": oracle_report["dogwoodRevision"],
        "defaultMaxDepth": oracle_report["defaultMaxDepth"],
        "defaultMaxNodes": oracle_report["defaultMaxNodes"],
        "sourceCases": len(cases),
        "wireCases": len(wire),
        "statusCounts": status_counts,
        "responseBytesMin": min(response_sizes) if response_sizes else 0,
        "responseBytesMax": max(response_sizes) if response_sizes else 0,
        "oracleWireBytesMax": maximum_wire,
        "responseCeilingExercised": ceiling_exercised,
        "messageMismatches": message_mismatches,
        "wireLengthMismatches": wire_length_mismatches,
        "resourceTransitions": transitions,
        "failures": failures,
        "result": "pass" if not failures else "fail",
    }
    report_path = report_dir / "differential-report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    summary = [
        "D1 independent differential summary",
        f"target: {args.target}",
        f"oracle: Dogwood {oracle_report['dogwoodRevision']}, "
        f"depth={oracle_report['defaultMaxDepth']}, nodes={oracle_report['defaultMaxNodes']}",
        f"source cases: {len(cases)}; wire cases: {len(wire)}",
        f"status counts: {json.dumps(status_counts, sort_keys=True)}",
        f"response bytes: {report['responseBytesMin']}..{report['responseBytesMax']}; "
        f"oracle projected max: {maximum_wire}; ceiling exercised: {ceiling_exercised}",
        f"message mismatches: {len(message_mismatches)}; "
        f"wire length mismatches: {len(wire_length_mismatches)}",
        f"failures: {len(failures)}",
        "result: " + report["result"].upper(),
    ]
    for failure in failures[:50]:
        summary.append(f"FAIL {failure['id']} [{failure['kind']}] {failure['detail']}")
    (report_dir / "differential-summary.txt").write_text("\n".join(summary) + "\n", encoding="utf-8")
    print("\n".join(summary))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
