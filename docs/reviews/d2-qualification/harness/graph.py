"""Verify the oracle and bridge use the same locked third-party graph."""
import json
from pathlib import Path
import sys


def graph(path):
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    versions = {entry["id"]: (entry["name"], entry["version"], entry["source"])
                for entry in data["packages"] if entry["source"] is not None}
    return {versions[node["id"]]: (sorted(node["features"]), sorted(versions[dep["pkg"]] for dep in node["deps"]))
            for node in data["resolve"]["nodes"] if node["id"] in versions}


bridge, oracle, original = map(graph, sys.argv[1:4])
assert bridge == oracle == original
named = {key[0]: (key, value[0]) for key, value in bridge.items()}
for name in ("cedar-policy", "cedar-policy-core", "cedar-policy-formatter", "dogwood-language"):
    assert sum(key[0] == name for key in bridge) == 1
for name in ("cedar-policy", "cedar-policy-core"):
    assert named[name][0][1] == "4.11.0"
    assert set(named[name][1]) - {"default"} == {"datetime", "decimal", "ipaddr"}
assert named["cedar-policy-formatter"][0][1] == "4.11.0" and not named["cedar-policy-formatter"][1]
dogwood, features = named["dogwood-language"]
assert dogwood[1] == "1.1.2" and dogwood[2].endswith("#929f49e7305534ac61ef06cf8e47269c596dc851") and not features
print(f"DEPENDENCY GRAPH PASS packages={len(bridge)}")
