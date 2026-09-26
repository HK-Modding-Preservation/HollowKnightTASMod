"""Build a v2 candidate by retaining a prefix and appending real action samples.

Usage: python Author-Fixture.py SOURCE DEST --prefix N 50:- 1:up,quickCast
The source supplies the observed environment header. Generated inputs require
actual game execution; writing this file alone is not a compatibility result.
"""
import argparse
import json
from pathlib import Path

ACTIONS = ("left right up down rightStickLeft rightStickRight rightStickUp "
           "rightStickDown menuSubmit menuCancel jump evade dash superDash "
           "dreamNail attack cast focus quickMap quickCast textSpeedup "
           "skipCutscene openInventory paneRight paneLeft pause").split()
INDEX = {name.lower(): i for i, name in enumerate(ACTIONS)}


def sample(held, previous):
    return dict(channel="hero", values=[32767 if i in held else 0 for i in range(26)],
                pressedMask=sum(1 << i for i in held - previous),
                releasedMask=sum(1 << i for i in previous - held), mouse=None)


def build(source, prefix, segments):
    rows = [json.loads(line) for line in Path(source).read_text(encoding="utf-8-sig").splitlines() if line]
    header = rows[0]
    assert header["version"] == 2 and not header["mouseEnabled"]
    runs = []
    remaining = prefix
    for row in rows[1:]:
        if remaining <= 0:
            break
        take = min(remaining, row["repeatCount"])
        runs.append(dict(row, repeatCount=take))
        remaining -= take
    if remaining:
        raise ValueError("Prefix exceeds source Movie")
    held = {i for i, v in enumerate(runs[-1]["samples"][-1]["values"]) if v} if runs else set()
    neutral = sample(set(), set())

    def append(count, samples):
        if not count:
            return
        if runs and runs[-1]["samples"] == samples and "framesPerSecond" not in runs[-1]:
            runs[-1]["repeatCount"] += count
        else:
            runs.append(dict(repeatCount=count, samples=samples))

    for segment in segments:
        count, names = segment.split(":", 1)
        count = int(count)
        if count < 1:
            raise ValueError("Each action segment must have positive length")
        next_held = set() if names == "-" else {INDEX[n.lower()] for n in names.split(",")}
        append(1, [neutral, sample(next_held, held)])
        append(count - 1, [neutral, sample(next_held, next_held)])
        held = next_held
    return [header] + runs


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("destination")
    parser.add_argument("--prefix", type=int, required=True)
    parser.add_argument("segments", nargs="*")
    args = parser.parse_intermixed_args()
    result = build(args.source, args.prefix, args.segments)
    Path(args.destination).write_text("\n".join(json.dumps(x, ensure_ascii=False, separators=(",", ":")) for x in result) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"path": str(Path(args.destination).resolve()), "frames": sum(x["repeatCount"] for x in result[1:])}))
