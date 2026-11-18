"""Run fresh processes on the ordinary public API and verify loader/buffer observations."""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess


def stage(original, destination, asset, substitute=None, managed=None):
    shutil.copytree(original, destination, dirs_exist_ok=True)
    manifest = json.loads((asset / "dogwoodsharp-native.json").read_text(encoding="utf-8-sig"))
    directory = destination / "runtimes" / manifest["rid"] / "native"
    directory.mkdir(parents=True, exist_ok=True)
    image = directory / manifest["file"]
    shutil.copyfile(substitute or (asset / image.name), image)
    digest = hashlib.sha256(image.read_bytes()).hexdigest()
    if substitute is None:
        assert digest == manifest["sha256"]
    manifest["sha256"] = digest
    (directory / "dogwoodsharp-native.json").write_text(json.dumps(manifest), encoding="utf-8")
    if managed:
        shutil.copyfile(managed, destination / "DogwoodSharp.dll")
    return image


def launch(args, directory, scenario, arguments, env):
    if args.aot:
        executable = directory / ("Probe.exe" if os.name == "nt" else "Probe")
        command = [str(executable)]
    else:
        command = ["dotnet", str(directory / "Probe.dll")]
    result = subprocess.run(command + arguments, cwd=directory, env=env, capture_output=True,
                            text=True, encoding="utf-8", errors="replace", timeout=120)
    (args.result / f"{scenario}.stdout.log").write_text(result.stdout, encoding="utf-8")
    (args.result / f"{scenario}.stderr.log").write_text(result.stderr, encoding="utf-8")
    return result


def verify_events(path, lowered, *, missing_release=None):
    lines = path.read_text(encoding="utf-8").splitlines()
    assert lines.count("abi") == 1, lines
    events = [line.split() for line in lines if line != "abi"]
    allocated = Counter((op, id) for event, op, id in events if event == "alloc")
    released = Counter((op, id) for event, op, id in events if event == "free")
    missing = Counter({("3", str(missing_release)): 1}) if missing_release else Counter()
    assert allocated - released == missing
    assert not released - allocated and all(n == 1 for n in allocated.values()), events
    assert sum(op == "0" for op, _ in allocated) == 1
    assert sum(op == "3" for op, _ in allocated) == lowered
    return {"abiChecks": 1, "identities": 1, "populatedResponses": sum(allocated.values()),
            "released": sum(released.values())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("asset", "probe", "substitute", "fixtures", "result"):
        parser.add_argument(name, type=Path)
    parser.add_argument("--aot", action="store_true")
    parser.add_argument("--r1", type=Path)
    parser.add_argument("--guard-mutant", type=Path)
    parser.add_argument("--free-mutant", type=Path)
    args = parser.parse_args()
    args.result.mkdir(parents=True, exist_ok=True)
    for key in ("asset", "probe", "fixtures", "result"):
        setattr(args, key, getattr(args, key).resolve())
    trace_linux = os.sys.platform.startswith("linux")
    summary = {}
    exports = str(args.fixtures / "exports.json")
    faults = str(args.fixtures / "faults.json")
    identity = args.fixtures / "identity.json"
    fault_rows = json.loads(Path(faults).read_text(encoding="utf-8"))
    fault_count = len(fault_rows)

    def perform(scenario, *, fake=False, arguments, swap=None, broken=False, failure_marker=None):
        target = args.result / scenario
        image = stage(args.probe, target, args.asset, args.substitute if fake else None, swap)
        events = args.result / (scenario + ".events.log")
        events.unlink(missing_ok=True)
        env = os.environ.copy()
        bad_identity = args.result / "broken-identity.json"
        if broken:
            bad_identity.write_bytes(b"\xFF")
        if fake:
            env.update(Q2_EVENTS=str(events), Q2_PAYLOADS=str(args.fixtures / "payloads"),
                       Q2_IDENTITY=str(bad_identity if broken else identity))
        if scenario == "real" and trace_linux:
            env["LD_DEBUG"] = "files"
        if broken:
            arguments += [str(image), str(identity), str(bad_identity)]
        if scenario == "real" and not trace_linux:
            arguments += [str(image)]
        run = launch(args, target, scenario, arguments, env)
        if failure_marker:
            assert run.returncode != 0 and failure_marker in run.stderr, (run.returncode, run.stderr)
        elif run.returncode != 0:
            raise RuntimeError(f"{scenario} exited {run.returncode}\n{run.stdout}\n{run.stderr[-6000:]}")
        print(run.stdout, end="")
        return run, events, image.name

    run, _, image = perform("real", arguments=["real", exports])
    assert "REAL PASS first-contenders=8 repeat=128 schema-cases=7" in run.stdout
    if not args.aot:
        assert run.stdout.count("CUSTOM CONTEXT PASS") == 2
    summary["real"] = run.stdout.splitlines()
    if trace_linux:
        trace = [line for line in run.stderr.splitlines() if image in line and
                 any(word in line for word in ("opening file=", "calling init:", "calling fini:"))]
        assert sum("opening file=" in line for line in trace) == 1, trace
        assert sum("direct_opencount=1" in line for line in trace) == 1, trace
        assert sum("calling fini:" in line for line in trace) == 1, trace
        summary["loader"] = trace

    run, events, _ = perform("faults", fake=True, arguments=["faults", faults])
    assert "FAULTS PASS" in run.stdout
    summary["faults"] = verify_events(events, fault_count)
    summary["faults"]["result"] = run.stdout.strip()

    run, events, _ = perform("bad-identity", fake=True, broken=True, arguments=["bad-identity"])
    assert "FAILED INIT PASS" in run.stdout
    summary["failedInit"] = verify_events(events, 0)
    summary["failedInit"]["result"] = run.stdout.strip()

    if args.r1:
        run, events, _ = perform("r1-original", fake=True, arguments=["r1"], swap=args.r1)
        assert "R1 REPRO QD2-01" in run.stdout
        summary["r1"] = verify_events(events, 1)
    if args.guard_mutant:
        perform("guard-mutant", arguments=["real", exports], swap=args.guard_mutant,
                failure_marker="Q2_GUARD_CONTROL private context initialized")
        summary["guardSensitivity"] = "private-context assertion detected initialization"
    if args.free_mutant:
        _, events, _ = perform("free-mutant", fake=True, arguments=["faults", faults], swap=args.free_mutant)
        try:
            verify_events(events, fault_count)
        except AssertionError:
            oversized_id = next(i for i, row in enumerate(fault_rows, 2) if row["id"] == "raw-length")
            summary["freeSensitivity"] = verify_events(events, fault_count, missing_release=oversized_id)
        else:
            raise AssertionError("The buffer auditor accepted the missing release.")

    (args.result / "managed.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print("MANAGED PROBE PASS")


if __name__ == "__main__":
    main()
