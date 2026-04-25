"""Scan MIDI files for periods of total silence ("quiet time").

A "gap" is a period during which no notes are sounding on any channel
(all notes have ended and no new note has started yet).

Usage
-----
    python find_quiet_gaps.py                     # scan all *.mid in this folder
    python find_quiet_gaps.py "70s*.mid"          # only scan files matching glob
    python find_quiet_gaps.py --min 0.5           # use 0.5s threshold (default 1.0s)
    python find_quiet_gaps.py "Disco*.mid" --min 2.0

A short threshold (e.g. 0.5s) will catch tiny rests; a longer one (e.g. 1.0s
or higher) only flags gaps a listener would clearly perceive as a pause. The
script ignores tail silence at the very end (songs that just finish).

Exit code: 0 if no gaps found, otherwise the number of files with at least
one gap above the threshold.
"""

from __future__ import annotations

import glob
import os
import struct
import sys


def _read_vlq(data: bytes, pos: int) -> tuple[int, int]:
    value = 0
    while True:
        b = data[pos]
        value = (value << 7) | (b & 0x7F)
        pos += 1
        if not (b & 0x80):
            return value, pos


def parse_midi(path: str):
    """Return (ticks_per_quarter, tempo_changes, notes).

    notes is a list of (start_tick, end_tick) for every successfully matched
    note-on/note-off pair on any channel. Drum channel (9) is included.
    """
    with open(path, "rb") as f:
        data = f.read()

    if data[:4] != b"MThd":
        raise ValueError(f"Not a MIDI file: {path}")
    header_len = struct.unpack(">I", data[4:8])[0]
    _fmt, ntrks, division = struct.unpack(">HHH", data[8:14])
    if division & 0x8000:
        raise ValueError(f"SMPTE division not supported: {path}")
    ppq = division

    pos = 8 + header_len
    tempo_changes: list[tuple[int, int]] = []
    notes: list[tuple[int, int]] = []

    for _ in range(ntrks):
        if data[pos:pos + 4] != b"MTrk":
            raise ValueError(f"Expected MTrk at pos {pos}")
        track_len = struct.unpack(">I", data[pos + 4:pos + 8])[0]
        track_start = pos + 8
        track_end = track_start + track_len

        cur = track_start
        tick = 0
        running = 0
        active: dict[tuple[int, int], int] = {}

        while cur < track_end:
            delta, cur = _read_vlq(data, cur)
            tick += delta

            b = data[cur]
            if b == 0xFF:
                cur += 1
                mt = data[cur]
                cur += 1
                ml, cur = _read_vlq(data, cur)
                md = data[cur:cur + ml]
                cur += ml
                if mt == 0x51 and len(md) == 3:
                    mpq = (md[0] << 16) | (md[1] << 8) | md[2]
                    tempo_changes.append((tick, mpq))
            elif b in (0xF0, 0xF7):
                cur += 1
                sl, cur = _read_vlq(data, cur)
                cur += sl
            else:
                if b & 0x80:
                    running = b
                    cur += 1
                status = running
                msg = status & 0xF0
                ch = status & 0x0F

                if msg in (0x80, 0x90):
                    pitch = data[cur]
                    vel = data[cur + 1]
                    cur += 2
                    key = (ch, pitch)
                    if msg == 0x90 and vel > 0:
                        active[key] = tick
                    else:
                        if key in active:
                            start = active.pop(key)
                            notes.append((start, tick))
                elif msg in (0xA0, 0xB0, 0xE0):
                    cur += 2
                elif msg in (0xC0, 0xD0):
                    cur += 1
                else:
                    raise ValueError(f"Unknown status {status:02x} at pos {cur}")

        # Close any still-open notes at end of track.
        for key, start in active.items():
            notes.append((start, tick))
        pos = track_end

    tempo_changes.sort()
    return ppq, tempo_changes, notes


