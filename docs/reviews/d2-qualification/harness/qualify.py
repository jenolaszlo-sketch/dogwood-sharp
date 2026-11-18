"""Independent C ABI, upstream conformance, limits and sensitivity checks."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import copy
import ctypes as ffi
import faulthandler
import hashlib
import json
from pathlib import Path
import subprocess

from corpus import cases, policy, POST, schema

MIB = 1024 * 1024
REQUEST = 6 * MIB + 64
ARTIFACTS = ("cedarPolicyText", "cedarSchemaJson", "cedarSchemaText")
FLAGS = ("serviceSchemaBuilt", "parsed", "actionSchemaBuilt", "lowered",
         "collisionCheckPassed", "validationPassed", "artifactsExported")


def encode(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode()


class Buffer(ffi.Structure):
    _fields_ = [("data", ffi.c_void_p), ("length", ffi.c_uint32)]


class Bridge:
    def __init__(self, image):
        self.library = ffi.CDLL(str(image))
        self.call = self.library.dogwoodsharp_call_v1
        self.call.argtypes = [ffi.c_uint32, ffi.c_uint32, ffi.c_void_p, ffi.c_uint32, ffi.POINTER(Buffer)]
        self.call.restype = ffi.c_uint32
        self.release = self.library.dogwoodsharp_free_v1
        self.release.argtypes = [ffi.POINTER(Buffer)]
        self.release.restype = None
        version = self.library.dogwoodsharp_abi_version
        version.argtypes = []
        version.restype = ffi.c_uint32
        assert version() == 1 and ffi.sizeof(Buffer) == 16

    def raw(self, op, data, expected=0, abi=1, length=None):
        output = Buffer()
        input_buffer = ffi.create_string_buffer(data) if data is not None else None
        size = len(data or b"") if length is None else length
        status = self.call(abi, op, input_buffer, size, ffi.byref(output))
        try:
            assert status == expected, (op, size, expected, status)
            if status:
                assert not output.data and output.length == 0
                return None
            limit = 8 * MIB if op in (2, 3) else 2 * MIB
            assert output.data and 0 < output.length <= limit
            return ffi.string_at(output.data, output.length)
        finally:
            self.release(ffi.byref(output))
            assert not output.data and output.length == 0
            self.release(ffi.byref(output))

    def reply(self, op, req):
        return json.loads(self.raw(op, encode(req)))


def oracle(command, mode, inputs):
    checked = subprocess.run([str(command), mode], input=b"".join(encode(value) + b"\n" for value in inputs),
                             capture_output=True, timeout=100)
    if checked.returncode:
        raise AssertionError(checked.stderr.decode("utf-8", "replace"))
    results = [json.loads(line) for line in checked.stdout.splitlines()]
    assert len(results) == len(inputs)
    return results


def projected_findings(entries):
    assert all(isinstance(entry["message"], str) and entry["message"] for entry in entries)
    return [{key: entry[key] for key in ("category", "location")} for entry in entries]


def expectation(upstream, op):
    terminal = upstream["terminal"]
    progressed = {"service-schema": 0, "parse": 1, "action-schema": 2, "lower": 3, "validate": 4}
    reached = progressed[terminal]
    reply = {name: index < reached for index, name in enumerate(FLAGS)}
    conflicts = [f for f in upstream.get("fields", []) if f["original"] is not None]
    first = conflicts[0] if conflicts else None
    validated = reached == 4 and first is None and not upstream["errors"]
    exported = op == 3 and validated
    reply.update(collisionCheckPassed=reached == 4 and first is None, validationPassed=validated,
                 artifactsExported=exported, policyCount=upstream.get("policyCount"),
                 parseDiagnostics=upstream.get("diagnostics", []), ruleCount=len(upstream.get("rules", [])),
                 selfContainedCedar=upstream.get("selfContainedCedar", False),
                 collision={key: first[key] for key in ("field", "actionNamespace", "actionId")} if first else None,
                 validationErrors=upstream.get("errors", []) if not first else [],
                 validationWarnings=upstream.get("warnings", []) if not first else [],
                 stageError="collision-check" if first else terminal if terminal not in ("parse", "validate") else None,
                 rules=[{key: rule[key] for key in (("ruleIndex", "cedarPolicyId", "sourceText") if op == 3
                                                     else ("ruleIndex", "cedarPolicyId"))}
                        for rule in upstream["rules"]] if validated else [])
    if op == 3:
        reply.update({key: upstream[key] if exported else "" for key in ARTIFACTS})
    return reply


def compare(actual, expected):
    assert sorted(actual) == sorted(expected), "operation-specific shape"
    for name, want in expected.items():
        got = actual[name]
        if name in ("parseDiagnostics", "validationErrors", "validationWarnings"):
            got = projected_findings(got)
        elif name == "stageError" and got is not None:
            assert isinstance(got["message"], str) and got["message"]
            if want == "collision-check":
                assert got["category"] == "generated-context-field-collision"
            got = got["stage"]
        elif name == "cedarSchemaJson" and got:
            got = json.loads(got)
        assert got == want, name


def check_intent(test, upstream, expect):
    requested = test["expect"]
    if requested == "collision":
        assert upstream["originalResolved"] and expect["collision"], test["id"]
    elif requested in ("export", "warn"):
        assert expect["artifactsExported"], (test["id"], upstream.get("errors"))
        if requested == "warn":
            assert expect["validationWarnings"], test["id"]
    elif requested == "validation":
        assert expect["lowered"] and expect["collisionCheckPassed"] and expect["validationErrors"], test["id"]
    else:
        assert upstream["terminal"] == requested, (test["id"], upstream["terminal"])


def sensitivity(replies, expectations):
    collision = copy.deepcopy(replies["original-overwrite"][3])
    collision.update(collision=None, collisionCheckPassed=True, stageError=None)
    early = copy.deepcopy(replies["validation-utf8"][3])
    early["artifactsExported"] = True
    for label in ARTIFACTS:
        early[label] = replies["cedar-policy"][3][label]
    partial = copy.deepcopy(replies["cedar-policy"][3])
    del partial["cedarSchemaText"]
    injected = [
        ("bypass-collision", "original-overwrite", 3, collision),
        ("export-before-validation", "validation-utf8", 3, early),
        ("drop-distincter", "temporal-custom", 3, replies["temporal-default"][3]),
        ("wrong-dispatch", "cedar-policy", 2, replies["cedar-policy"][3]),
        ("partial-artifact-set", "cedar-policy", 3, partial),
    ]
    detected = {}
    for name, case, op, mutant in injected:
        try:
            compare(mutant, expectations[case][op])
        except AssertionError as failure:
            detected[name] = str(failure)
        else:
            raise AssertionError(f"Comparator missed injected fault {name}")
    return detected


def transport(bridge, req):
    observed = {}

    def check(name, op, body, status, **extra):
        reply = bridge.raw(op, body, status, **extra)
        observed[name] = {"operation": op, "status": status, "responseBytes": len(reply or b"")}
        return reply

    check("empty", 2, b"", 1)
    check("unsupported-precedes-empty", 9, None, 3)
    check("unsupported-precedes-size", 9, None, 3, length=REQUEST + 1)
    check("abi-precedes-operation", 9, None, 2, abi=2)
    check("op2-d1-envelope", 2, b'{"source":""}', 5)
    check("op3-null-required", 3, b'{"source":"","policySchema":null}', 5)
    check("unknown-property", 2, b'{"source":"","policySchema":"","other":0}', 5)
    check("duplicate-property", 2, b'{"source":"","source":"","policySchema":""}', 5)
    check("invalid-utf8", 3, b'{"source":"\xff","policySchema":""}', 4)
    check("invalid-json", 3, b'{"source":', 5)
    body = encode(req)
    check("exact-encoded-ceiling", 2, body.ljust(REQUEST, b" "), 0)
    check("over-encoded-ceiling", 2, body.ljust(REQUEST + 1, b" "), 6)
    maximum = "//" + "a" * (MIB - 2)
    for name in ("source", "policySchema", "eventSchema", "macros"):
        exact = {**req, name: maximum}
        check("exact-" + name, 2, encode(exact), 0)
        check("over-" + name, 2, encode({**req, name: maximum + "a"}), 6)
    check("exact-distincter", 3, encode({**req, "distincter": "b" * 256}), 0)
    check("over-distincter", 3, encode({**req, "distincter": "b" * 257}), 6)
    # A bounded malformed token exercises the accepted D1 diagnostic ceiling.
    d1_large = {"source": '"' + "\x02" * (MIB - 2) + '"'}
    check("D1-response-limit", 1, encode(d1_large), 6)
    # Each of six rules expands a single small-node macro literal. Validation
    # still succeeds; only export crosses the D2 encoded response limit.
    huge = {**req, "source": "\n".join(policy(POST, "when { quote() }") for _ in range(6)),
            "macros": 'def cedar quote() { context.word == "' + "r" * (768 * 1024) + '" };'}
    checked = json.loads(check("D2-large-validation", 2, encode(huge), 0))
    assert checked["validationPassed"] and checked["ruleCount"] == 6
    check("D2-response-limit", 3, encode(huge), 6)
    assert json.loads(check("post-limit-independent-call", 3, body, 0))["artifactsExported"]
    return observed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("oracle", type=Path)
    parser.add_argument("image", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    bridge = Bridge(args.image.resolve())
    identity = bridge.reply(0, {})
    args.output.joinpath("identity.json").write_bytes(encode(identity))

    def derive(reqs):
        return oracle(args.oracle.resolve(), "derive", reqs)

    tests = cases(derive)
    observed = {}
    expected = {}
    evidence = []
    inspections = []
    inspection_expected = []
    for test, upstream in zip(tests, derive([test["request"] for test in tests]), strict=True):
        case = test["id"]
        expected[case] = {op: expectation(upstream, op) for op in (2, 3)}
        check_intent(test, upstream, expected[case][3])
        actual = observed[case] = {op: bridge.reply(op, test["request"]) for op in (2, 3)}
        for op in (2, 3):
            try:
                compare(actual[op], expected[case][op])
            except AssertionError as failure:
                raise AssertionError(f"{case} operation {op}: {failure}") from failure
        if expected[case][3]["artifactsExported"]:
            inspections.append({key: actual[3][key] for key in ARTIFACTS})
            bodies = {rule["cedarPolicyId"]: rule["body"] for rule in upstream["rules"]}
            inspection_expected.append({"bodies": [bodies[rule_id] for rule_id in upstream["emissionOrder"]],
                                        "validWithBothSchemas": True})
        evidence.append({"case": case, "intent": test["expect"], "upstreamStage": upstream["terminal"],
                         "generated": upstream.get("fields", []), "validated": actual[3]["validationPassed"],
                         "exported": actual[3]["artifactsExported"], "collision": actual[3]["collision"],
                         "diagnosticRanges": projected_findings(actual[3]["parseDiagnostics"] + actual[3]["validationErrors"]),
                         "warnings": projected_findings(actual[3]["validationWarnings"]),
                         "reportedRuleIds": [row["cedarPolicyId"] for row in actual[3]["rules"]],
                         "upstreamEmissionOrder": upstream.get("emissionOrder", [])})
        test["validate"] = actual[2]
        test["lower"] = actual[3]
    assert oracle(args.oracle.resolve(), "inspect", inspections) == inspection_expected
    overwrite = next(row for row in evidence if row["case"] == "original-overwrite")["generated"][0]
    assert overwrite["original"]["type"] != overwrite["augmented"]["type"] and overwrite["augmented"]["required"]

    with ThreadPoolExecutor(max_workers=6) as pool:
        def independent(index):
            test = tests[index % len(tests)]
            parse_only = bridge.reply(1, {"source": "permit(principal, action, resource);"})
            assert parse_only == {"success": True, "policyCount": 1, "diagnostics": []}
            for operation in (2, 3):
                compare(bridge.reply(operation, test["request"]), expected[test["id"]][operation])
        list(pool.map(independent, range(72), timeout=75))

    limits = transport(bridge, {"source": "", "policySchema": schema()})
    faults = sensitivity(observed, expected)
    args.output.joinpath("cases.json").write_bytes(encode(tests))
    result = {"cases": len(tests), "ABIReplies": 2 * len(tests), "CedarRoundTrips": len(inspections),
              "concurrentCalls": 72 * 3, "transport": limits, "sensitivity": faults, "evidence": evidence,
              "nativeSHA256": hashlib.file_digest(args.image.open("rb"), "sha256").hexdigest(),
              "identity": identity}
    args.output.joinpath("independent-native.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"INDEPENDENT ABI cases={len(tests)} replies={2 * len(tests)} CedarRoundTrips={len(inspections)} "
          f"concurrent=216 transport={len(limits)} sensitivity={len(faults)}")


if __name__ == "__main__":
    faulthandler.dump_traceback_later(120, exit=True)
    main()
