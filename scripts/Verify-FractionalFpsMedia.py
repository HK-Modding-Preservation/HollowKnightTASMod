"""Verify the opt-in StudioScenarioHarness --fractional-fps real-game export."""
import argparse
import json
import subprocess
from collections import Counter
from fractions import Fraction
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--ffprobe", type=Path, required=True)
    parser.add_argument("--ffmpeg", type=Path, required=True)
    args = parser.parse_args()
    root = args.output.resolve()
    states = json.loads((root / "states.json").read_text(encoding="utf-8-sig"))
    state = next(x["state"] for x in states if x["label"] == "export")
    assert state["videoExport.state"] == "Completed" and state["mismatchCount"] == "0"
    runs = [json.loads(line) for line in (root / "mixed.hktas").read_text(encoding="utf-8-sig").splitlines()[1:]]
    expected = Counter()
    for run in runs:
        expected[Fraction(run.get("fps", 50), run.get("fpsDenominator", 1))] += run["repeatCount"]
    path = root / "mixed.mp4"
    raw = subprocess.check_output([str(args.ffprobe), "-v", "error", "-show_streams", "-show_packets", "-of", "json", str(path)])
    (root / "mixed.probe.json").write_bytes(raw)
    data = json.loads(raw)
    video = next(s for s in data["streams"] if s["codec_type"] == "video")
    audio = next(s for s in data["streams"] if s["codec_type"] == "audio")
    packets = [p for p in data["packets"] if p["stream_index"] == video["index"]]
    assert len(packets) == int(state["videoExport.frames"])
    loading = len(packets) - sum(expected.values())
    assert loading >= 0
    expected[Fraction(50)] += loading
    time_base = Fraction(video["time_base"])
    counts = Counter()
    end = 0
    for packet in packets:
        assert int(packet["pts"]) == end, (packet, end)
        duration = int(packet["duration"])
        seconds = duration * time_base
        fps = min(expected, key=lambda f: abs(seconds - 1 / f))
        assert abs(seconds - 1 / fps) < Fraction(2, 1000000), (fps, seconds)
        counts[fps] += 1
        end += duration
    assert counts == expected, (counts, expected)
    assert abs(float(end * time_base) - float(state["videoExport.durationSeconds"])) < 0.000002
    assert abs(float(video["duration"]) - float(audio["duration"])) < 0.001
    assert float(state["videoExport.maximumAudioPeak"]) > 0
    decoded = subprocess.run([str(args.ffmpeg), "-v", "error", "-xerror", "-i", str(path),
                              "-fps_mode:v", "passthrough", "-enc_time_base:v", "1:1000000",
                              "-f", "null", "-"], check=True, capture_output=True)
    assert not decoded.stderr, decoded.stderr.decode(errors="replace")
    result = dict(status="PASS", file=str(path), frames=len(packets), loading_frames=loading,
                  frame_rate_counts={str(k): v for k, v in counts.items()},
                  video_seconds=video["duration"], audio_seconds=audio["duration"], decode="PASS")
    (root / "media-verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