def tick_to_seconds(tick: int, ppq: int, tempo_changes: list[tuple[int, int]]) -> float:
    """Convert tick to seconds, honoring tempo changes."""
    if not tempo_changes:
        return tick * 500000 / ppq / 1_000_000  # default 120 BPM

    secs = 0.0
    last_tick = 0
    last_mpq = 500000
    for ct, mpq in tempo_changes:
        if ct > tick:
            break
        secs += (ct - last_tick) * last_mpq / ppq / 1_000_000
        last_tick = ct
        last_mpq = mpq
    secs += (tick - last_tick) * last_mpq / ppq / 1_000_000
    return secs


def find_gaps(path: str, min_seconds: float = 1.0) -> tuple[list[tuple[float, float]], float]:
    """Return (gaps, total_seconds) where each gap is (start_seconds, end_seconds)."""
    ppq, tempo_changes, notes = parse_midi(path)
    if not notes:
        return [], 0.0

    events: list[tuple[int, int]] = []
    for s, e in notes:
        events.append((s, +1))
        events.append((e, -1))
    events.sort()

    # Combine simultaneous events at the same tick into one delta.
    timeline: list[tuple[int, int]] = []
    i = 0
    while i < len(events):
        t = events[i][0]
        d = 0
        while i < len(events) and events[i][0] == t:
            d += events[i][1]
            i += 1
        timeline.append((t, d))

    gaps: list[tuple[float, float]] = []
    active = 0
    silence_start: int | None = 0  # the file starts silent (no notes yet)

    for tick, delta in timeline:
        prev_active = active
        active += delta
        if prev_active == 0 and active > 0:
            # Silence ended at this tick.
            if silence_start is not None and tick > silence_start:
                start_sec = tick_to_seconds(silence_start, ppq, tempo_changes)
                end_sec = tick_to_seconds(tick, ppq, tempo_changes)
                if end_sec - start_sec >= min_seconds:
                    gaps.append((start_sec, end_sec))
            silence_start = None
        elif prev_active > 0 and active == 0:
            silence_start = tick

    # Total file length = tick of final event.
    last_tick = timeline[-1][0]
    total = tick_to_seconds(last_tick, ppq, tempo_changes)
    return gaps, total


def fmt_time(s: float) -> str:
    minutes = int(s // 60)
    secs = s - minutes * 60
    return f"{minutes:02d}:{secs:06.3f}"


def main() -> int:
    args = sys.argv[1:]
    pattern = "*.mid"
    min_sec = 1.0
    i = 0
    while i < len(args):
        a = args[i]
        if a in ("-h", "--help"):
            print(__doc__)
            return 0
        if a == "--min":
            i += 1
            if i >= len(args):
                print("--min requires a value (seconds)")
                return 2
            min_sec = float(args[i])
        elif a.startswith("-"):
            print(f"Unknown option: {a}")
            return 2
        else:
            pattern = a
        i += 1

    script_dir = os.path.dirname(os.path.abspath(__file__))
    files = sorted(glob.glob(os.path.join(script_dir, pattern)))
    if not files:
        print(f"No files matched '{pattern}' in {script_dir}")
        return 0

    print(f"Scanning {len(files)} file(s) for silent gaps >= {min_sec}s")
    print(f"  in: {script_dir}")
    print()

    flagged = 0
    clean = 0
    for path in files:
        name = os.path.basename(path)
        try:
            gaps, total = find_gaps(path, min_seconds=min_sec)
        except Exception as e:
            print(f"  {name:55s}  PARSE ERROR: {e}")
            continue

        if gaps:
            print(f"  {name:55s}  {fmt_time(total)}   {len(gaps)} GAP(S):")
            for start, end in gaps:
                dur = end - start
                print(f"      silent {fmt_time(start)} - {fmt_time(end)}  ({dur:.2f}s)")
            flagged += 1
        else:
            print(f"  {name:55s}  {fmt_time(total)}   ok")
            clean += 1

    print()
    print(f"Summary: {clean} clean, {flagged} with gaps (out of {len(files)})")
    return flagged


if __name__ == "__main__":
    sys.exit(main())
