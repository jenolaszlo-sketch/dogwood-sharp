"""Derive public and deliberately malformed response fixtures from the staged C ABI."""
import argparse
import copy
import ctypes
import hashlib
import json
from pathlib import Path
import subprocess


class Buffer(ctypes.Structure):
    _fields_ = [("data", ctypes.c_void_p), ("length", ctypes.c_uint32)]


def invoke(lib, op, request):
    payload = json.dumps(request).encode()
    output = Buffer()
    assert lib.dogwoodsharp_call_v1(1, op, payload, len(payload), ctypes.byref(output)) == 0
    try:
        assert output.data and output.length
        return json.loads(ctypes.string_at(output.data, output.length))
    finally:
        lib.dogwoodsharp_free_v1(ctypes.byref(output))
        assert output.data is None and output.length == 0


def save(path, obj):
    path.write_text(json.dumps(obj, indent=2) + "\n", encoding="utf-8")


def fixtures(lib, oracle, path):
    rows = []
    for case in json.loads(path.read_text(encoding="utf-8")):
        request = {k: case[k] for k in ("source", "policySchema")}
        validate = invoke(lib, 2, request)
        lowered = invoke(lib, 3, request)
        assert validate["validationPassed"] and lowered["artifactsExported"], case["id"]
        assert validate["validationWarnings"] == lowered["validationWarnings"]
        maps = json.loads(lowered["cedarSchemaJson"])
        shape = {
            namespace: sorted(f"{kind}:{name}" for kind, key in [
                ("action", "actions"), ("entity", "entityTypes"), ("type", "commonTypes")
            ] for name in declarations.get(key, {}))
            for namespace, declarations in maps.items()
        }
        assert shape == case["shape"], (case["id"], shape)
        assert bool(lowered["cedarSchemaText"].strip()) == bool(case["shape"])
        rows.append({**case, "reply": lowered})
    unnamed = copy.deepcopy(rows[0])
    assert unnamed["id"] == "empty"
    unnamed["id"] = "empty-unnamed"
    unnamed["reply"]["cedarSchemaJson"] = '{"":{"entityTypes":{},"actions":{}}}'
    oracle_input = "".join(json.dumps(row) + "\n" for row in [*rows, unnamed])
    proc = subprocess.run([str(oracle)], input=oracle_input, text=True, encoding="utf-8",
                          capture_output=True)
    if proc.returncode:
        raise RuntimeError(proc.stderr)
    semantic = [json.loads(line) for line in proc.stdout.splitlines()]
    assert [r["id"] for r in semantic] == [r["id"] for r in [*rows, unnamed]]
    return rows, semantic


def malformed_cases(rows, archived, output):
    replies = {r["id"]: r["reply"] for r in rows}
    archived_policy, = (r for r in archived if r["id"] == "cedar-policy")
    cases = []
    payloads = output / "payloads"
    payloads.mkdir(exist_ok=True)

    def add(id, baseline, expect="ok", **replacements):
        reply = copy.deepcopy(baseline)
        for key, value in replacements.items():
            if value is ...:
                reply.pop(key)
            else:
                reply[key] = value
        cases.append({"id": id, "expect": expect})
        save(payloads / f"{id}.json", reply)

    for id, row in replies.items():
        add(id, row)
    add("q1-original", archived_policy["lower"], "malformed", cedarSchemaText="")
    empty = replies["empty"]
    common = replies["common-only"]
    add("empty-unnamed", empty, cedarSchemaJson='{"":{"entityTypes":{},"actions":{}}}')
    for id in ("named-empty", "common-only", "entity-only", "action-only", "multiple"):
        add(id + "-blank", replies[id], "malformed", cedarSchemaText=" \t\r\n")
    add("empty-json-with-text", common, "malformed", cedarSchemaJson="{}")
    add("empty-unnamed-with-text", common, "malformed", cedarSchemaJson='{"":{"entityTypes":{},"actions":{}}}')
    add("empty-unnamed-with-space", empty, "malformed", cedarSchemaText="\t ")
    for n, value in enumerate([
        "", "{", "null", "[]", "5", '{"":null}', '{"":[]}', '{"":{}}',
        '{"A":{"entityTypes":{}}}', '{"A":{"actions":{}}}',
        '{"A":{"entityTypes":false,"actions":{}}}', '{"A":{"entityTypes":{},"actions":[]}}',
        '{"A":{"entityTypes":{},"actions":{},"commonTypes":null}}',
    ]):
        add(f"schema-kind-{n:02d}", common, "malformed", cedarSchemaJson=value)
    for key in ("cedarPolicyText", "cedarSchemaJson", "cedarSchemaText"):
        add(key + "-missing", common, "malformed", **{key: ...})
        add(key + "-null", common, "malformed", **{key: None})
        add(key + "-wrong-type", common, "malformed", **{key: 1})
    for id, body, expect in [
        ("raw-utf8", b"\xFF", "malformed"), ("raw-json", b"[", "malformed"),
        ("raw-length", bytes(8 * 1024 * 1024 + 1), "malformed"),
        ("status-with-buffer", b"{}", "native"),
    ]:
        (payloads / f"{id}.json").write_bytes(body)
        cases.append({"id": id, "expect": expect})
    save(output / "faults.json", cases)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("asset", type=Path)
    parser.add_argument("oracle", type=Path)
    parser.add_argument("archived_cases", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    manifest = json.loads((args.asset / "dogwoodsharp-native.json").read_text(encoding="utf-8-sig"))
    image = args.asset / manifest["file"]
    assert hashlib.sha256(image.read_bytes()).hexdigest() == manifest["sha256"]
    lib = ctypes.CDLL(str(image.resolve()))
    scope = {"dogwoodsharp_abi_version", "dogwoodsharp_call_v1", "dogwoodsharp_free_v1"}
    for name in scope:
        getattr(lib, name)
    lib.dogwoodsharp_abi_version.restype = ctypes.c_uint32
    lib.dogwoodsharp_call_v1.argtypes = [ctypes.c_uint32, ctypes.c_uint32, ctypes.c_char_p,
                                       ctypes.c_uint32, ctypes.POINTER(Buffer)]
    lib.dogwoodsharp_call_v1.restype = ctypes.c_uint32
    lib.dogwoodsharp_free_v1.argtypes = [ctypes.POINTER(Buffer)]
    lib.dogwoodsharp_free_v1.restype = None
    assert ctypes.sizeof(Buffer) == 16 and Buffer.data.offset == 0 and Buffer.length.offset == 8
    assert lib.dogwoodsharp_abi_version() == 1
    identity = invoke(lib, 0, {})
    assert all(manifest[k] == v for k, v in identity.items())
    save(args.output / "identity.json", identity)

    rows, semantic = fixtures(lib, args.oracle.resolve(), Path(__file__).with_name("fixtures.json"))
    save(args.output / "exports.json", rows)
    save(args.output / "semantics.json", semantic)
    archived = json.loads(args.archived_cases.read_text(encoding="utf-8-sig"))
    malformed_cases(rows, archived, args.output)
    print(f"SCHEMA ORACLE PASS native-round-trips={len(rows)} synthetic=1 rules={sum(len(r['rules']) for r in rows)}")


if __name__ == "__main__":
    main()
