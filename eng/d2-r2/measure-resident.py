"""Measure the D2 C consumer with and without Linux Memcheck."""

import argparse
import json
import re
import subprocess
from pathlib import Path


def measure(probe, image, output):
    rows = []
    for mode in ("sequential", "concurrent"):
        for iterations in (32, 256, 2048):
            command = [str(probe), str(image), str(iterations), mode]
            stem = output / f"{mode}-{iterations}"
            samples = subprocess.check_output(command, text=True)
            stem.with_suffix(".log").write_text(samples, encoding="utf-8")
            print(samples, end="", flush=True)
            log = stem.with_suffix(".memcheck.log")
            check = subprocess.run(
                ["valgrind", "--error-exitcode=97", "--leak-check=full",
                 "--show-leak-kinds=all", "--errors-for-leak-kinds=definite,indirect",
                 "--keep-debuginfo=yes", "--num-callers=40", f"--log-file={log}",
                 *command],
                capture_output=True,
                text=True,
            )
            stem.with_suffix(".memcheck.stdout").write_text(
                check.stdout + check.stderr, encoding="utf-8"
            )
            diagnostics = log.read_text(encoding="utf-8")
            row = {"mode": mode, "iterations": iterations, "exit": check.returncode}
            for name in ("definitely lost", "indirectly lost", "possibly lost",
                         "still reachable", "suppressed"):
                match = re.search(rf"{name}: +([\d,]+) bytes in ([\d,]+) blocks", diagnostics)
                if match is not None:
                    row[name] = [int(x.replace(",", "")) for x in match.groups()]
                elif "All heap blocks were freed" in diagnostics:
                    row[name] = [0, 0]
            match = re.search(r"ERROR SUMMARY: ([\d,]+) errors", diagnostics)
            row["errors"] = None if match is None else int(match[1].replace(",", ""))
            rows.append(row)
            print(json.dumps(row), flush=True)

    (output / "resident-summary.json").write_text(
        json.dumps(rows, indent=2) + "\n", encoding="utf-8"
    )
    return all(
        row["exit"] == row["errors"] == 0
        and row.get("definitely lost") == row.get("indirectly lost") == [0, 0]
        for row in rows
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("probe", type=Path)
    parser.add_argument("image", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    raise SystemExit(not measure(args.probe.resolve(), args.image.resolve(), args.output.resolve()))
