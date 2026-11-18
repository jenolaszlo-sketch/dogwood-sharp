"""Run the qualification C consumer under glibc and Linux Memcheck."""
import argparse
from collections import defaultdict
import json
from pathlib import Path
import re
import subprocess


def measure(probe, image, output):
    controls = [(mode, count) for mode in ("sequential", "fixed") for count in (32, 256, 2048)]
    controls += [("churn", count) for count in (8, 64, 256, 1024)]
    controls.append(("unload", 32))
    rows = []
    for mode, count in controls:
        stem = output / f"{mode}-{count}"
        call = [str(probe), str(image), mode, str(count)]
        native = subprocess.check_output(call, text=True)
        stem.with_suffix(".native.log").write_text(native, encoding="utf-8")
        log = stem.with_suffix(".valgrind.log")
        run = subprocess.run(
            ["valgrind", "--leak-check=full", "--show-leak-kinds=all",
             "--errors-for-leak-kinds=definite,indirect", "--error-exitcode=97",
             "--keep-debuginfo=yes", "--num-callers=24", f"--log-file={log}", *call],
            capture_output=True, text=True,
        )
        stem.with_suffix(".valgrind.stdout").write_text(run.stdout + run.stderr, encoding="utf-8")
        diagnostics = log.read_text(encoding="utf-8")
        result = {"mode": mode, "rounds": count, "exit": run.returncode,
                  "native": native.splitlines(), "memcheck": run.stdout.splitlines()}
        assert f"PASS mode={mode} rounds={count}" in run.stdout, result
        for name in ("definitely lost", "indirectly lost", "possibly lost", "still reachable", "suppressed"):
            match = re.search(rf"{name}:\s+([\d,]+) bytes in ([\d,]+) blocks", diagnostics)
            assert match, (name, log)
            result[name] = [int(value.replace(",", "")) for value in match.groups()]
        match = re.search(r"ERROR SUMMARY: ([\d,]+) errors", diagnostics)
        result["errors"] = int(match[1].replace(",", "")) if match else None
        groups = defaultdict(lambda: [0, 0])
        for record in re.split(r"\n==\d+==\s*\n", diagnostics):
            match = re.search(r"([\d,]+) bytes in ([\d,]+) blocks? (?:are|is) possibly lost", record)
            if not match:
                continue
            key = next((k for k in (
                "ALL_AVAILABLE_EXTENSION_OBJECTS", "LazyLock<cedar_policy_core::extensions::Extensions",
                "LazyLock<cedar_policy_core::validator::extensions::ExtensionSchemas", "RESERVED_IDS",
                "regex_automata", "allocate_dtv",
            ) if k in record), "other")
            for index, number in enumerate(match.groups()):
                groups[key][index] += int(number.replace(",", ""))
        result["possiblyLostStacks"] = groups
        rows.append(result)
        print(json.dumps(result), flush=True)
        (output / "memory.json").write_text(json.dumps(rows, indent=2) + "\n", encoding="utf-8")

    for row in rows[:-1]:
        assert row["exit"] == row["errors"] == 0, row
        assert row["definitely lost"] == row["indirectly lost"] == [0, 0], row
    assert rows[-1]["exit"] == 97 and rows[-1]["definitely lost"][0] > 0 and rows[-1]["indirectly lost"][0] > 0
    print("MEMORY PASS resident-loss=0 unload-sensitivity=detected")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("probe", "image", "output"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    measure(args.probe.resolve(), args.image.resolve(), args.output.resolve())
