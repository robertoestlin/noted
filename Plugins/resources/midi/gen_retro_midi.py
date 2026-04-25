"""Generate 5 original retro/chiptune-style MIDI files.

Run: python gen_retro_midi.py
Output: 5 .mid files alongside this script.

No third-party deps; emits raw Standard MIDI File bytes.
"""

from __future__ import annotations

import os
import struct
from dataclasses import dataclass, field
from typing import Iterable

PPQ = 480


def vlq(n: int) -> bytes:
    if n < 0:
        raise ValueError("delta time must be non-negative")
    if n == 0:
        return b"\x00"
    parts = [n & 0x7F]
    n >>= 7
    while n:
        parts.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(parts))


def note_on(ch: int, pitch: int, vel: int) -> bytes:
    return bytes([0x90 | (ch & 0x0F), pitch & 0x7F, vel & 0x7F])


def note_off(ch: int, pitch: int, vel: int = 0) -> bytes:
    return bytes([0x80 | (ch & 0x0F), pitch & 0x7F, vel & 0x7F])


def program_change(ch: int, program: int) -> bytes:
    return bytes([0xC0 | (ch & 0x0F), program & 0x7F])


def control_change(ch: int, controller: int, value: int) -> bytes:
    return bytes([0xB0 | (ch & 0x0F), controller & 0x7F, value & 0x7F])


def meta_tempo_bpm(bpm: float) -> bytes:
    mpq = int(round(60_000_000 / bpm))
    return bytes([0xFF, 0x51, 0x03, (mpq >> 16) & 0xFF, (mpq >> 8) & 0xFF, mpq & 0xFF])


def meta_time_signature(num: int = 4, den_pow2: int = 2) -> bytes:
    return bytes([0xFF, 0x58, 0x04, num, den_pow2, 24, 8])


def meta_track_name(name: str) -> bytes:
    text = name.encode("ascii", errors="replace")
    return bytes([0xFF, 0x03, len(text)]) + text


def meta_end_of_track() -> bytes:
    return bytes([0xFF, 0x2F, 0x00])


def build_track_chunk(events: Iterable[tuple[int, bytes]]) -> bytes:
    body = b"".join(vlq(delta) + data for delta, data in events)
    return b"MTrk" + struct.pack(">I", len(body)) + body


def build_smf(tracks: list[bytes], division: int = PPQ) -> bytes:
    fmt = 1
    header = b"MThd" + struct.pack(">IHHH", 6, fmt, len(tracks), division)
    return header + b"".join(tracks)


PITCH_CLASS = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def n(name: str) -> int:
    """Parse 'C4', 'F#5', 'Eb3' into a MIDI note number (C4 = 60)."""
    name = name.strip()
    pc = PITCH_CLASS[name[0].upper()]
    i = 1
    while i < len(name) and name[i] in ("#", "b"):
        if name[i] == "#":
            pc += 1
        else:
            pc -= 1
        i += 1
    octave = int(name[i:])
    return pc + (octave + 1) * 12


@dataclass
class Track:
    name: str
    program: int
    channel: int
    events: list[tuple[int, bytes]] = field(default_factory=list)

    def add_raw(self, abs_tick: int, data: bytes) -> None:
        self.events.append((abs_tick, data))


@dataclass
class Sequence:
    ppq: int = PPQ
    bpm: float = 120.0
    time_sig: tuple[int, int] = (4, 2)  # numerator, denominator power-of-2
    tracks: list[Track] = field(default_factory=list)

    def beats(self, n: float) -> int:
        return int(round(n * self.ppq))

    def add_track(self, name: str, program: int, channel: int) -> Track:
        t = Track(name=name, program=program, channel=channel)
        self.tracks.append(t)
        return t

    def add_note(self, track: Track, beat: float, dur: float, pitch: int, vel: int = 96) -> None:
        on = self.beats(beat)
        # Slight gap so consecutive same-pitch notes retrigger cleanly.
        off = max(on + 1, self.beats(beat + dur) - 4)
        track.add_raw(on, note_on(track.channel, pitch, vel))
        track.add_raw(off, note_off(track.channel, pitch, 0))

    def add_chord(self, track: Track, beat: float, dur: float, pitches: list[int], vel: int = 88) -> None:
        for p in pitches:
            self.add_note(track, beat, dur, p, vel)

    def play_line(self, track: Track, start: float, notes: list[tuple[str | None, float]], vel: int = 96) -> float:
        t = start
        for pitch, dur in notes:
            if pitch is not None:
                self.add_note(track, t, dur, n(pitch), vel)
            t += dur
        return t

    def add_drum(self, track: Track, beat: float, drum: int, vel: int = 110) -> None:
        on = self.beats(beat)
        off = on + 12
        track.add_raw(on, note_on(track.channel, drum, vel))
        track.add_raw(off, note_off(track.channel, drum, 0))

    def to_smf(self) -> bytes:
        # Conductor track 0: tempo, time sig, EOT.
        conductor: list[tuple[int, bytes]] = [
            (0, meta_track_name("Conductor")),
            (0, meta_time_signature(self.time_sig[0], self.time_sig[1])),
            (0, meta_tempo_bpm(self.bpm)),
        ]
        max_tick = 0
        for t in self.tracks:
            if t.events:
                max_tick = max(max_tick, max(e[0] for e in t.events))
        conductor.append((max_tick, meta_end_of_track()))
        track_bytes = [build_track_chunk(_to_delta(conductor))]

        for t in self.tracks:
            track_events: list[tuple[int, bytes]] = [
                (0, meta_track_name(t.name)),
                (0, program_change(t.channel, t.program)),
                (0, control_change(t.channel, 7, 110)),  # volume
                (0, control_change(t.channel, 10, 64)),  # pan center
            ]
            sorted_events = sorted(t.events, key=lambda x: x[0])
            track_events.extend(sorted_events)
            last_tick = sorted_events[-1][0] if sorted_events else 0
            track_events.append((last_tick, meta_end_of_track()))
            track_bytes.append(build_track_chunk(_to_delta(track_events)))

        return build_smf(track_bytes, division=self.ppq)


def _to_delta(events: list[tuple[int, bytes]]) -> list[tuple[int, bytes]]:
    """Convert (abs_tick, data) list (already sorted) to (delta, data)."""
    out = []
    prev = 0
    for abs_tick, data in events:
        delta = abs_tick - prev
        if delta < 0:
            delta = 0
        out.append((delta, data))
        prev = abs_tick
    return out


# ---------- General MIDI program numbers ----------
GM_SQUARE_LEAD = 80
GM_SAW_LEAD = 81
GM_SYNTH_BASS_1 = 38
GM_SYNTH_BASS_2 = 39
GM_OVERDRIVEN_GUITAR = 29
GM_DISTORTION_GUITAR = 30
GM_ELECTRIC_BASS_FINGER = 33
GM_ELECTRIC_BASS_PICK = 34
GM_PAD_CHOIR = 91
GM_PAD_HALO = 94
GM_TRUMPET = 56
GM_FRENCH_HORN = 60
GM_TUBA = 58
GM_STRINGS = 48
GM_GLOCKENSPIEL = 9
GM_ACOUSTIC_GRAND = 0
GM_ELECTRIC_PIANO_1 = 4
GM_VIBRAPHONE = 11
GM_NYLON_GUITAR = 24
GM_ACOUSTIC_BASS = 32
GM_LEAD_3_CALLIOPE = 82
GM_PAD_NEW_AGE = 88
GM_PAD_WARM = 89
GM_PAD_POLYSYNTH = 90
GM_FX_CRYSTAL = 98
GM_HARP = 46
GM_PAN_FLUTE = 75

# Additional drum notes (channel 10 / index 9).
PEDAL_HAT = 44
HAND_CLAP = 39
COWBELL = 56
TAMBOURINE = 54

# ---------- GM drum notes (channel 10 / index 9) ----------
KICK = 36
SNARE = 38
CLOSED_HAT = 42
OPEN_HAT = 46
CRASH = 49
RIDE = 51
TOM_LOW = 41
TOM_MID = 47
TOM_HIGH = 50

# ---------- GM programs used by the disco tunes ----------
GM_ELECTRIC_GUITAR_CLEAN = 27
GM_SLAP_BASS_1 = 36
GM_BRASS_SECTION = 61
GM_SYNTH_BRASS_1 = 62
GM_SYNTH_STRINGS_1 = 50
GM_VOICE_OOHS = 53


# =============================================================================
# 1. PIXEL QUEST - 8-bit overworld adventure (C major, 132 BPM)
# =============================================================================
def make_pixel_quest(reps: int = 2) -> bytes:
    seq = Sequence(bpm=132)
    lead = seq.add_track("Square Lead", GM_SQUARE_LEAD, 0)
    bass = seq.add_track("Bass", GM_SYNTH_BASS_1, 1)
    arp = seq.add_track("Arp", GM_SQUARE_LEAD, 2)

    # `reps` repetitions of the 8-bar phrase (= 32 beats each).
    for rep in range(reps):
        offset = rep * 32

        # Lead (8 bars). Quarter = 1 beat.
        melody = [
            # Bar 1 (C)
            ("G5", 1), ("C6", 1), ("E6", 1), ("C6", 1),
            # Bar 2 (C) - run-up
            ("D6", 0.5), ("E6", 0.5), ("F6", 0.5), ("E6", 0.5),
            ("D6", 0.5), ("C6", 0.5), ("B5", 1),
            # Bar 3 (Am)
            ("A5", 1), ("C6", 1), ("E6", 1), ("C6", 1),
            # Bar 4 (Am)
            ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("C6", 0.5),
            ("B5", 0.5), ("A5", 0.5), ("G5", 1),
            # Bar 5 (F)
            ("F5", 1), ("A5", 1), ("C6", 1), ("A5", 1),
            # Bar 6 (F)
            ("G5", 0.5), ("A5", 0.5), ("Bb5", 0.5), ("A5", 0.5),
            ("G5", 0.5), ("F5", 0.5), ("E5", 1),
            # Bar 7 (G)
            ("G5", 1), ("B5", 1), ("D6", 1), ("B5", 1),
            # Bar 8 (G->C resolve)
            ("A5", 0.5), ("B5", 0.5), ("C6", 0.5), ("B5", 0.5),
            ("A5", 0.5), ("G5", 0.5), ("C5" if rep == reps - 1 else "G5", 1),
        ]
        seq.play_line(lead, offset, melody, vel=104)

        # Bass: octave-bouncing 8th notes per chord (2 bars each).
        bass_pattern = [
            ("C2", "C3"),  # bars 1-2
            ("A1", "A2"),  # bars 3-4
            ("F1", "F2"),  # bars 5-6
            ("G1", "G2"),  # bars 7-8
        ]
        for chord_idx, (low, high) in enumerate(bass_pattern):
            for beat_offset in range(8):  # 8 eighth notes * 2 = 16 (over 2 bars = 8 beats? Actually 2 bars = 8 beats = 16 eighths)
                pass
        # Re-do correctly: 2 bars per chord = 8 beats = 16 eighth notes.
        for chord_idx, (low, high) in enumerate(bass_pattern):
            chord_start = offset + chord_idx * 8
            for i in range(16):
                pitch = low if (i % 2 == 0) else high
                seq.add_note(bass, chord_start + i * 0.5, 0.45, n(pitch), vel=92)

        # Arp (subtle higher arpeggios on bar 2, 4, 6, 8 second halves) for sparkle.
        arp_chords = {
            1: ["E5", "G5", "C6", "E6"],   # bar 2 second half
            3: ["E5", "A5", "C6", "E6"],   # bar 4 second half
            5: ["A5", "C6", "F6", "A6"],   # bar 6 second half
            7: ["D6", "G5", "B5", "D6"],   # bar 8 second half
        }
        for bar_idx, chord in arp_chords.items():
            base = offset + bar_idx * 4 + 2  # second half of the bar
            for i, p in enumerate(chord):
                seq.add_note(arp, base + i * 0.25, 0.22, n(p), vel=64)

    return seq.to_smf()


# =============================================================================
# 2. NEON ARCADE - fast shoot-'em-up (A minor, 160 BPM)
# =============================================================================
def make_neon_arcade(reps: int = 2) -> bytes:
    seq = Sequence(bpm=160)
    lead = seq.add_track("Square Lead", GM_SQUARE_LEAD, 0)
    bass = seq.add_track("Pulse Bass", GM_SYNTH_BASS_2, 1)
    drums = seq.add_track("Drums", 0, 9)

    # 16 bars (4/4) = 64 beats.
    # Chord progression: Am - F - G - E (vi-IV-V-III in C major frame).
    progression = ["Am", "F", "G", "E"]
    chord_roots = {"Am": "A", "F": "F", "G": "G", "E": "E"}
    chord_pitches = {
        "Am": ["A4", "C5", "E5"],
        "F":  ["F4", "A4", "C5"],
        "G":  ["G4", "B4", "D5"],
        "E":  ["E4", "G#4", "B4"],
    }

    # `reps` reps of 4-chord progression (4 bars each).
    for rep in range(reps):
        for chord_idx, ch in enumerate(progression):
            bar_start = rep * 16 + chord_idx * 4

            # Lead: arpeggio sixteenths + accent melody on beat 4.
            arp_notes = chord_pitches[ch]
            arp_pattern = [arp_notes[0], arp_notes[1], arp_notes[2], arp_notes[1]]
            for beat in range(3):  # beats 1,2,3 - sixteenth arpeggio
                for i, pitch in enumerate(arp_pattern):
                    seq.add_note(lead, bar_start + beat + i * 0.25, 0.22, n(pitch), vel=92)

            # Beat 4: accent melodic line based on chord
            accent = {
                "Am": [("E5", 0.25), ("A5", 0.25), ("C6", 0.25), ("E6", 0.25)],
                "F":  [("C5", 0.25), ("F5", 0.25), ("A5", 0.25), ("C6", 0.25)],
                "G":  [("D5", 0.25), ("G5", 0.25), ("B5", 0.25), ("D6", 0.25)],
                "E":  [("B4", 0.25), ("E5", 0.25), ("G#5", 0.25), ("B5", 0.25)],
            }
            seq.play_line(lead, bar_start + 3, accent[ch], vel=110)

            # Bass: pulse 8ths on the root, octaves alternating.
            root_low = n(chord_roots[ch] + "2")
            root_high = root_low + 12
            for i in range(8):
                pitch = root_low if (i % 2 == 0) else root_high
                seq.add_note(bass, bar_start + i * 0.5, 0.45, pitch, vel=100)

            # Drums: kick on 1, 3; snare 2, 4; closed hat every 8th; crash on bar 1 only.
            for beat in range(4):
                if beat in (0, 2):
                    seq.add_drum(drums, bar_start + beat, KICK, vel=115)
                if beat in (1, 3):
                    seq.add_drum(drums, bar_start + beat, SNARE, vel=110)
                seq.add_drum(drums, bar_start + beat, CLOSED_HAT, vel=80)
                seq.add_drum(drums, bar_start + beat + 0.5, CLOSED_HAT, vel=70)
            if rep == 0 and chord_idx == 0:
                seq.add_drum(drums, bar_start, CRASH, vel=110)
            if rep == reps - 1 and chord_idx == 3:
                seq.add_drum(drums, bar_start + 3.5, CRASH, vel=120)

    return seq.to_smf()


# =============================================================================
# 3. CASTLE CRAWL - mysterious dungeon (D minor, 92 BPM)
# =============================================================================
def make_castle_crawl(reps: int = 2) -> bytes:
    seq = Sequence(bpm=92)
    lead = seq.add_track("Saw Lead", GM_SAW_LEAD, 0)
    pad = seq.add_track("Choir Pad", GM_PAD_CHOIR, 1)
    bass = seq.add_track("Bass", GM_SYNTH_BASS_1, 2)
    bell = seq.add_track("Glock", GM_GLOCKENSPIEL, 3)

    # 8 bars (4/4). Chord progression: Dm - Bb - Gm - A (i - VI - iv - V).
    chords = [
        ("Dm", ["D4", "F4", "A4"], "D2"),
        ("Bb", ["Bb3", "D4", "F4"], "Bb1"),
        ("Gm", ["G3", "Bb3", "D4"], "G1"),
        ("A",  ["A3", "C#4", "E4"], "A1"),
    ]

    # `reps` reps of 4 chords (1 bar each).
    for rep in range(reps):
        for ci, (cname, ch_pitches, root) in enumerate(chords):
            start = rep * 16 + ci * 4

            # Pad: held chord across the whole bar.
            seq.add_chord(pad, start, 4, [n(p) for p in ch_pitches], vel=72)

            # Bass: half + half (root, fifth-up).
            seq.add_note(bass, start, 2, n(root), vel=88)
            fifth_offset = 7
            seq.add_note(bass, start + 2, 2, n(root) + fifth_offset, vel=80)

        # Eerie melody over the 4 bars.
        if rep == 0:
            mel = [
                ("A4", 1.5), ("F4", 0.5), ("D5", 1), ("C5", 1),     # bar 1
                ("Bb4", 1.5), ("D5", 0.5), ("F5", 1), ("D5", 1),    # bar 2
                ("G4", 1), ("Bb4", 1), ("D5", 1), ("Bb4", 1),       # bar 3
                ("A4", 2), ("E5", 1), ("C#5", 1),                   # bar 4
            ]
        else:
            mel = [
                ("D5", 1), ("F5", 1), ("A5", 1), ("F5", 1),         # bar 5
                ("D5", 1.5), ("F5", 0.5), ("A5", 2),                # bar 6
                ("G5", 1), ("F5", 1), ("D5", 1), ("Bb4", 1),        # bar 7
                ("A4", 2), ("D5", 2),                               # bar 8 (resolve)
            ]
        seq.play_line(lead, rep * 16, mel, vel=96)

        # Sparse glockenspiel sparkle (one-shot accent during the final rep).
        if rep == reps - 1:
            seq.add_note(bell, 14, 0.5, n("D6"), vel=80)
            seq.add_note(bell, 14.5, 0.5, n("F6"), vel=72)
            seq.add_note(bell, 15, 1, n("A6"), vel=88)

    return seq.to_smf()


# =============================================================================
# 4. BOSS SHOWDOWN - intense battle (E minor, 168 BPM)
# =============================================================================
def make_boss_showdown(reps: int = 2) -> bytes:
    seq = Sequence(bpm=168)
    lead = seq.add_track("Lead Riff", GM_DISTORTION_GUITAR, 0)
    chunk = seq.add_track("Power Chords", GM_OVERDRIVEN_GUITAR, 1)
    bass = seq.add_track("Bass", GM_ELECTRIC_BASS_PICK, 2)
    drums = seq.add_track("Drums", 0, 9)

    # 16 bars. Chord progression: Em - C - G - D (i - VI - III - VII), classic.
    progression = [
        ("Em", "E", ["E3", "B3", "E4"]),
        ("C",  "C", ["C3", "G3", "C4"]),
        ("G",  "G", ["G2", "D3", "G3"]),
        ("D",  "D", ["D3", "A3", "D4"]),
    ]

    for rep in range(reps):
        for ci, (cname, root, power) in enumerate(progression):
            bar_start = rep * 16 + ci * 4

            # Power chord stabs: 8ths on beat 1, 1.5, 2.5, 3, 4 (driving).
            stab_beats = [0, 0.5, 1.5, 2, 2.75, 3, 3.5]
            for sb in stab_beats:
                seq.add_chord(chunk, bar_start + sb, 0.35, [n(p) for p in power], vel=104)

            # Bass: pumping 8ths on root.
            for i in range(8):
                seq.add_note(bass, bar_start + i * 0.5, 0.42, n(root + "2"), vel=110)

            # Drums: kick on 1, 1.5, 3; snare 2, 4; closed hat 8ths.
            kicks = [0, 0.5, 2]
            for k in kicks:
                seq.add_drum(drums, bar_start + k, KICK, vel=120)
            for s in [1, 3]:
                seq.add_drum(drums, bar_start + s, SNARE, vel=115)
            for i in range(8):
                seq.add_drum(drums, bar_start + i * 0.5, CLOSED_HAT, vel=78)
            if ci == 0:
                seq.add_drum(drums, bar_start, CRASH, vel=125)

        # Lead riff over 16 bars (different per rep).
        if rep == 0:
            riff = [
                # Em (bar 1)
                ("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 0.5),
                ("D6", 0.5), ("B5", 0.5), ("G5", 0.5), ("E5", 0.5),
                # C (bar 2)
                ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5),
                ("B5", 0.5), ("G5", 0.5), ("E5", 0.5), ("C5", 0.5),
                # G (bar 3)
                ("G4", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 0.5),
                ("F#5", 0.5), ("D5", 0.5), ("B4", 0.5), ("G4", 0.5),
                # D (bar 4)
                ("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 0.5),
                ("C6", 0.25), ("B5", 0.25), ("A5", 0.5), ("F#5", 1),
            ]
        else:
            # Same shape but with a soaring climax in the last bar.
            riff = [
                # Em
                ("E5", 1), ("G5", 0.5), ("B5", 0.5),
                ("E6", 1), ("D6", 0.5), ("B5", 0.5),
                # C
                ("C5", 1), ("E5", 0.5), ("G5", 0.5),
                ("C6", 1), ("B5", 0.5), ("G5", 0.5),
                # G
                ("G4", 1), ("B4", 0.5), ("D5", 0.5),
                ("G5", 1), ("F#5", 0.5), ("D5", 0.5),
                # D - rapid ascent
                ("D5", 0.25), ("F#5", 0.25), ("A5", 0.25), ("D6", 0.25),
                ("F#6", 0.25), ("A6", 0.25), ("B6", 0.5),
                ("E7", 2),
            ]
        seq.play_line(lead, rep * 16, riff, vel=115)

    return seq.to_smf()


# =============================================================================
# 5. VICTORY FANFARE - short triumphant jingle (C major, 120 BPM)
# =============================================================================
def make_victory_fanfare(reps: int = 1) -> bytes:
    seq = Sequence(bpm=120)
    trumpet = seq.add_track("Trumpet", GM_TRUMPET, 0)
    horn = seq.add_track("French Horn", GM_FRENCH_HORN, 1)
    tuba = seq.add_track("Tuba", GM_TUBA, 2)
    drums = seq.add_track("Drums", 0, 9)

    # 8 bars (4/4) = 32 beats per rep. Classic triadic fanfare.

    trumpet_line = [
        # Bar 1: triad announce
        ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5),
        ("E6", 0.5), ("G6", 0.5), ("E6", 1),
        # Bar 2: descending answer
        ("D6", 0.5), ("C6", 0.5), ("B5", 0.5), ("A5", 0.5),
        ("G5", 0.5), ("F5", 0.5), ("E5", 1),
        # Bar 3: pick up to climax
        ("G5", 0.5), ("C6", 0.5), ("E6", 0.5), ("G6", 0.5),
        ("C7", 2),
        # Bar 4: hold + flourish
        ("B6", 0.5), ("C7", 1.5), ("G6", 1), ("E6", 1),
        # Bar 5: restate (V chord)
        ("D5", 0.5), ("G5", 0.5), ("B5", 0.5), ("D6", 0.5),
        ("F6", 0.5), ("D6", 0.5), ("B5", 1),
        # Bar 6: turnaround
        ("C6", 0.5), ("D6", 0.5), ("E6", 0.5), ("F6", 0.5),
        ("G6", 2),
        # Bar 7: final ascent
        ("E6", 0.5), ("F6", 0.5), ("G6", 0.5), ("A6", 0.5),
        ("B6", 0.5), ("C7", 0.5), ("D7", 1),
        # Bar 8: resolve
        ("E7", 2), ("C7", 2),
    ]

    horn_line = [
        # Bar 1
        ("E4", 0.5), ("G4", 0.5), ("C5", 0.5), ("E5", 0.5),
        ("G5", 0.5), ("C6", 0.5), ("G5", 1),
        # Bar 2
        ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5),
        ("B4", 0.5), ("A4", 0.5), ("G4", 1),
        # Bar 3
        ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("E6", 0.5),
        ("G6", 2),
        # Bar 4
        ("F6", 0.5), ("G6", 1.5), ("E6", 1), ("C6", 1),
        # Bar 5 (V chord)
        ("B4", 0.5), ("D5", 0.5), ("G5", 0.5), ("B5", 0.5),
        ("D6", 0.5), ("B5", 0.5), ("G5", 1),
        # Bar 6
        ("A5", 0.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5),
        ("E6", 2),
        # Bar 7
        ("C6", 0.5), ("D6", 0.5), ("E6", 0.5), ("F6", 0.5),
        ("G6", 0.5), ("A6", 0.5), ("B6", 1),
        # Bar 8
        ("C7", 2), ("E6", 2),
    ]

    tuba_line = [
        ("C2", 2), ("C2", 2),     # bar 1: I
        ("F2", 2), ("F2", 2),     # bar 2: IV
        ("C2", 2), ("E2", 2),     # bar 3
        ("F2", 2), ("G2", 2),     # bar 4
        ("G2", 2), ("G2", 2),     # bar 5: V
        ("C2", 2), ("C2", 2),     # bar 6
        ("F2", 2), ("G2", 2),     # bar 7
        ("C2", 4),                # bar 8
    ]

    for rep in range(reps):
        offset = rep * 32
        seq.play_line(trumpet, offset, trumpet_line, vel=110)
        seq.play_line(horn, offset, horn_line, vel=88)
        seq.play_line(tuba, offset, tuba_line, vel=100)

        # Drums: timpani-like rolls + crashes per rep.
        seq.add_drum(drums, offset + 0, CRASH, vel=120)
        seq.add_drum(drums, offset + 0, KICK, vel=110)
        seq.add_drum(drums, offset + 12.5, TOM_LOW, vel=105)
        seq.add_drum(drums, offset + 12.75, TOM_MID, vel=108)
        seq.add_drum(drums, offset + 13, TOM_HIGH, vel=112)
        seq.add_drum(drums, offset + 13.25, TOM_HIGH, vel=112)
        seq.add_drum(drums, offset + 13.5, CRASH, vel=118)
        seq.add_drum(drums, offset + 13.5, KICK, vel=110)
        seq.add_drum(drums, offset + 28, CRASH, vel=125)
        seq.add_drum(drums, offset + 28, KICK, vel=115)
        seq.add_drum(drums, offset + 30, CRASH, vel=125)
        seq.add_drum(drums, offset + 30, KICK, vel=115)

    return seq.to_smf()


# =============================================================================
# 6. LO-FI LOOPS - mellow boom-bap for coding sessions (78 BPM, A minor)
# =============================================================================
def make_lofi_loops(reps: int = 6) -> bytes:
    seq = Sequence(bpm=78)
    keys = seq.add_track("Rhodes", GM_ELECTRIC_PIANO_1, 0)
    bass = seq.add_track("Bass", GM_ELECTRIC_BASS_FINGER, 1)
    drums = seq.add_track("Drums", 0, 9)
    pad = seq.add_track("Soft Pad", GM_PAD_WARM, 2)

    # Am7 - Fmaj7 - Cmaj7 - G7, 2 bars per chord = 8 bars per cycle (~24.6s).
    progression = [
        ("Am7",   "A1", ["A3", "C4",  "E4", "G4"]),
        ("Fmaj7", "F1", ["F3", "A3",  "C4", "E4"]),
        ("Cmaj7", "C2", ["C3", "E3",  "G3", "B3"]),
        ("G7",    "G1", ["G3", "B3",  "D4", "F4"]),
    ]

    for rep in range(reps):
        for ci, (cname, root, ch_pitches) in enumerate(progression):
            base = rep * 32 + ci * 8  # 8 beats per chord (2 bars)

            # Pad: warm sustained chord an octave below the Rhodes voicing.
            seq.add_chord(pad, base, 8, [n(p) - 12 for p in ch_pitches], vel=36)

            # Rhodes: chord on beat 1 (full bar), softer re-strike on the "and" of 4.
            seq.add_chord(keys, base, 4, [n(p) for p in ch_pitches], vel=64)
            seq.add_chord(keys, base + 4.5, 3.5, [n(p) for p in ch_pitches], vel=52)

            # Bass: root on 1, ghost octave-up on the "and" of 4, fifth on bar 2.
            seq.add_note(bass, base, 2.5, n(root), vel=78)
            seq.add_note(bass, base + 3.5, 0.5, n(root) + 12, vel=66)
            seq.add_note(bass, base + 5, 2, n(root) + 7, vel=72)

            # Drums: chill boom-bap pattern, soft offbeat hats.
            for bar in range(2):
                bs = base + bar * 4
                seq.add_drum(drums, bs + 0, KICK, vel=84)
                seq.add_drum(drums, bs + 2, SNARE, vel=66)
                if bar == 1:
                    seq.add_drum(drums, bs + 3.5, KICK, vel=70)  # syncopated push
                seq.add_drum(drums, bs + 1, CLOSED_HAT, vel=40)
                seq.add_drum(drums, bs + 1.5, CLOSED_HAT, vel=32)
                seq.add_drum(drums, bs + 3, CLOSED_HAT, vel=40)
                seq.add_drum(drums, bs + 3.5, CLOSED_HAT, vel=32)

    return seq.to_smf()


# =============================================================================
# 7. AMBIENT DRIFT - slow ambient pad atmosphere, no drums (60 BPM, D major)
# =============================================================================
def make_ambient_drift(reps: int = 2) -> bytes:
    seq = Sequence(bpm=60)
    pad = seq.add_track("Warm Pad", GM_PAD_WARM, 0)
    bell = seq.add_track("Crystal", GM_FX_CRYSTAL, 1)
    sub = seq.add_track("Sub", GM_PAD_NEW_AGE, 2)
    strings = seq.add_track("Strings", GM_STRINGS, 3)

    # Dmaj7 - Amaj7 - Bm7 - Gmaj7, 4 bars per chord = 16 bars per cycle (64s).
    progression = [
        ("Dmaj7",  "D2",  ["D4",  "F#4", "A4",  "C#5"]),
        ("Amaj7",  "A1",  ["A3",  "C#4", "E4",  "G#4"]),
        ("Bm7",    "B1",  ["B3",  "D4",  "F#4", "A4"]),
        ("Gmaj7",  "G1",  ["G3",  "B3",  "D4",  "F#4"]),
    ]

    for rep in range(reps):
        for ci, (cname, root, ch_pitches) in enumerate(progression):
            base = rep * 64 + ci * 16  # 16 beats per chord

            # Pad: held chord across the full 4 bars.
            seq.add_chord(pad, base, 16, [n(p) for p in ch_pitches], vel=58)

            # Strings: doubled an octave up, even softer.
            seq.add_chord(strings, base, 16, [n(p) + 12 for p in ch_pitches[:3]], vel=42)

            # Sub: long held root.
            seq.add_note(sub, base, 16, n(root), vel=58)

            # Crystal bell: sparse arpeggio sprinkled across the chord.
            arp_indices = [0, 2, 1, 3, 2, 0, 3, 1]
            for i, idx in enumerate(arp_indices):
                seq.add_note(bell, base + i * 2, 1.8, n(ch_pitches[idx]) + 12, vel=44)

    return seq.to_smf()


# =============================================================================
# 8. COFFEE SHOP JAZZ - relaxed jazz turnaround (88 BPM, F major)
# =============================================================================
def make_coffee_shop_jazz(reps: int = 7) -> bytes:
    seq = Sequence(bpm=88)
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 0)
    bass = seq.add_track("Acoustic Bass", GM_ACOUSTIC_BASS, 1)
    drums = seq.add_track("Drums", 0, 9)
    vibes = seq.add_track("Vibes", GM_VIBRAPHONE, 2)

    # ii-V-I-vi turnaround in F major: Gm7 - C7 - Fmaj7 - Dm7
    # 2 bars per chord, 8 bars per cycle (~21.8s at 88 BPM).
    chords = [
        # name,    root,  walking-bass (4 quarters),     piano voicing
        ("Gm7",   "G2", ["G2",  "Bb2", "D3", "F3"], ["F3",  "Bb3", "D4", "G4"]),
        ("C7",    "C2", ["C2",  "E2",  "G2", "Bb2"], ["E3",  "G3",  "Bb3", "C4"]),
        ("Fmaj7", "F2", ["F2",  "A2",  "C3", "E3"], ["E3",  "A3",  "C4",  "F4"]),
        ("Dm7",   "D2", ["D2",  "F2",  "A2", "C3"], ["F3",  "A3",  "C4",  "D4"]),
    ]

    for rep in range(reps):
        for ci, (cname, root, walk, voicing) in enumerate(chords):
            base = rep * 32 + ci * 8  # 2 bars per chord

            for bar in range(2):
                bs = base + bar * 4

                # Walking bass: quarter notes.
                for i, p in enumerate(walk):
                    seq.add_note(bass, bs + i, 0.92, n(p), vel=78)

                # Piano: comp on offbeats (Charleston-style).
                if bar == 0:
                    seq.add_chord(piano, bs, 1.4, [n(p) for p in voicing], vel=68)
                    seq.add_chord(piano, bs + 2.5, 1.4, [n(p) for p in voicing], vel=58)
                else:
                    seq.add_chord(piano, bs + 1.5, 1.5, [n(p) for p in voicing], vel=62)
                    seq.add_chord(piano, bs + 3.5, 0.4, [n(p) for p in voicing], vel=52)

                # Drums: jazz ride pattern, soft snare backbeat, sparse kick.
                seq.add_drum(drums, bs + 0, RIDE, vel=58)
                seq.add_drum(drums, bs + 1.5, RIDE, vel=44)
                seq.add_drum(drums, bs + 2, RIDE, vel=54)
                seq.add_drum(drums, bs + 3.5, RIDE, vel=44)
                seq.add_drum(drums, bs + 1, PEDAL_HAT, vel=46)
                seq.add_drum(drums, bs + 3, PEDAL_HAT, vel=46)
                seq.add_drum(drums, bs + 2, SNARE, vel=44)
                if bar == 0:
                    seq.add_drum(drums, bs + 0, KICK, vel=58)

        # Vibraphone: sparse melodic phrase every other cycle (4-bar call).
        if rep % 2 == 1:
            phrase = [
                ("F4", 1), ("A4", 0.5), ("C5", 0.5), ("F5", 2),
                ("E5", 1), ("D5", 1), ("C5", 2),
                ("Bb4", 0.5), ("A4", 0.5), ("G4", 1), ("F4", 2),
                (None, 4),  # rest the rest of the cycle
            ]
            seq.play_line(vibes, rep * 32, phrase, vel=66)

    return seq.to_smf()


# =============================================================================
# 9. SYNTHWAVE CRUISE - laid-back retro synth groove (95 BPM, E minor)
# =============================================================================
def make_synthwave_cruise(reps: int = 7) -> bytes:
    seq = Sequence(bpm=95)
    lead = seq.add_track("Smooth Lead", GM_LEAD_3_CALLIOPE, 0)
    bass = seq.add_track("Synth Bass", GM_SYNTH_BASS_1, 1)
    drums = seq.add_track("Drums", 0, 9)
    pad = seq.add_track("Pad", GM_PAD_POLYSYNTH, 2)
    arp = seq.add_track("Arp", GM_SAW_LEAD, 3)

    # Em - Cmaj7 - G - D, 2 bars per chord = 8 bars per cycle (~20.2s).
    progression = [
        ("Em",    "E2", ["E3",  "G3",  "B3"]),
        ("Cmaj7", "C2", ["C3",  "E3",  "G3",  "B3"]),
        ("G",     "G1", ["G2",  "B2",  "D3"]),
        ("D",     "D2", ["D3",  "F#3", "A3"]),
    ]

    for rep in range(reps):
        for ci, (cname, root, ch_pitches) in enumerate(progression):
            base = rep * 32 + ci * 8

            # Pad: held chord softly.
            seq.add_chord(pad, base, 8, [n(p) for p in ch_pitches], vel=50)

            # Bass: 8th note pulse on root, octave-bouncing every 2 8ths.
            for i in range(16):
                pitch = n(root) if (i % 4 < 2) else n(root) + 12
                seq.add_note(bass, base + i * 0.5, 0.42, pitch, vel=78)

            # Drums: kick on 1 + 3 (no four-on-floor for chill feel),
            # snare on 2 + 4, soft 8th hats.
            for bar in range(2):
                bs = base + bar * 4
                seq.add_drum(drums, bs + 0, KICK, vel=80)
                seq.add_drum(drums, bs + 2, KICK, vel=72)
                seq.add_drum(drums, bs + 1, SNARE, vel=64)
                seq.add_drum(drums, bs + 3, SNARE, vel=64)
                for h in range(8):
                    seq.add_drum(drums, bs + h * 0.5, CLOSED_HAT, vel=42)

            # Arp: 16th note arpeggio softly under the chord.
            arp_pitches = ch_pitches + ch_pitches[::-1][1:-1]
            if not arp_pitches:
                arp_pitches = ch_pitches
            for i in range(32):
                p = arp_pitches[i % len(arp_pitches)]
                seq.add_note(arp, base + i * 0.25, 0.22, n(p) + 12, vel=38)

        # Lead melody appears every other cycle.
        if rep % 2 == 1:
            mel = [
                ("E5", 2),    ("G5", 1),  ("B5", 1),     # Em
                ("C5", 1),    ("G5", 1),  ("E5", 2),     # Cmaj7
                ("D5", 1),    ("B4", 1),  ("G4", 2),     # G
                ("F#5", 1),   ("D5", 1),  ("A4", 2),     # D
            ]
            seq.play_line(lead, rep * 32, mel, vel=80)

    return seq.to_smf()


# =============================================================================
# 10. FOREST MEDITATION - peaceful nylon guitar + flute, no drums (72 BPM, A maj)
# =============================================================================
def make_forest_meditation(reps: int = 5) -> bytes:
    seq = Sequence(bpm=72)
    flute = seq.add_track("Pan Flute", GM_PAN_FLUTE, 0)
    guitar = seq.add_track("Nylon Guitar", GM_NYLON_GUITAR, 1)
    strings = seq.add_track("Strings", GM_STRINGS, 2)
    harp = seq.add_track("Harp", GM_HARP, 3)

    # A - E - F#m - D, 2 bars per chord = 8 bars per cycle (~26.7s).
    progression = [
        ("A",   "A2",  ["A3",  "C#4", "E4"]),
        ("E",   "E2",  ["E3",  "G#3", "B3"]),
        ("F#m", "F#2", ["F#3", "A3",  "C#4"]),
        ("D",   "D2",  ["D3",  "F#3", "A3"]),
    ]

    flute_lines = [
        # Melody A (used on most cycles)
        [
            ("A4", 2), ("E5", 2),                       # bar 1 over A
            ("C#5", 1.5), ("D5", 0.5), ("E5", 2),       # bar 2
            ("B4", 1.5), ("G#4", 0.5), ("E4", 2),       # bar 3 over E
            ("G#4", 1), ("B4", 1), ("E5", 2),           # bar 4
            ("F#5", 2), ("E5", 2),                      # bar 5 over F#m
            ("D5", 1), ("C#5", 1), ("F#5", 2),          # bar 6
            ("F#5", 1), ("E5", 1), ("D5", 2),           # bar 7 over D
            ("F#5", 2), ("A5", 2),                      # bar 8
        ],
        # Melody B (variation, used on cycle 3 / "bridge")
        [
            ("E5", 1), ("F#5", 1), ("A5", 2),
            ("G#5", 1), ("F#5", 1), ("E5", 2),
            ("B4", 1), ("E5", 1), ("G#5", 2),
            ("F#5", 1.5), ("E5", 0.5), ("D5", 2),
            ("C#5", 1), ("D5", 1), ("E5", 2),
            ("F#5", 1), ("A5", 1), ("E5", 2),
            ("D5", 2), ("F#5", 2),
            ("E5", 4),
        ],
    ]

    for rep in range(reps):
        for ci, (cname, root, ch_pitches) in enumerate(progression):
            base = rep * 32 + ci * 8

            # Strings: hold the chord softly.
            seq.add_chord(strings, base, 8, [n(p) for p in ch_pitches], vel=52)

            # Nylon guitar: Travis-picking arpeggio, eighth notes for 2 bars.
            arp = [n(root), n(ch_pitches[1]), n(ch_pitches[2]), n(ch_pitches[1])]
            for bar in range(2):
                bs = base + bar * 4
                for i in range(8):
                    seq.add_note(guitar, bs + i * 0.5, 0.45, arp[i % len(arp)], vel=58)

            # Harp: a gentle ascending sparkle on the first bar of each chord.
            seq.add_note(harp, base + 6, 0.5, n(ch_pitches[0]) + 12, vel=50)
            seq.add_note(harp, base + 6.5, 0.5, n(ch_pitches[1]) + 12, vel=50)
            seq.add_note(harp, base + 7, 1, n(ch_pitches[2]) + 12, vel=54)

        # Flute melody: alternates between A and B (with a quiet middle breath).
        if rep == 2:
            flute_choice = 1
        elif rep == reps - 1:
            flute_choice = 0
        else:
            flute_choice = 0
        # Drop the flute on the second cycle for breathing room.
        if rep != 1:
            seq.play_line(flute, rep * 32, flute_lines[flute_choice], vel=68)

    return seq.to_smf()


# =============================================================================
# 11. CLASSICAL ETUDE - Bach-inspired prelude in C major (92 BPM)
# =============================================================================
def make_classical_etude(reps: int = 6) -> bytes:
    seq = Sequence(bpm=92)
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 0)

    # 8-bar Pachelbel-ish progression: C - G/B - Am - Em - F - C - F - G7
    chords = [
        ("C",   "C2", ["C4", "E4", "G4", "C5"]),
        ("G/B", "B1", ["B3", "D4", "G4", "B4"]),
        ("Am",  "A1", ["A3", "C4", "E4", "A4"]),
        ("Em",  "E2", ["E3", "G3", "B3", "E4"]),
        ("F",   "F1", ["F3", "A3", "C4", "F4"]),
        ("C",   "C2", ["C4", "E4", "G4", "C5"]),
        ("F",   "F1", ["F3", "A3", "C4", "F4"]),
        ("G7",  "G1", ["G3", "B3", "D4", "F4"]),
    ]

    # Up-down arpeggio shape over 4 chord tones, expanded to 8 eighths.
    arp_idx = [0, 1, 2, 3, 2, 1, 0, 1]

    for rep in range(reps):
        for ci, (cname, bass, tones) in enumerate(chords):
            bar_start = rep * 32 + ci * 4

            # LH: bass tone held across the bar.
            seq.add_note(piano, bar_start, 4, n(bass), vel=72)
            # Re-strike on beat 3 for a soft pulse.
            seq.add_note(piano, bar_start + 2, 2, n(bass) + 7, vel=58)

            # RH: 8 eighth-note arpeggio.
            for i, idx in enumerate(arp_idx):
                seq.add_note(piano, bar_start + i * 0.5, 0.45, n(tones[idx]), vel=70)

        # On the last rep, end with a tonic resolution chord.
        if rep == reps - 1:
            final = rep * 32 + 32  # one beat past the last bar
            seq.add_chord(piano, final, 4, [n("C3"), n("E3"), n("G3"), n("C4"), n("E4")], vel=78)
            seq.add_note(piano, final, 4, n("C2"), vel=80)

    return seq.to_smf()


# =============================================================================
# 12. ROMANTIC NOCTURNE - Chopin-style lyrical piece (60 BPM, Bb major)
# =============================================================================
def make_romantic_nocturne(reps: int = 4) -> bytes:
    seq = Sequence(bpm=60)
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 0)

    # 8-bar phrase in Bb major: Bb - Gm - Eb - F - Bb - Eb - Cm - F7
    chords = [
        # name, LH arpeggio (8 eighths over 1 bar): root_low, 5th, oct_root, 3rd, 5th_up, 3rd, oct_root, 5th
        ("Bb",  ["Bb1", "F2",  "Bb2", "D3",  "F3",  "D3",  "Bb2", "F2"]),
        ("Gm",  ["G1",  "D2",  "G2",  "Bb2", "D3",  "Bb2", "G2",  "D2"]),
        ("Eb",  ["Eb1", "Bb1", "Eb2", "G2",  "Bb2", "G2",  "Eb2", "Bb1"]),
        ("F",   ["F1",  "C2",  "F2",  "A2",  "C3",  "A2",  "F2",  "C2"]),
        ("Bb",  ["Bb1", "F2",  "Bb2", "D3",  "F3",  "D3",  "Bb2", "F2"]),
        ("Eb",  ["Eb1", "Bb1", "Eb2", "G2",  "Bb2", "G2",  "Eb2", "Bb1"]),
        ("Cm",  ["C2",  "G2",  "C3",  "Eb3", "G3",  "Eb3", "C3",  "G2"]),
        ("F7",  ["F1",  "C2",  "F2",  "A2",  "Eb3", "A2",  "F2",  "C2"]),
    ]

    # Lyrical 8-bar RH melody.
    melody = [
        # Bar 1 (Bb): F4 half + Bb4 quarter + D5 quarter
        ("F4", 2),    ("Bb4", 1),   ("D5", 1),
        # Bar 2 (Gm): D5 dotted-q + C5 e + Bb4 half
        ("D5", 1.5),  ("C5", 0.5),  ("Bb4", 2),
        # Bar 3 (Eb): G4 quarter + Bb4 quarter + Eb5 half
        ("G4", 1),    ("Bb4", 1),   ("Eb5", 2),
        # Bar 4 (F):  C5 quarter + Bb4 quarter + A4 quarter + G4 quarter
        ("C5", 1),    ("Bb4", 1),   ("A4", 1),    ("G4", 1),
        # Bar 5 (Bb): F5 half + D5 quarter + Bb4 quarter
        ("F5", 2),    ("D5", 1),    ("Bb4", 1),
        # Bar 6 (Eb): G4 e + Ab4 e + Bb4 quarter + Eb5 half
        ("G4", 0.5),  ("Ab4", 0.5), ("Bb4", 1),   ("Eb5", 2),
        # Bar 7 (Cm): G4 quarter + C5 quarter + Eb5 quarter + G5 quarter
        ("G4", 1),    ("C5", 1),    ("Eb5", 1),   ("G5", 1),
        # Bar 8 (F7): F5 quarter + Eb5 quarter + D5 quarter + C5 quarter
        ("F5", 1),    ("Eb5", 1),   ("D5", 1),    ("C5", 1),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # LH arpeggio: 8 eighths per bar, 8 bars per rep.
        for ci, (_, lh_arp) in enumerate(chords):
            bar_start = rep_start + ci * 4
            for i, p in enumerate(lh_arp):
                # Slightly accent the bass note on beat 1.
                v = 68 if i == 0 else 56
                seq.add_note(piano, bar_start + i * 0.5, 0.45, n(p), v)

        # RH melody.
        seq.play_line(piano, rep_start, melody, vel=78)

        # Final-rep tonic resolution.
        if rep == reps - 1:
            final = rep_start + 32
            seq.add_chord(piano, final, 4, [n("Bb3"), n("D4"), n("F4"), n("Bb4")], vel=70)
            seq.add_note(piano, final, 4, n("Bb1"), vel=72)

    return seq.to_smf()


# =============================================================================
# 13. RAGTIME STRIDE - Joplin-style stride piano (105 BPM, C major)
# =============================================================================
def make_ragtime_stride(reps: int = 7) -> bytes:
    seq = Sequence(bpm=105)
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 0)

    # 8-bar progression: C - G7 - C - C7 - F - C - G7 - C
    bars = [
        # name, low_bass_a (beat 1), low_bass_b (beat 3), chord (beats 2 and 4)
        ("C",   "C2", "G2", ["E3",  "G3",  "C4"]),
        ("G7",  "G1", "D2", ["F3",  "B3",  "D4"]),
        ("C",   "C2", "G2", ["E3",  "G3",  "C4"]),
        ("C7",  "C2", "G2", ["E3",  "G3",  "Bb3"]),
        ("F",   "F1", "C2", ["A3",  "C4",  "F4"]),
        ("C",   "C2", "G2", ["E3",  "G3",  "C4"]),
        ("G7",  "G1", "D2", ["F3",  "B3",  "D4"]),
        ("C",   "C2", "G2", ["E3",  "G3",  "C4"]),
    ]

    # Syncopated RH melody (8 bars). Mix of e and dotted-e patterns for ragtime feel.
    melody = [
        # Bar 1 (C)
        ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), ("E5", 0.5), ("C5", 1),
        # Bar 2 (G7)
        ("D5", 0.75), ("F5", 0.25), ("D5", 0.5), ("B4", 0.5), ("D5", 1), ("G4", 1),
        # Bar 3 (C)
        ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("G5", 0.75), ("E5", 0.25), ("G5", 1),
        # Bar 4 (C7)
        ("E5", 0.5), ("G5", 0.5), ("Bb5", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 1),
        # Bar 5 (F)
        ("F5", 1), ("A5", 0.5), ("C6", 0.5), ("A5", 0.75), ("F5", 0.25), ("A5", 1),
        # Bar 6 (C)
        ("G5", 0.5), ("E5", 0.5), ("C5", 1), ("E5", 0.5), ("G5", 0.5), ("C6", 1),
        # Bar 7 (G7)
        ("B5", 0.5), ("D6", 0.5), ("B5", 0.5), ("G5", 0.5), ("F5", 0.75), ("D5", 0.25), ("B4", 1),
        # Bar 8 (C)
        ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("E6", 1), ("C6", 1),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # LH stride pattern.
        for bi, (_, low_a, low_b, chord) in enumerate(bars):
            bs = rep_start + bi * 4
            seq.add_note(piano, bs + 0, 0.9, n(low_a), vel=78)
            seq.add_chord(piano, bs + 1, 0.9, [n(p) for p in chord], vel=64)
            seq.add_note(piano, bs + 2, 0.9, n(low_b), vel=72)
            seq.add_chord(piano, bs + 3, 0.9, [n(p) for p in chord], vel=64)

        # RH melody.
        seq.play_line(piano, rep_start, melody, vel=82)

        # Final-rep tonic chord.
        if rep == reps - 1:
            final = rep_start + 32
            seq.add_chord(piano, final, 4, [n("C3"), n("E3"), n("G3"), n("C4"), n("E4"), n("G4")], vel=82)
            seq.add_note(piano, final, 4, n("C2"), vel=88)

    return seq.to_smf()


# =============================================================================
# 14. MINIMALIST PATTERNS - hypnotic Glass/Tiersen-style (100 BPM, A minor)
# =============================================================================
def make_minimalist_patterns(reps: int = 8) -> bytes:
    seq = Sequence(bpm=100)
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 0)

    # 8-bar descending progression: Am - Am - G - G - F - F - E - E
    cycle = [
        ("Am", "A2", ["A3", "C4", "E4"]),
        ("Am", "A2", ["A3", "C4", "E4"]),
        ("G",  "G2", ["G3", "B3", "D4"]),
        ("G",  "G2", ["G3", "B3", "D4"]),
        ("F",  "F2", ["F3", "A3", "C4"]),
        ("F",  "F2", ["F3", "A3", "C4"]),
        ("E",  "E2", ["E3", "G#3", "B3"]),
        ("E",  "E2", ["E3", "G#3", "B3"]),
    ]

    # Per-bar 8-eighth pattern derived from chord tones.
    # Pattern: [tone0, tone1, tone2, tone1+oct, tone2, tone1, tone0, tone1]
    def bar_pattern(tones):
        return [tones[0], tones[1], tones[2], tones[1], tones[2], tones[1], tones[0], tones[1]]

    # Sustained upper-voice melody added on certain reps for additive growth.
    melodies = {
        2: [("E5", 4), ("E5", 4), ("D5", 4), ("D5", 4),
            ("C5", 4), ("C5", 4), ("B4", 4), ("B4", 4)],
        4: [("A5", 2), ("E5", 2), ("G5", 2), ("D5", 2),
            ("F5", 2), ("C5", 2), ("E5", 2), ("B4", 2),
            ("A5", 2), ("E5", 2), ("G5", 2), ("D5", 2),
            ("F5", 2), ("C5", 2), ("E5", 2), ("B4", 2)],
        6: [("E5", 1), ("A5", 1), ("E5", 1), ("C5", 1),
            ("D5", 1), ("G5", 1), ("D5", 1), ("B4", 1),
            ("C5", 1), ("F5", 1), ("C5", 1), ("A4", 1),
            ("B4", 1), ("E5", 1), ("B4", 1), ("G#4", 1)] * 2,
    }

    for rep in range(reps):
        rep_start = rep * 32

        for ci, (_, bass, tones) in enumerate(cycle):
            bs = rep_start + ci * 4

            # LH: held bass for the whole bar.
            seq.add_note(piano, bs, 4, n(bass), vel=66)

            # RH: ostinato 8th-note pattern.
            pat = bar_pattern(tones)
            # Velocity dips on the "off" beats for a gentle pulse.
            for i, p in enumerate(pat):
                v = 64 if i % 4 == 0 else 56
                seq.add_note(piano, bs + i * 0.5, 0.45, n(p), v)

        # Optional upper-voice melodic line for selected reps.
        if rep in melodies:
            seq.play_line(piano, rep_start, melodies[rep], vel=66)

    return seq.to_smf()


# =============================================================================
# 15. BLUES PIANO - 12-bar blues in F (92 BPM, swing feel)
# =============================================================================
def make_blues_piano(reps: int = 4) -> bytes:
    seq = Sequence(bpm=92)
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 0)

    # 12-bar blues progression in F.
    progression = ["F", "F", "F", "F", "Bb", "Bb", "F", "F", "C7", "Bb", "F", "C7"]

    walking = {
        # Walking bass: 4 quarter notes per bar (R - 3 - 5 - 6 -> next chord)
        "F":  ["F2",  "A2",  "C3",  "D3"],
        "Bb": ["Bb2", "D3",  "F3",  "G3"],
        "C7": ["C3",  "E3",  "G3",  "A3"],
    }

    voicing = {
        # 7th-chord shell voicings for RH stabs.
        "F":  ["F3",  "A3",  "Eb4"],
        "Bb": ["Bb3", "D4",  "Ab4"],
        "C7": ["C4",  "E4",  "Bb4"],
    }

    # Bluesy RH licks (one per bar of the 12-bar form). Each list is bars 1..12.
    # Used during reps 2 and 4 for a "verse / chorus" feel; reps 1 and 3 are sparser.
    licks_main = [
        # Bar 1 (F): F-blues lick
        [("F4", 0.5), ("Ab4", 0.5), ("A4", 0.5), ("C5", 0.5), (None, 2)],
        # Bar 2 (F): bend-style (Eb to E approach)
        [(None, 1), ("Eb5", 0.5), ("F5", 0.5), ("D5", 0.5), ("C5", 0.5), (None, 1)],
        # Bar 3 (F): descending blues run
        [("C5", 0.5), ("Bb4", 0.5), ("Ab4", 0.5), ("F4", 0.5), (None, 2)],
        # Bar 4 (F): walk-up to IV
        [(None, 2), ("F4", 0.333), ("G4", 0.333), ("Ab4", 0.333), ("A4", 1)],
        # Bar 5 (Bb): IV chord lick
        [("Bb4", 0.5), ("D5", 0.5), ("F5", 1), ("D5", 0.5), ("Bb4", 0.5), (None, 1)],
        # Bar 6 (Bb): blue-note phrase
        [(None, 1), ("Db5", 0.5), ("D5", 0.5), ("F5", 0.5), ("D5", 0.5), ("Bb4", 1)],
        # Bar 7 (F): back to F lick
        [("A4", 0.5), ("C5", 0.5), ("Eb5", 0.5), ("F5", 0.5), ("Eb5", 0.5), ("C5", 0.5), (None, 1)],
        # Bar 8 (F): preparing the V
        [(None, 2), ("F4", 0.5), ("A4", 0.5), ("C5", 1)],
        # Bar 9 (C7): V chord lick
        [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("Bb5", 0.5), ("G5", 0.5), ("E5", 0.5), (None, 1)],
        # Bar 10 (Bb): IV chord lick
        [("Bb4", 0.5), ("D5", 0.5), ("F5", 0.5), ("Ab5", 0.5), ("F5", 0.5), ("D5", 0.5), (None, 1)],
        # Bar 11 (F): turnaround start
        [("A4", 0.5), ("C5", 0.5), ("Eb5", 0.5), ("F5", 0.5), ("Eb5", 0.5), ("C5", 0.5), ("A4", 0.5), ("F4", 0.5)],
        # Bar 12 (C7): turnaround tag
        [("G4", 0.5), ("Bb4", 0.5), ("E5", 0.5), ("G5", 0.5), (None, 2)],
    ]

    licks_sparse = [
        [(None, 4)],
        [(None, 2), ("C5", 0.5), ("F5", 0.5), (None, 1)],
        [(None, 4)],
        [(None, 4)],
        [(None, 2), ("F5", 0.5), ("D5", 0.5), ("Bb4", 1)],
        [(None, 4)],
        [(None, 4)],
        [(None, 3), ("F4", 0.5), ("A4", 0.5)],
        [("C5", 0.5), ("E5", 0.5), ("G5", 1), (None, 2)],
        [(None, 4)],
        [(None, 2), ("A4", 0.5), ("C5", 0.5), ("Eb5", 0.5), ("F5", 0.5)],
        [(None, 4)],
    ]

    for rep in range(reps):
        rep_start = rep * 48  # 12 bars * 4 beats
        use_main = (rep % 2 == 1)  # alternate sparse / main licks for verse/chorus feel
        licks = licks_main if use_main else licks_sparse

        for bi, ch in enumerate(progression):
            bar_start = rep_start + bi * 4

            # LH walking bass: 4 quarter notes per bar.
            for i, p in enumerate(walking[ch]):
                seq.add_note(piano, bar_start + i, 0.92, n(p), vel=78)

            # RH: chord stabs on beats 2 and 4 (offbeats for swing feel).
            seq.add_chord(piano, bar_start + 1, 0.45, [n(p) for p in voicing[ch]], vel=62)
            seq.add_chord(piano, bar_start + 3, 0.45, [n(p) for p in voicing[ch]], vel=58)

            # RH lick for this bar.
            seq.play_line(piano, bar_start, licks[bi], vel=78)

        # Final tonic stab on the very last rep.
        if rep == reps - 1:
            final = rep_start + 48
            seq.add_chord(piano, final, 3, [n("F3"), n("A3"), n("C4"), n("Eb4"), n("F4")], vel=82)
            seq.add_note(piano, final, 3, n("F2"), vel=86)

    return seq.to_smf()


# =============================================================================
# Shared disco helpers (4-on-the-floor, octave bass pulse, chicken-scratch).
# =============================================================================
def _disco_drums(seq: Sequence, drums: Track, bar_start: float,
                 with_open_hat: bool = True,
                 with_cowbell: bool = False,
                 with_clap: bool = False,
                 with_tambourine: bool = False) -> None:
    # 4-on-the-floor kick.
    for k in range(4):
        seq.add_drum(drums, bar_start + k, KICK, vel=108)
    # Snare on 2 and 4.
    seq.add_drum(drums, bar_start + 1, SNARE, vel=98)
    seq.add_drum(drums, bar_start + 3, SNARE, vel=100)
    # Closed hat on the beat, open hat on the "and" (signature disco shimmer).
    for h in range(4):
        seq.add_drum(drums, bar_start + h, CLOSED_HAT, vel=68)
        seq.add_drum(drums, bar_start + h + 0.5, OPEN_HAT if with_open_hat else CLOSED_HAT, vel=78)
    if with_clap:
        seq.add_drum(drums, bar_start + 1, HAND_CLAP, vel=88)
        seq.add_drum(drums, bar_start + 3, HAND_CLAP, vel=90)
    if with_cowbell:
        for c in (0.5, 1.5, 2.5, 3.5):
            seq.add_drum(drums, bar_start + c, COWBELL, vel=76)
    if with_tambourine:
        for t in (0.5, 1.5, 2.5, 3.5):
            seq.add_drum(drums, bar_start + t, TAMBOURINE, vel=70)


def _disco_bass_octave(seq: Sequence, bass: Track, bar_start: float,
                       root_pitch: int, beats: int = 4, vel: int = 92) -> None:
    """8th-note octave-bouncing bass: low-high-low-high...."""
    eighths = beats * 2
    for i in range(eighths):
        pitch = root_pitch if (i % 2 == 0) else root_pitch + 12
        seq.add_note(bass, bar_start + i * 0.5, 0.42, pitch, vel)


def _disco_chicken_scratch(seq: Sequence, guitar: Track, bar_start: float,
                           chord_pitches: list[int], beats: int = 4,
                           accent_idx: tuple[int, ...] = (0, 6, 8, 14)) -> None:
    """16th-note muted chord strums with a few accents (Nile Rodgers-ish)."""
    sixteenths = beats * 4
    for i in range(sixteenths):
        beat = bar_start + i * 0.25
        is_accent = i in accent_idx
        vel = 78 if is_accent else 32
        for p in chord_pitches:
            seq.add_note(guitar, beat, 0.18, p, vel)


# =============================================================================
# 16. DISCO - SATURDAY GROOVE: happy major-key disco (118 BPM, D major)
# =============================================================================
def make_disco_saturday_groove(reps: int = 8) -> bytes:
    seq = Sequence(bpm=118)
    lead = seq.add_track("Voice Lead", GM_VOICE_OOHS, 0)
    bass = seq.add_track("Disco Bass", GM_SYNTH_BASS_2, 1)
    strings = seq.add_track("Strings", GM_STRINGS, 2)
    brass = seq.add_track("Brass", GM_BRASS_SECTION, 3)
    guitar = seq.add_track("Guitar", GM_ELECTRIC_GUITAR_CLEAN, 4)
    drums = seq.add_track("Drums", 0, 9)

    # D - Bm - G - A, 2 bars per chord = 8 bars per cycle (~16.3s).
    progression = [
        ("D",  "D2",  ["D4",  "F#4", "A4",  "D5"]),
        ("Bm", "B1",  ["B3",  "D4",  "F#4", "B4"]),
        ("G",  "G1",  ["G3",  "B3",  "D4",  "G4"]),
        ("A",  "A1",  ["A3",  "C#4", "E4",  "A4"]),
    ]

    melody_a = [
        ("F#5", 1),    ("A5", 1),    ("D6", 2),
        ("F#5", 0.5),  ("E5", 0.5),  ("D5", 1),    ("F#5", 2),
        ("D5", 1),     ("F#5", 1),   ("B5", 2),
        ("A5", 0.5),   ("F#5", 0.5), ("D5", 1),    ("F#5", 2),
        ("B4", 1),     ("D5", 1),    ("G5", 2),
        ("F#5", 1),    ("E5", 1),    ("D5", 2),
        ("C#5", 1),    ("E5", 1),    ("A5", 2),
        ("G5", 1),     ("E5", 1),    ("C#5", 2),
    ]
    melody_b = [
        ("D6", 0.5),   ("F#6", 0.5), ("A6", 1),    ("F#6", 2),
        ("E6", 0.5),   ("D6", 0.5),  ("F#6", 1),   ("D6", 2),
        ("F#5", 0.5),  ("A5", 0.5),  ("D6", 1),    ("B5", 2),
        ("D6", 0.5),   ("B5", 0.5),  ("F#5", 1),   ("B5", 2),
        ("G5", 0.5),   ("B5", 0.5),  ("D6", 1),    ("G5", 2),
        ("D6", 0.5),   ("G5", 0.5),  ("B5", 1),    ("D6", 2),
        ("E5", 0.5),   ("A5", 0.5),  ("C#6", 1),   ("E6", 2),
        ("C#6", 1),    ("E6", 1),    ("A5", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # Lead drops out for the first cycle (intro), comes in afterwards.
        if rep >= 1:
            mel = melody_b if rep in (3, 5) else melody_a
            seq.play_line(lead, rep_start, mel, vel=86)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Strings: held chord across the 2 bars per chord.
            seq.add_chord(strings, chord_start, 8, [n(p) for p in ch_pitches], vel=58)

            for bar in range(2):
                bar_start = chord_start + bar * 4

                _disco_drums(seq, drums, bar_start, with_tambourine=(rep >= 2))
                _disco_bass_octave(seq, bass, bar_start, n(root) + 12)
                _disco_chicken_scratch(seq, guitar, bar_start, [n(p) for p in ch_pitches[:3]])

                # Brass stab on the "and" of beat 4 (push into next bar).
                stab_pitches = [n(p) + 12 for p in ch_pitches[:3]]
                seq.add_chord(brass, bar_start + 3.5, 0.45, stab_pitches, vel=82)

    return seq.to_smf()


# =============================================================================
# 17. DISCO - NIGHT FEVER: minor-key disco anthem (116 BPM, F# minor)
# =============================================================================
def make_disco_night_fever(reps: int = 8) -> bytes:
    seq = Sequence(bpm=116)
    lead = seq.add_track("Synth Lead", GM_LEAD_3_CALLIOPE, 0)
    bass = seq.add_track("Bass", GM_SYNTH_BASS_2, 1)
    strings = seq.add_track("Strings", GM_STRINGS, 2)
    brass = seq.add_track("Brass", GM_BRASS_SECTION, 3)
    guitar = seq.add_track("Guitar", GM_ELECTRIC_GUITAR_CLEAN, 4)
    drums = seq.add_track("Drums", 0, 9)

    # F#m - D - E - A (i-VI-VII-III), 2 bars each = 8-bar cycle (~16.6s).
    progression = [
        ("F#m", "F#1", ["F#3", "A3",  "C#4", "F#4"]),
        ("D",   "D1",  ["D3",  "F#3", "A3",  "D4"]),
        ("E",   "E1",  ["E3",  "G#3", "B3",  "E4"]),
        ("A",   "A1",  ["A3",  "C#4", "E4",  "A4"]),
    ]

    falsetto = [
        # Bar 1-2 (F#m): soaring high lead
        ("C#6", 1),    ("F#6", 2),   ("E6", 1),
        ("C#6", 0.5),  ("E6", 0.5),  ("F#6", 1),  ("A6", 2),
        # Bar 3-4 (D)
        ("F#6", 1),    ("D6", 1),    ("A6", 2),
        ("G6", 0.5),   ("F#6", 0.5), ("E6", 1),   ("D6", 2),
        # Bar 5-6 (E)
        ("E6", 1),     ("G#6", 1),   ("B6", 2),
        ("A6", 0.5),   ("G#6", 0.5), ("F#6", 1),  ("E6", 2),
        # Bar 7-8 (A)
        ("A5", 1),     ("C#6", 1),   ("E6", 2),
        ("D6", 1),     ("C#6", 1),   ("A5", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(lead, rep_start, falsetto, vel=82)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Strings: doubled (low + octave up) for that lush disco-string sound.
            seq.add_chord(strings, chord_start, 8, [n(p) for p in ch_pitches], vel=62)
            seq.add_chord(strings, chord_start, 8, [n(p) + 12 for p in ch_pitches[:3]], vel=46)

            for bar in range(2):
                bar_start = chord_start + bar * 4

                _disco_drums(seq, drums, bar_start, with_clap=(rep >= 2))
                _disco_bass_octave(seq, bass, bar_start, n(root) + 12)
                _disco_chicken_scratch(seq, guitar, bar_start, [n(p) for p in ch_pitches[:3]])

                # Brass: two stabs per bar (beats 1 and 3.5).
                stab = [n(p) + 12 for p in ch_pitches[:3]]
                seq.add_chord(brass, bar_start + 0, 0.4, stab, vel=78)
                seq.add_chord(brass, bar_start + 3.5, 0.4, stab, vel=82)

    return seq.to_smf()


# =============================================================================
# 18. DISCO - FUNKY STRUT: Nile Rodgers-style funk-disco (108 BPM, E minor)
# =============================================================================
def make_disco_funky_strut(reps: int = 7) -> bytes:
    seq = Sequence(bpm=108)
    lead = seq.add_track("Brass Lead", GM_SYNTH_BRASS_1, 0)
    bass = seq.add_track("Slap Bass", GM_SLAP_BASS_1, 1)
    guitar = seq.add_track("Funk Guitar", GM_ELECTRIC_GUITAR_CLEAN, 2)
    keys = seq.add_track("Clavinet", 7, 3)  # GM 7 = Clavinet, super funky
    drums = seq.add_track("Drums", 0, 9)

    # Em7 - A7 - Dmaj7 - G - 2 bars per chord = 8-bar cycle (~17.8s).
    progression = [
        ("Em7",   "E2", ["E3",  "G3",  "B3",  "D4"]),
        ("A7",    "A1", ["A3",  "C#4", "E4",  "G4"]),
        ("Dmaj7", "D2", ["D3",  "F#3", "A3",  "C#4"]),
        ("G",     "G1", ["G3",  "B3",  "D4",  "F#4"]),
    ]

    horn_riff = [
        # 8-bar funk horn line
        ("E5", 0.5),   ("G5", 0.5),  ("B5", 0.5),  ("E5", 0.5),
        ("D5", 1),     ("E5", 1),
        ("E5", 0.5),   ("G5", 0.5),  ("A5", 1),    ("E5", 2),
        ("C#5", 0.5),  ("E5", 0.5),  ("G5", 0.5),  ("C#5", 0.5),
        ("B4", 1),     ("A4", 1),
        ("F#5", 0.5),  ("A5", 0.5),  ("D6", 1),    ("A5", 2),
        ("G5", 0.5),   ("B5", 0.5),  ("D6", 0.5),  ("G5", 0.5),
        ("F#5", 1),    ("D5", 1),
        ("E5", 2),     ("G5", 1),    ("B5", 1),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # Horn riff every other cycle (call/response feel).
        if rep % 2 == 1:
            seq.play_line(lead, rep_start, horn_riff, vel=88)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            for bar in range(2):
                bar_start = chord_start + bar * 4

                # Drums: cowbell + claps for extra funk.
                _disco_drums(seq, drums, bar_start, with_cowbell=True, with_clap=(rep >= 2))

                # Bass: syncopated 16ths instead of straight 8ths for funkier feel.
                root_pitch = n(root) + 12
                # Pattern: R - R - oct - R - R - 5th - R - oct (16ths)
                bass_pattern = [
                    (0,    root_pitch,         92),
                    (0.5,  root_pitch,         70),
                    (0.75, root_pitch + 12,    82),
                    (1,    root_pitch,         88),
                    (1.5,  root_pitch + 7,     78),
                    (2,    root_pitch,         92),
                    (2.5,  root_pitch,         70),
                    (2.75, root_pitch + 12,    82),
                    (3,    root_pitch,         88),
                    (3.5,  root_pitch + 7,     78),
                ]
                for off, p, v in bass_pattern:
                    seq.add_note(bass, bar_start + off, 0.22, p, v)

                # Heavy chicken scratch on guitar.
                _disco_chicken_scratch(seq, guitar, bar_start, [n(p) for p in ch_pitches[:3]],
                                       accent_idx=(0, 3, 6, 8, 11, 14))

                # Clavinet: chord on beat 1 + offbeat stab on 2.5.
                seq.add_chord(keys, bar_start + 0, 0.3, [n(p) for p in ch_pitches[:3]], vel=72)
                seq.add_chord(keys, bar_start + 2.5, 0.3, [n(p) for p in ch_pitches[:3]], vel=64)

    return seq.to_smf()


# =============================================================================
# 19. DISCO - DIVA ANTHEM: lush dramatic disco (112 BPM, A minor)
# =============================================================================
def make_disco_diva_anthem(reps: int = 8) -> bytes:
    seq = Sequence(bpm=112)
    voice = seq.add_track("Voice Lead", GM_VOICE_OOHS, 0)
    bass = seq.add_track("Bass", GM_SYNTH_BASS_2, 1)
    strings = seq.add_track("Strings", GM_STRINGS, 2)
    pad = seq.add_track("Synth Strings", GM_SYNTH_STRINGS_1, 3)
    brass = seq.add_track("Brass", GM_BRASS_SECTION, 4)
    drums = seq.add_track("Drums", 0, 9)

    # 8-chord progression in 8 bars (1 bar per chord) for more drama.
    progression = [
        ("Am",  "A2",  ["A3",  "C4",  "E4"]),
        ("F",   "F1",  ["F3",  "A3",  "C4"]),
        ("C",   "C2",  ["C4",  "E4",  "G4"]),
        ("G",   "G1",  ["G3",  "B3",  "D4"]),
        ("Dm",  "D2",  ["D4",  "F4",  "A4"]),
        ("E7",  "E1",  ["E3",  "G#3", "B3",  "D4"]),
        ("Am",  "A2",  ["A3",  "C4",  "E4"]),
        ("E7",  "E1",  ["E3",  "G#3", "B3",  "D4"]),
    ]

    # 8-bar diva-style melody (1 bar per chord change).
    diva_melody = [
        ("E5", 1.5),   ("G5", 0.5),  ("A5", 2),                  # Am
        ("F5", 1),     ("A5", 1),    ("C6", 2),                  # F
        ("E5", 1),     ("G5", 1),    ("C6", 2),                  # C
        ("D5", 0.5),   ("G5", 0.5),  ("B5", 1),    ("D6", 2),    # G
        ("F5", 1),     ("A5", 1),    ("D6", 2),                  # Dm
        ("E5", 1),     ("G#5", 1),   ("B5", 2),                  # E7
        ("A5", 0.5),   ("C6", 0.5),  ("E6", 1),    ("A6", 2),    # Am
        ("G#5", 1),    ("B5", 1),    ("E5", 2),                  # E7
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # Voice lead drops in from cycle 2.
        if rep >= 1:
            seq.play_line(voice, rep_start, diva_melody, vel=88)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 4  # 1 bar per chord

            # Strings + pad: layered held chords.
            seq.add_chord(strings, chord_start, 4, [n(p) for p in ch_pitches], vel=60)
            seq.add_chord(pad, chord_start, 4, [n(p) + 12 for p in ch_pitches[:3]], vel=46)

            _disco_drums(seq, drums, chord_start, with_clap=(rep >= 1))
            _disco_bass_octave(seq, bass, chord_start, n(root) + 12)

            # Brass stab on beat 4.5 of every bar (drives into next chord).
            stab = [n(p) + 12 for p in ch_pitches[:3]]
            seq.add_chord(brass, chord_start + 3.5, 0.4, stab, vel=80)

    return seq.to_smf()


# =============================================================================
# 20. DISCO - DANCE FLOOR HEAT: euro-disco, 16th-note synth bass (128 BPM, C min)
# =============================================================================
def make_disco_dance_floor_heat(reps: int = 9) -> bytes:
    seq = Sequence(bpm=128)
    lead = seq.add_track("Synth Lead", GM_SQUARE_LEAD, 0)
    bass = seq.add_track("Synth Bass", GM_SYNTH_BASS_1, 1)
    pad = seq.add_track("Pad", GM_PAD_POLYSYNTH, 2)
    strings = seq.add_track("Strings", GM_STRINGS, 3)
    drums = seq.add_track("Drums", 0, 9)

    # Cm - Ab - Eb - Bb (i - VI - III - VII), 2 bars each = 8-bar cycle (~15s).
    progression = [
        ("Cm",  "C2",  ["C4",  "Eb4", "G4"]),
        ("Ab",  "Ab1", ["Ab3", "C4",  "Eb4"]),
        ("Eb",  "Eb2", ["Eb3", "G3",  "Bb3"]),
        ("Bb",  "Bb1", ["Bb3", "D4",  "F4"]),
    ]

    arp_melody = [
        # 8-bar Moroder-style synth lead
        ("G5", 0.5),  ("Eb5", 0.5), ("C5", 0.5),  ("G4", 0.5),
        ("G5", 0.5),  ("Eb5", 0.5), ("C5", 0.5),  ("Eb5", 0.5),
        ("Ab5", 0.5), ("Eb5", 0.5), ("C5", 0.5),  ("Ab4", 0.5),
        ("Ab5", 0.5), ("Eb5", 0.5), ("C5", 0.5),  ("Eb5", 0.5),
        ("Bb5", 0.5), ("G5", 0.5),  ("Eb5", 0.5), ("Bb4", 0.5),
        ("Bb5", 0.5), ("G5", 0.5),  ("Eb5", 0.5), ("G5", 0.5),
        ("D6", 0.5),  ("Bb5", 0.5), ("F5", 0.5),  ("D5", 0.5),
        ("F5", 1),    ("Bb5", 1),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(lead, rep_start, arp_melody, vel=82)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Pad/strings: held chord through 2 bars.
            seq.add_chord(pad, chord_start, 8, [n(p) for p in ch_pitches], vel=52)
            seq.add_chord(strings, chord_start, 8, [n(p) + 12 for p in ch_pitches[:3]], vel=44)

            for bar in range(2):
                bar_start = chord_start + bar * 4

                _disco_drums(seq, drums, bar_start, with_clap=(rep >= 2), with_tambourine=(rep >= 4))

                # 16th-note synth bass arpeggio (the Moroder hallmark).
                root_pitch = n(root) + 12
                fifth_pitch = root_pitch + 7
                octave_pitch = root_pitch + 12
                bass_seq = [root_pitch, fifth_pitch, octave_pitch, fifth_pitch] * 4
                for i, p in enumerate(bass_seq):
                    seq.add_note(bass, bar_start + i * 0.25, 0.22, p, vel=88)

    return seq.to_smf()


# =============================================================================
# Shared jazz helpers (swing ride pattern, walking bass quarters).
# =============================================================================
def _jazz_swing_ride(seq: Sequence, drums: Track, bar_start: float, vel: int = 58) -> None:
    """Classic 'spang-a-lang' swung ride pattern over 4 beats (4/4)."""
    # Beat 1, 2, 2.667 (swung "and"), 3, 4, 4.667
    for b in (0, 1, 1.667, 2, 3, 3.667):
        seq.add_drum(drums, bar_start + b, RIDE, vel=vel - (8 if b in (1.667, 3.667) else 0))


def _jazz_hat_foot(seq: Sequence, drums: Track, bar_start: float, vel: int = 50) -> None:
    """Hi-hat pedal on beats 2 and 4 (jazz afterbeat)."""
    seq.add_drum(drums, bar_start + 1, PEDAL_HAT, vel=vel)
    seq.add_drum(drums, bar_start + 3, PEDAL_HAT, vel=vel)


def _jazz_walking_bass(seq: Sequence, bass: Track, bar_start: float,
                       notes: list[str], vel: int = 78) -> None:
    """4 quarter notes per bar, slight gap so attack is clean."""
    for i, p in enumerate(notes):
        seq.add_note(bass, bar_start + i, 0.92, n(p), vel)


# =============================================================================
# 21. JAZZ - SMOOTH STANDARDS: medium-swing ballad (80 BPM, F major)
# =============================================================================
def make_jazz_smooth_standards(reps: int = 6) -> bytes:
    seq = Sequence(bpm=80)
    sax = seq.add_track("Tenor Sax", 66, 0)  # GM Tenor Sax
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 1)
    bass = seq.add_track("Bass", GM_ACOUSTIC_BASS, 2)
    drums = seq.add_track("Drums", 0, 9)

    # 8-bar form: Fmaj7 - Dm7 - Gm7 - C7 - Am7 - D7 - Gm7 - C7 (~24s/cycle).
    chords = [
        # name, walking bass (4 quarters), piano voicing
        ("Fmaj7", ["F2",  "A2",  "C3",  "E3"],  ["E3",  "A3",  "C4",  "F4"]),
        ("Dm7",   ["D2",  "F2",  "A2",  "C3"],  ["F3",  "A3",  "C4",  "D4"]),
        ("Gm7",   ["G2",  "Bb2", "D3",  "F3"],  ["F3",  "Bb3", "D4",  "G4"]),
        ("C7",    ["C2",  "E2",  "G2",  "Bb2"], ["E3",  "G3",  "Bb3", "C4"]),
        ("Am7",   ["A2",  "C3",  "E3",  "G3"],  ["G3",  "C4",  "E4",  "A4"]),
        ("D7",    ["D2",  "F#2", "A2",  "C3"],  ["F#3", "A3",  "C4",  "D4"]),
        ("Gm7",   ["G2",  "Bb2", "D3",  "F3"],  ["F3",  "Bb3", "D4",  "G4"]),
        ("C7",    ["C2",  "E2",  "G2",  "Bb2"], ["E3",  "G3",  "Bb3", "C4"]),
    ]

    # Sax melody: lyrical, sustained, with a few ornaments.
    sax_line = [
        ("A4", 1.5),   ("C5", 0.5),  ("F5", 1),    ("E5", 1),     # Fmaj7
        ("D5", 1),     ("F5", 1),    ("A5", 2),                   # Dm7
        ("Bb4", 1),    ("D5", 1),    ("F5", 0.5),  ("D5", 0.5),   ("Bb4", 1),  # Gm7
        ("E4", 0.5),   ("G4", 0.5),  ("Bb4", 1),   ("C5", 2),     # C7
        ("A4", 1.5),   ("C5", 0.5),  ("E5", 1),    ("G5", 1),     # Am7
        ("F5", 0.5),   ("D5", 0.5),  ("F#5", 1),   ("A5", 2),     # D7
        ("G5", 1),     ("F5", 1),    ("D5", 1),    ("Bb4", 1),    # Gm7
        ("Bb4", 1),    ("A4", 1),    ("E4", 1),    ("F4", 1),     # C7 -> resolve to F
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # Sax plays from cycle 2 onward (intro = bass + drums + piano comp).
        if rep >= 1:
            seq.play_line(sax, rep_start, sax_line, vel=82)

        for ci, (cname, walk, voicing) in enumerate(chords):
            bar_start = rep_start + ci * 4

            _jazz_swing_ride(seq, drums, bar_start)
            _jazz_hat_foot(seq, drums, bar_start)
            _jazz_walking_bass(seq, bass, bar_start, walk)

            # Piano: comp on beat 1 + offbeat (Charleston feel).
            seq.add_chord(piano, bar_start + 0, 1.4, [n(p) for p in voicing], vel=66)
            seq.add_chord(piano, bar_start + 2.5, 1.4, [n(p) for p in voicing], vel=58)

    return seq.to_smf()


# =============================================================================
# 22. JAZZ - BEBOP BURNER: fast bebop (192 BPM, C major)
# =============================================================================
def make_jazz_bebop_burner(reps: int = 7) -> bytes:
    seq = Sequence(bpm=192)
    trumpet = seq.add_track("Trumpet", GM_TRUMPET, 0)
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 1)
    bass = seq.add_track("Bass", GM_ACOUSTIC_BASS, 2)
    drums = seq.add_track("Drums", 0, 9)

    # 16-bar rhythm-changes-style A section in C major (~20s/cycle at 192 BPM).
    # | C  Am | Dm G | C  Am | Dm G | F  Fm | C  A7 | Dm G | C   ||
    # Use 1 bar per chord pair where listed, simplified:
    chords_16 = [
        # First 8 bars (A1)
        ("C",   ["C2",  "E2",  "G2",  "B2"], ["E3", "G3", "C4"]),
        ("Am7", ["A2",  "C3",  "E3",  "G3"], ["E3", "G3", "C4"]),
        ("Dm7", ["D2",  "F2",  "A2",  "C3"], ["F3", "A3", "C4"]),
        ("G7",  ["G2",  "B2",  "D3",  "F3"], ["F3", "B3", "D4"]),
        ("C",   ["C2",  "E2",  "G2",  "B2"], ["E3", "G3", "C4"]),
        ("Am7", ["A2",  "C3",  "E3",  "G3"], ["E3", "G3", "C4"]),
        ("Dm7", ["D2",  "F2",  "A2",  "C3"], ["F3", "A3", "C4"]),
        ("G7",  ["G2",  "B2",  "D3",  "F3"], ["F3", "B3", "D4"]),
        # Second 8 bars (A2)
        ("F",   ["F2",  "A2",  "C3",  "E3"], ["A3", "C4", "F4"]),
        ("Fm",  ["F2",  "Ab2", "C3",  "Eb3"], ["Ab3","C4", "F4"]),
        ("C",   ["C2",  "E2",  "G2",  "B2"], ["E3", "G3", "C4"]),
        ("A7",  ["A2",  "C#3", "E3",  "G3"], ["C#3","E3", "G3"]),
        ("Dm7", ["D2",  "F2",  "A2",  "C3"], ["F3", "A3", "C4"]),
        ("G7",  ["G2",  "B2",  "D3",  "F3"], ["F3", "B3", "D4"]),
        ("C",   ["C2",  "E2",  "G2",  "B2"], ["E3", "G3", "C4"]),
        ("G7",  ["G2",  "B2",  "D3",  "F3"], ["F3", "B3", "D4"]),
    ]

    # Bebop trumpet head: rapid 8th-note runs over the changes (16 bars).
    bebop_head = [
        # Bar 1 (C): C major arp with chromatic approach
        ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("B5", 0.5),
        ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("C5", 0.5),
        # Bar 2 (Am7)
        ("A5", 0.5), ("E5", 0.5), ("C5", 0.5), ("E5", 0.5),
        ("G5", 0.5), ("E5", 0.5), ("C5", 0.5), ("A4", 0.5),
        # Bar 3 (Dm7)
        ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("C6", 0.5),
        ("Bb5", 0.5), ("A5", 0.5), ("F5", 0.5), ("D5", 0.5),
        # Bar 4 (G7)
        ("D5", 0.5), ("F5", 0.5), ("G5", 0.5), ("B5", 0.5),
        ("D6", 0.5), ("B5", 0.5), ("F5", 0.5), ("D5", 0.5),
        # Bar 5 (C)
        ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("E5", 0.5),
        ("C5", 0.5), ("D5", 0.5), ("E5", 0.5), ("F5", 0.5),
        # Bar 6 (Am7)
        ("G5", 0.5), ("A5", 0.5), ("C6", 0.5), ("A5", 0.5),
        ("G5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5),
        # Bar 7 (Dm7)
        ("D5", 0.5), ("E5", 0.5), ("F5", 0.5), ("A5", 0.5),
        ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5),
        # Bar 8 (G7)
        ("B4", 0.5), ("D5", 0.5), ("F5", 0.5), ("G5", 0.5),
        ("F5", 0.5), ("D5", 0.5), ("B4", 0.5), ("G4", 0.5),
        # Bar 9 (F)
        ("A4", 0.5), ("C5", 0.5), ("F5", 0.5), ("A5", 0.5),
        ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("C5", 0.5),
        # Bar 10 (Fm)
        ("F5", 0.5), ("Ab5", 0.5), ("C6", 0.5), ("Ab5", 0.5),
        ("F5", 0.5), ("Eb5", 0.5), ("C5", 0.5), ("Ab4", 0.5),
        # Bar 11 (C)
        ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("E6", 0.5),
        ("D6", 0.5), ("C6", 0.5), ("B5", 0.5), ("G5", 0.5),
        # Bar 12 (A7)
        ("E5", 0.5), ("G5", 0.5), ("C#6", 0.5), ("E6", 0.5),
        ("C#6", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5),
        # Bar 13 (Dm7)
        ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("C6", 0.5),
        ("Bb5", 0.5), ("A5", 0.5), ("F5", 0.5), ("D5", 0.5),
        # Bar 14 (G7)
        ("G5", 0.5), ("B5", 0.5), ("D6", 0.5), ("F6", 0.5),
        ("D6", 0.5), ("B5", 0.5), ("F5", 0.5), ("D5", 0.5),
        # Bar 15 (C)
        ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("G5", 0.5),
        ("E5", 0.5), ("D5", 0.5), ("C5", 0.5), ("E5", 0.5),
        # Bar 16 (G7) - turnaround
        ("D5", 0.5), ("B4", 0.5), ("D5", 0.5), ("F5", 0.5),
        ("G5", 0.5), ("F5", 0.5), ("D5", 0.5), ("B4", 0.5),
    ]

    for rep in range(reps):
        rep_start = rep * 64  # 16 bars * 4 beats = 64 beats per cycle

        # Trumpet plays the head on cycles 0 and the last; rests on middle ones.
        if rep == 0 or rep == reps - 1 or rep % 3 == 1:
            seq.play_line(trumpet, rep_start, bebop_head, vel=88)

        for ci, (cname, walk, voicing) in enumerate(chords_16):
            bar_start = rep_start + ci * 4

            _jazz_swing_ride(seq, drums, bar_start, vel=66)
            _jazz_hat_foot(seq, drums, bar_start, vel=58)
            # Drummer occasionally drops a snare comment on beat 4.5
            if ci % 4 == 3:
                seq.add_drum(drums, bar_start + 3.5, SNARE, vel=55)

            _jazz_walking_bass(seq, bass, bar_start, walk, vel=82)

            # Piano: rhythmic comp (Bud Powell-style: chord on beat 1 + offbeat 3.5).
            seq.add_chord(piano, bar_start + 0, 0.6, [n(p) for p in voicing], vel=66)
            seq.add_chord(piano, bar_start + 2.5, 0.6, [n(p) for p in voicing], vel=58)

    return seq.to_smf()


# =============================================================================
# 23. JAZZ - BOSSA NOVA: Brazilian feel, gentle & syncopated (120 BPM, D minor)
# =============================================================================
def make_jazz_bossa_nova(reps: int = 8) -> bytes:
    seq = Sequence(bpm=120)
    flute = seq.add_track("Flute", 73, 0)  # GM Flute
    piano = seq.add_track("Electric Piano", GM_ELECTRIC_PIANO_1, 1)
    bass = seq.add_track("Bass", GM_ACOUSTIC_BASS, 2)
    drums = seq.add_track("Drums", 0, 9)

    # 8-bar progression: Dm7 - G7 - Cmaj7 - Fmaj7 - Bm7b5 - E7 - Am7 - D7
    chords = [
        ("Dm7",    "D2",  "A2",  ["D3",  "F3",  "A3",  "C4"]),
        ("G7",     "G1",  "D2",  ["F3",  "B3",  "D4"]),
        ("Cmaj7",  "C2",  "G2",  ["E3",  "G3",  "B3"]),
        ("Fmaj7",  "F1",  "C2",  ["A3",  "C4",  "E4"]),
        ("Bm7b5",  "B1",  "F2",  ["D3",  "F3",  "A3"]),
        ("E7",     "E1",  "B1",  ["G#3", "B3",  "D4"]),
        ("Am7",    "A1",  "E2",  ["C4",  "E4",  "G4"]),
        ("D7",     "D2",  "A2",  ["F#3", "A3",  "C4"]),
    ]

    # Flute melody (8 bars), gentle and syncopated.
    flute_line = [
        ("F5", 1.5),   ("E5", 0.5),   ("D5", 1),    ("F5", 1),
        ("D5", 1),     ("F5", 1),     ("G5", 1.5),  ("E5", 0.5),
        ("E5", 1),     ("D5", 0.5),   ("E5", 0.5),  ("G5", 2),
        ("F5", 1),     ("E5", 1),     ("C5", 1),    ("A4", 1),
        ("D5", 1.5),   ("F5", 0.5),   ("A5", 2),
        ("G#5", 0.5),  ("B5", 0.5),   ("D6", 1),    ("B5", 2),
        ("A5", 1),     ("E5", 1),     ("G5", 1),    ("C5", 1),
        ("F#5", 1),    ("A5", 1),     ("D5", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(flute, rep_start, flute_line, vel=78)

        for ci, (cname, root_low, root_high, voicing) in enumerate(chords):
            bar_start = rep_start + ci * 4

            # Bossa bass: root on 1 + 5th on 2.5 (anticipation), every bar.
            seq.add_note(bass, bar_start + 0,   1.4, n(root_low),  vel=78)
            seq.add_note(bass, bar_start + 2.5, 1.4, n(root_high), vel=72)

            # Bossa drum: side stick (snare at low velocity) on syncopated pattern,
            # plus light closed hat 8ths.
            seq.add_drum(drums, bar_start + 0,    SNARE, vel=42)  # low-vel = side-stick approximation
            seq.add_drum(drums, bar_start + 1,    SNARE, vel=38)
            seq.add_drum(drums, bar_start + 1.5,  SNARE, vel=42)
            seq.add_drum(drums, bar_start + 2.5,  SNARE, vel=42)
            seq.add_drum(drums, bar_start + 3,    SNARE, vel=38)
            seq.add_drum(drums, bar_start + 3.5,  SNARE, vel=42)
            for h in range(8):
                seq.add_drum(drums, bar_start + h * 0.5, CLOSED_HAT, vel=44)
            # Light kick on beat 1 only.
            seq.add_drum(drums, bar_start + 0, KICK, vel=58)

            # Piano comp: classic bossa "1+3+ 4 +" pattern with 16th anticipation.
            for off in (0, 0.75, 1.5, 2.5, 3.25):
                seq.add_chord(piano, bar_start + off, 0.45, [n(p) for p in voicing], vel=58)

    return seq.to_smf()


# =============================================================================
# 24. JAZZ - WALTZ: jazz waltz in 3/4 (96 BPM, A minor)
# =============================================================================
def make_jazz_waltz(reps: int = 9) -> bytes:
    seq = Sequence(bpm=96, time_sig=(3, 2))
    sax = seq.add_track("Soprano Sax", 64, 0)  # GM Soprano Sax
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 1)
    bass = seq.add_track("Bass", GM_ACOUSTIC_BASS, 2)
    drums = seq.add_track("Drums", 0, 9)

    # 8-bar progression in 3/4: Am - Em - F - C - Dm - G - C - E7
    chords = [
        ("Am",  ["A2",  "C3",  "E3"], ["A3",  "C4",  "E4"]),
        ("Em",  ["E2",  "G2",  "B2"], ["G3",  "B3",  "E4"]),
        ("F",   ["F2",  "A2",  "C3"], ["A3",  "C4",  "F4"]),
        ("C",   ["C3",  "E3",  "G3"], ["E3",  "G3",  "C4"]),
        ("Dm",  ["D2",  "F2",  "A2"], ["F3",  "A3",  "D4"]),
        ("G",   ["G2",  "B2",  "D3"], ["G3",  "B3",  "D4"]),
        ("C",   ["C3",  "E3",  "G3"], ["E3",  "G3",  "C4"]),
        ("E7",  ["E2",  "G#2", "B2"], ["G#3", "B3",  "D4"]),
    ]

    # Sax line in 3 (8 bars * 3 beats = 24 beats per phrase).
    sax_line = [
        ("E5", 1),   ("A5", 1),   ("C6", 1),     # bar 1 (Am)
        ("B5", 1),   ("G5", 1),   ("E5", 1),     # bar 2 (Em)
        ("F5", 1),   ("A5", 1),   ("C6", 1),     # bar 3 (F)
        ("E5", 1.5), ("D5", 0.5), ("C5", 1),     # bar 4 (C)
        ("D5", 1),   ("F5", 1),   ("A5", 1),     # bar 5 (Dm)
        ("G5", 1),   ("D5", 1),   ("B5", 1),     # bar 6 (G)
        ("C5", 1),   ("E5", 1),   ("G5", 1),     # bar 7 (C)
        ("G#5", 1),  ("B5", 1),   ("E6", 1),     # bar 8 (E7)
    ]

    for rep in range(reps):
        rep_start = rep * 24  # 8 bars * 3 beats per bar

        if rep >= 1:
            seq.play_line(sax, rep_start, sax_line, vel=80)

        for ci, (cname, walk, voicing) in enumerate(chords):
            bar_start = rep_start + ci * 3  # 3 beats per bar

            # Bass on beat 1; chord tones on 2 and 3 (jazz waltz "in 3" feel).
            seq.add_note(bass, bar_start + 0, 0.95, n(walk[0]), vel=78)
            seq.add_note(bass, bar_start + 1, 0.95, n(walk[1]), vel=72)
            seq.add_note(bass, bar_start + 2, 0.95, n(walk[2]), vel=72)

            # Ride pattern in 3: ride on every beat, accent on 1.
            seq.add_drum(drums, bar_start + 0, RIDE, vel=64)
            seq.add_drum(drums, bar_start + 1, RIDE, vel=52)
            seq.add_drum(drums, bar_start + 2, RIDE, vel=52)
            seq.add_drum(drums, bar_start + 1.667, RIDE, vel=44)  # swung "and" of 2

            # Hi-hat on beat 2 (jazz waltz afterbeat).
            seq.add_drum(drums, bar_start + 1, PEDAL_HAT, vel=46)
            # Soft snare comment on beat 2 once per bar (low vel).
            seq.add_drum(drums, bar_start + 2.5, SNARE, vel=38)

            # Piano comp: chord on beat 1 + a softer offbeat on 2.5.
            seq.add_chord(piano, bar_start + 0,   1.0, [n(p) for p in voicing], vel=66)
            seq.add_chord(piano, bar_start + 2.5, 0.5, [n(p) for p in voicing], vel=54)

    return seq.to_smf()


# =============================================================================
# 25. JAZZ - COOL BLUE: modal "Kind of Blue" feel (100 BPM, D dorian)
# =============================================================================
def make_jazz_cool_blue(reps: int = 2) -> bytes:
    seq = Sequence(bpm=100)
    trumpet = seq.add_track("Muted Trumpet", 59, 0)  # GM Muted Trumpet
    piano = seq.add_track("Piano", GM_ACOUSTIC_GRAND, 1)
    bass = seq.add_track("Bass", GM_ACOUSTIC_BASS, 2)
    drums = seq.add_track("Drums", 0, 9)

    # Modal vamp: 16 bars Dm7, then 16 bars Ebm7 (a-la "So What").
    # Each rep = 32 bars = 128 beats = 76.8s. 2 reps = 153s.
    sections = [
        ("Dm7",  ["D2", "F2", "A2", "C3"], ["F3", "A3", "C4", "E4"]),  # D dorian
        ("Ebm7", ["Eb2","Gb2","Bb2","Db3"], ["Gb3","Bb3","Db4","F4"]), # Eb dorian
    ]

    # Modal melody (8 bars, repeated and transposed for the second key).
    modal_phrase = [
        # Bar 1
        ("D5", 1.5),   ("F5", 0.5),   ("A5", 2),
        # Bar 2
        ("G5", 1),     ("F5", 1),     ("E5", 1),     ("D5", 1),
        # Bar 3
        ("C5", 2),     ("E5", 2),
        # Bar 4
        ("A4", 2),     (None, 2),
        # Bar 5
        ("F5", 1.5),   ("A5", 0.5),   ("C6", 2),
        # Bar 6
        ("Bb5", 1),    ("A5", 1),     ("F5", 2),
        # Bar 7
        ("D5", 4),
        # Bar 8 - rest
        (None, 4),
    ]

    def transpose_phrase(phrase, semitones):
        out = []
        for pitch, dur in phrase:
            if pitch is None:
                out.append((None, dur))
            else:
                out.append((_transpose(pitch, semitones), dur))
        return out

    for rep in range(reps):
        rep_start = rep * 128

        for si, (cname, walk, voicing) in enumerate(sections):
            section_start = rep_start + si * 64  # 16 bars per section

            # Modal phrase plays in bars 1-8 of each 16-bar section.
            phrase = modal_phrase if si == 0 else transpose_phrase(modal_phrase, 1)
            seq.play_line(trumpet, section_start, phrase, vel=78)
            # Echo of phrase higher in bars 9-16.
            phrase_high = transpose_phrase(phrase, 12) if si == 0 else transpose_phrase(phrase, 13)
            seq.play_line(trumpet, section_start + 32, phrase_high, vel=68)

            # 16 bars of comping/walking/ride for this modal section.
            for bi in range(16):
                bar_start = section_start + bi * 4

                _jazz_swing_ride(seq, drums, bar_start, vel=58)
                _jazz_hat_foot(seq, drums, bar_start, vel=48)
                # Walking bass with subtle variation across the section.
                walk_for_bar = walk if bi % 2 == 0 else list(reversed(walk))
                _jazz_walking_bass(seq, bass, bar_start, walk_for_bar, vel=74)
                # Sparse modal piano voicing on beat 1 + occasional comp.
                seq.add_chord(piano, bar_start + 0, 1.5, [n(p) for p in voicing], vel=58)
                if bi % 4 == 1:
                    seq.add_chord(piano, bar_start + 2.5, 1.0, [n(p) for p in voicing[:3]], vel=48)

    return seq.to_smf()


def _transpose(pitch_name: str, semitones: int) -> str:
    """Transpose a note name like 'D5' or 'Bb4' by N semitones, returning a note name."""
    midi = n(pitch_name) + semitones
    pc = midi % 12
    octave = midi // 12 - 1
    name = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"][pc]
    return f"{name}{octave}"


# =============================================================================
# Shared thug-life helpers (boom-bap and trap drum patterns).
# =============================================================================
def _boom_bap_drums(seq: Sequence, drums: Track, bar_start: float,
                    with_rim: bool = False, with_open_hat_offbeats: bool = True) -> None:
    """Classic 90s boom-bap: heavy kick on 1 + 3.5, snare on 3, soft hat 8ths."""
    seq.add_drum(drums, bar_start + 0,   KICK, vel=110)
    seq.add_drum(drums, bar_start + 2,   SNARE, vel=104)
    seq.add_drum(drums, bar_start + 3.5, KICK, vel=92)
    for h in range(8):
        beat = bar_start + h * 0.5
        if with_open_hat_offbeats and (h % 2 == 1):
            seq.add_drum(drums, beat, OPEN_HAT, vel=58)
        else:
            seq.add_drum(drums, beat, CLOSED_HAT, vel=52)
    if with_rim:
        seq.add_drum(drums, bar_start + 1, SNARE, vel=42)


def _trap_drums(seq: Sequence, drums: Track, bar_start: float, fill: bool = False) -> None:
    """Trap-style drums: kick on 1 + syncopated, clap on 3, fast 16th hats with rolls."""
    seq.add_drum(drums, bar_start + 0,    KICK, vel=115)
    seq.add_drum(drums, bar_start + 1.5,  KICK, vel=92)
    seq.add_drum(drums, bar_start + 2.75, KICK, vel=88)
    # Clap+snare on beat 3 (the "snare" of trap).
    seq.add_drum(drums, bar_start + 2, HAND_CLAP, vel=108)
    seq.add_drum(drums, bar_start + 2, SNARE, vel=78)
    # 16th hats throughout, with a 32nd-note roll on beat 4 if fill.
    if fill:
        for i in range(12):
            seq.add_drum(drums, bar_start + i * 0.25, CLOSED_HAT, vel=54 - (i % 2) * 6)
        # 32nd-note roll on beat 4.
        for i in range(8):
            seq.add_drum(drums, bar_start + 3 + i * 0.125, CLOSED_HAT, vel=48 + i * 2)
    else:
        for i in range(16):
            v = 56 if i % 2 == 0 else 44
            # Slight emphasis on beats 1, 2, 3, 4
            if i in (0, 4, 8, 12):
                v = 64
            seq.add_drum(drums, bar_start + i * 0.25, CLOSED_HAT, vel=v)


# =============================================================================
# 26. THUG LIFE - G-FUNK CRUISE: 90s West Coast g-funk (92 BPM, E minor)
# =============================================================================
def make_thug_g_funk_cruise(reps: int = 6) -> bytes:
    seq = Sequence(bpm=92)
    lead = seq.add_track("Whiny Lead", 84, 0)  # GM Lead 5 (Charang) - g-funk whiny synth
    bass = seq.add_track("Sub Bass", GM_SYNTH_BASS_1, 1)
    pad = seq.add_track("Pad", GM_PAD_WARM, 2)
    talkbox = seq.add_track("Talkbox", GM_VOICE_OOHS, 3)
    drums = seq.add_track("Drums", 0, 9)

    # Em - Bm - Am - Em (i-v-iv-i), 2 bars per chord = 8-bar cycle (~21s).
    progression = [
        ("Em",  "E2",  ["E3",  "G3",  "B3"]),
        ("Bm",  "B1",  ["B2",  "D3",  "F#3"]),
        ("Am",  "A1",  ["A2",  "C3",  "E3"]),
        ("Em",  "E2",  ["E3",  "G3",  "B3"]),
    ]

    lead_line = [
        # Whiny portamento-style lead (8 bars)
        ("E5", 1.5),   ("G5", 0.5),   ("B5", 2),
        ("A5", 1),     ("G5", 1),     ("E5", 2),
        ("F#5", 2),    ("A5", 1),     ("B5", 1),
        ("D6", 1),     ("B5", 1),     ("F#5", 2),
        ("E5", 1),     ("A5", 1),     ("C6", 2),
        ("B5", 1),     ("A5", 1),     ("E5", 2),
        ("E5", 1),     ("G5", 1),     ("B5", 2),
        ("A5", 1),     ("G5", 1),     ("E5", 2),
    ]

    talk_oohs = [
        ("E4", 4), (None, 4),
        ("D4", 4), (None, 4),
        ("C4", 4), (None, 4),
        ("E4", 4), (None, 4),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # Lead drops in cycle 2; talkbox-style oohs on cycles 3-4 for variation.
        if rep >= 1:
            seq.play_line(lead, rep_start, lead_line, vel=82)
        if rep in (2, 4):
            seq.play_line(talkbox, rep_start, talk_oohs, vel=58)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Pad: warm sustained chord across 2 bars.
            seq.add_chord(pad, chord_start, 8, [n(p) for p in ch_pitches], vel=50)

            for bar in range(2):
                bar_start = chord_start + bar * 4

                _boom_bap_drums(seq, drums, bar_start, with_rim=(rep >= 2))

                # Sub bass: long held root + octave bounce on beat 3.
                seq.add_note(bass, bar_start + 0, 2.5, n(root), vel=98)
                seq.add_note(bass, bar_start + 3, 1.0, n(root) + 12, vel=80)

    return seq.to_smf()


# =============================================================================
# 27. THUG LIFE - EAST COAST BOOM BAP: jazzy sample style (88 BPM, A minor)
# =============================================================================
def make_thug_east_coast_boom_bap(reps: int = 6) -> bytes:
    seq = Sequence(bpm=88)
    piano = seq.add_track("Sample Piano", GM_ACOUSTIC_GRAND, 0)
    vibes = seq.add_track("Vibes", GM_VIBRAPHONE, 1)
    bass = seq.add_track("Upright Bass", GM_ACOUSTIC_BASS, 2)
    rhodes = seq.add_track("Rhodes", GM_ELECTRIC_PIANO_1, 3)
    drums = seq.add_track("Drums", 0, 9)

    # Am7 - Dm7 - Em7 - Am7 (i-iv-v-i), 2 bars per chord = 8-bar cycle (~21.8s).
    progression = [
        ("Am7", "A1",  ["A3",  "C4",  "E4",  "G4"]),
        ("Dm7", "D2",  ["D3",  "F3",  "A3",  "C4"]),
        ("Em7", "E2",  ["E3",  "G3",  "B3",  "D4"]),
        ("Am7", "A1",  ["A3",  "C4",  "E4",  "G4"]),
    ]

    # Vibraphone "sample" melody (8 bars).
    vibes_line = [
        ("A4", 1),   ("C5", 1),   ("E5", 2),
        ("G5", 1),   ("E5", 1),   ("A4", 2),
        ("D5", 1),   ("F5", 1),   ("A5", 2),
        ("G5", 0.5), ("F5", 0.5), ("D5", 1),   ("A4", 2),
        ("B4", 1),   ("D5", 1),   ("G5", 2),
        ("F5", 1),   ("E5", 1),   ("B4", 2),
        ("A4", 1),   ("E5", 1),   ("C5", 2),
        ("E5", 1),   ("A4", 1),   ("E4", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(vibes, rep_start, vibes_line, vel=72)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Piano sample: chord stab on beat 1 + offbeat (every bar).
            for bar in range(2):
                bar_start = chord_start + bar * 4

                _boom_bap_drums(seq, drums, bar_start)

                # Bass: 1 and 3 (lazy walking).
                seq.add_note(bass, bar_start + 0, 1.4, n(root) + 12, vel=88)
                seq.add_note(bass, bar_start + 2, 1.4, n(root) + 19, vel=78)

                # Piano stab on 1, soft.
                seq.add_chord(piano, bar_start + 0, 0.6, [n(p) for p in ch_pitches[:3]], vel=68)
                # Rhodes layered chord on 2.5 for depth.
                seq.add_chord(rhodes, bar_start + 2.5, 1.4, [n(p) for p in ch_pitches], vel=52)

    return seq.to_smf()


# =============================================================================
# 28. THUG LIFE - TRAP ANTHEM: dark trap with rolled hats (70 BPM, G minor)
# =============================================================================
def make_thug_trap_anthem(reps: int = 5) -> bytes:
    seq = Sequence(bpm=70)
    lead = seq.add_track("Dark Lead", 81, 0)  # GM Lead 2 (Sawtooth) - dark melodic
    bass_808 = seq.add_track("808 Bass", GM_SYNTH_BASS_1, 1)
    pad = seq.add_track("Pad", GM_PAD_WARM, 2)
    bell = seq.add_track("Bell", GM_GLOCKENSPIEL, 3)
    drums = seq.add_track("Drums", 0, 9)

    # Gm - Eb - Cm - Dm (i-VI-iv-v), 2 bars per chord = 8-bar cycle (~27.4s).
    progression = [
        ("Gm",  "G1",  ["G3",  "Bb3", "D4"]),
        ("Eb",  "Eb1", ["Eb3", "G3",  "Bb3"]),
        ("Cm",  "C2",  ["C3",  "Eb3", "G3"]),
        ("Dm",  "D2",  ["D3",  "F3",  "A3"]),
    ]

    # Dark melodic lead (sparse, repeats with variation).
    lead_line = [
        ("G4", 2),     ("Bb4", 1),   ("D5", 1),
        ("F5", 2),     ("Eb5", 1),   ("D5", 1),
        ("Eb4", 2),    ("G4", 1),    ("Bb4", 1),
        ("D5", 2),     ("Bb4", 1),   ("G4", 1),
        ("C5", 2),     ("Eb5", 1),   ("G5", 1),
        ("F5", 2),     ("Eb5", 1),   ("C5", 1),
        ("D5", 2),     ("F5", 1),    ("A5", 1),
        ("G5", 2),     ("F5", 1),    ("D5", 1),
    ]

    bell_phrase = [
        # Sparse bell ostinato that adds atmosphere.
        ("D6", 0.5), (None, 1.5), ("F6", 0.5), (None, 1.5),
        ("D6", 0.5), (None, 1.5), ("Bb5", 0.5), (None, 1.5),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(lead, rep_start, lead_line, vel=78)
        # Bell ostinato on cycles 2+ (one phrase per chord change of 2 bars).
        if rep >= 2:
            for ci in range(4):
                seq.play_line(bell, rep_start + ci * 8, bell_phrase, vel=64)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Pad: held chord softly, very ambient.
            seq.add_chord(pad, chord_start, 8, [n(p) for p in ch_pitches], vel=46)

            for bar in range(2):
                bar_start = chord_start + bar * 4

                # Trap drums; fill on the very last bar of every other cycle.
                fill = (bar == 1 and ci == 3 and rep % 2 == 1)
                _trap_drums(seq, drums, bar_start, fill=fill)

                # 808 sub bass: long held note on chord change (sustained for 4 beats).
                # Plus a slide-up on beat 3 for trap-feel.
                seq.add_note(bass_808, bar_start + 0, 2.8, n(root) - 12, vel=110)
                seq.add_note(bass_808, bar_start + 3, 1.0, n(root), vel=92)

    return seq.to_smf()


# =============================================================================
# 29. THUG LIFE - BLOCK PARTY: early 90s hip-hop, soul vibe (100 BPM, G major)
# =============================================================================
def make_thug_block_party(reps: int = 7) -> bytes:
    seq = Sequence(bpm=100)
    horn = seq.add_track("Horn Sample", GM_BRASS_SECTION, 0)
    rhodes = seq.add_track("Rhodes", GM_ELECTRIC_PIANO_1, 1)
    bass = seq.add_track("Funk Bass", GM_ELECTRIC_BASS_FINGER, 2)
    guitar = seq.add_track("Funk Guitar", GM_ELECTRIC_GUITAR_CLEAN, 3)
    drums = seq.add_track("Drums", 0, 9)

    # G - Em - C - D (I-vi-IV-V), 2 bars per chord = 8-bar cycle (~19.2s).
    progression = [
        ("G",  "G1",  ["G3",  "B3",  "D4"]),
        ("Em", "E2",  ["E3",  "G3",  "B3"]),
        ("C",  "C2",  ["C3",  "E3",  "G3"]),
        ("D",  "D2",  ["D3",  "F#3", "A3"]),
    ]

    # Horn riff (8 bars, soul sample style).
    horn_riff = [
        ("D5", 0.5),   ("G5", 0.5),  ("B5", 1),    ("A5", 0.5),  ("G5", 0.5), ("D5", 1),
        ("G5", 1),     ("B5", 1),    ("D6", 2),
        ("B4", 0.5),   ("E5", 0.5),  ("G5", 1),    ("F#5", 0.5), ("E5", 0.5), ("B4", 1),
        ("E5", 1),     ("G5", 1),    ("B5", 2),
        ("E5", 0.5),   ("G5", 0.5),  ("C6", 1),    ("B5", 0.5),  ("G5", 0.5), ("E5", 1),
        ("G5", 1),     ("E5", 1),    ("C5", 2),
        ("F#5", 0.5),  ("A5", 0.5),  ("D6", 1),    ("C6", 0.5),  ("A5", 0.5), ("F#5", 1),
        ("A5", 1),     ("F#5", 1),   ("D5", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(horn, rep_start, horn_riff, vel=82)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            for bar in range(2):
                bar_start = chord_start + bar * 4

                _boom_bap_drums(seq, drums, bar_start, with_rim=True)

                # Bass: funky pattern (octave + fifth shuffle).
                seq.add_note(bass, bar_start + 0,    0.92, n(root) + 12, vel=92)
                seq.add_note(bass, bar_start + 0.75, 0.45, n(root) + 19, vel=72)
                seq.add_note(bass, bar_start + 2,    0.92, n(root) + 12, vel=88)
                seq.add_note(bass, bar_start + 3.5,  0.45, n(root) + 19, vel=72)

                # Rhodes: chord on beat 1 + offbeat 2.5.
                seq.add_chord(rhodes, bar_start + 0,   1.4, [n(p) for p in ch_pitches], vel=64)
                seq.add_chord(rhodes, bar_start + 2.5, 1.4, [n(p) for p in ch_pitches], vel=58)

                # Funk guitar: 16th-note muted strums (lighter accents than disco).
                _disco_chicken_scratch(seq, guitar, bar_start, [n(p) for p in ch_pitches[:3]],
                                       accent_idx=(0, 6, 8, 14))

    return seq.to_smf()


# =============================================================================
# 30. THUG LIFE - LOWRIDER SMOOTH: cruising lowrider feel (80 BPM, Bb minor)
# =============================================================================
def make_thug_lowrider_smooth(reps: int = 6) -> bytes:
    seq = Sequence(bpm=80)
    lead = seq.add_track("Smooth Lead", GM_VOICE_OOHS, 0)
    bass = seq.add_track("Sub Bass", GM_SYNTH_BASS_1, 1)
    strings = seq.add_track("Strings", GM_STRINGS, 2)
    keys = seq.add_track("Rhodes", GM_ELECTRIC_PIANO_1, 3)
    drums = seq.add_track("Drums", 0, 9)

    # Bbm7 - Eb7 - Abmaj7 - Gm7 (jazzy minor), 2 bars per chord = 8-bar cycle (~24s).
    progression = [
        ("Bbm7",   "Bb1", ["Bb3", "Db4", "F4",  "Ab4"]),
        ("Eb7",    "Eb1", ["G3",  "Bb3", "Db4", "F4"]),
        ("Abmaj7", "Ab1", ["Ab3", "C4",  "Eb4", "G4"]),
        ("Gm7",    "G1",  ["G3",  "Bb3", "D4",  "F4"]),
    ]

    smooth_lead = [
        ("F5", 1.5),   ("Ab5", 0.5), ("Bb5", 2),
        ("Ab5", 1),    ("F5", 1),    ("Db5", 2),
        ("Eb5", 1),    ("G5", 1),    ("Bb5", 2),
        ("Ab5", 0.5),  ("F5", 0.5),  ("Db5", 1),   ("Eb5", 2),
        ("C5", 1.5),   ("Eb5", 0.5), ("G5", 2),
        ("F5", 1),     ("Eb5", 1),   ("C5", 2),
        ("Bb4", 1),    ("D5", 1),    ("F5", 2),
        ("D5", 1),     ("Bb4", 1),   ("G4", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(lead, rep_start, smooth_lead, vel=80)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Strings: lush sustained chord across 2 bars.
            seq.add_chord(strings, chord_start, 8, [n(p) for p in ch_pitches], vel=54)

            for bar in range(2):
                bar_start = chord_start + bar * 4

                _boom_bap_drums(seq, drums, bar_start)

                # Sub bass: long held root + walk to next chord on beat 3.5 of bar 2.
                seq.add_note(bass, bar_start + 0, 3.4, n(root), vel=96)
                if bar == 1 and ci < 3:
                    next_root = progression[ci + 1][1]
                    seq.add_note(bass, bar_start + 3.5, 0.5, n(next_root), vel=86)

                # Rhodes: chord stab on 2.5 + sus chord on 4.5 (sparse).
                seq.add_chord(keys, bar_start + 2.5, 1.0, [n(p) for p in ch_pitches], vel=58)

    return seq.to_smf()


# =============================================================================
# 31. FLUTE - PAN FLUTE PASTORALE: gentle Zamfir-style (75 BPM, A major)
# =============================================================================
def make_flute_pan_flute_pastorale(reps: int = 5) -> bytes:
    seq = Sequence(bpm=75)
    flute = seq.add_track("Pan Flute", GM_PAN_FLUTE, 0)
    guitar = seq.add_track("Nylon Guitar", GM_NYLON_GUITAR, 1)
    strings = seq.add_track("Strings", GM_STRINGS, 2)
    harp = seq.add_track("Harp", GM_HARP, 3)

    # A - F#m - D - E (I-vi-IV-V), 2 bars per chord = 8 bars per cycle (~25.6s).
    progression = [
        ("A",   "A2",  ["A3",  "C#4", "E4"]),
        ("F#m", "F#2", ["F#3", "A3",  "C#4"]),
        ("D",   "D2",  ["D3",  "F#3", "A3"]),
        ("E",   "E2",  ["E3",  "G#3", "B3"]),
    ]

    melody_a = [
        ("A4", 1.5),   ("C#5", 0.5), ("E5", 2),
        ("F#5", 1),    ("E5", 1),    ("C#5", 2),
        ("D5", 1.5),   ("E5", 0.5),  ("F#5", 2),
        ("E5", 1),     ("D5", 1),    ("A4", 2),
        ("D5", 1),     ("F#5", 1),   ("A5", 2),
        ("G5", 1),     ("F#5", 1),   ("D5", 2),
        ("E5", 1),     ("G#5", 1),   ("B5", 2),
        ("A5", 1),     ("G#5", 1),   ("E5", 2),
    ]

    # Variation B - higher register, more elaborate
    melody_b = [
        ("E5", 1),     ("F#5", 1),   ("A5", 2),
        ("C#6", 1),    ("B5", 1),    ("A5", 2),
        ("F#5", 0.5),  ("A5", 0.5),  ("C#6", 1),   ("F#6", 2),
        ("E6", 1),     ("D6", 1),    ("A5", 2),
        ("F#5", 1),    ("A5", 1),    ("D6", 2),
        ("A5", 1),     ("F#5", 1),   ("D5", 2),
        ("B5", 0.5),   ("D6", 0.5),  ("E6", 1),    ("G#6", 2),
        ("E6", 1),     ("D6", 1),    ("B5", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # Flute drops out for cycle 1 (intro) and uses higher melody in alternating cycles.
        if rep >= 1:
            mel = melody_b if rep % 2 == 0 else melody_a
            seq.play_line(flute, rep_start, mel, vel=78)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 8

            # Strings: held chord for 2 bars.
            seq.add_chord(strings, chord_start, 8, [n(p) for p in ch_pitches], vel=44)

            # Guitar: gentle Travis-picking arpeggio.
            arp = [n(root), n(ch_pitches[1]), n(ch_pitches[2]), n(ch_pitches[1])]
            for bar in range(2):
                bs = chord_start + bar * 4
                for i in range(8):
                    seq.add_note(guitar, bs + i * 0.5, 0.45, arp[i % len(arp)], vel=58)

            # Harp: sparse upward sweep at start of each chord (2 bars).
            seq.add_note(harp, chord_start + 0, 0.5, n(ch_pitches[0]) + 12, vel=48)
            seq.add_note(harp, chord_start + 0.5, 0.5, n(ch_pitches[1]) + 12, vel=46)
            seq.add_note(harp, chord_start + 1, 1, n(ch_pitches[2]) + 12, vel=52)

    return seq.to_smf()


# =============================================================================
# 32. FLUTE - CELTIC REEL: Irish jig in 6/8 (180 BPM, D major)
# =============================================================================
def make_flute_celtic_reel(reps: int = 8) -> bytes:
    seq = Sequence(bpm=180, time_sig=(6, 3))
    flute = seq.add_track("Flute", 73, 0)  # GM Flute
    harp = seq.add_track("Harp", GM_HARP, 1)
    fiddle = seq.add_track("Fiddle", 40, 2)  # GM Violin
    drums = seq.add_track("Bodhran", 0, 9)

    # 16 bars in 6/8 (each bar = 3 beats / 6 eighths).
    # D - G - D - A | Bm - G - D - A (2x)
    progression = [
        ("D",  "D2",  ["D3",  "F#3", "A3"]),
        ("G",  "G1",  ["G3",  "B3",  "D4"]),
        ("D",  "D2",  ["D3",  "F#3", "A3"]),
        ("A",  "A1",  ["A3",  "C#4", "E4"]),
        ("Bm", "B1",  ["B3",  "D4",  "F#4"]),
        ("G",  "G1",  ["G3",  "B3",  "D4"]),
        ("D",  "D2",  ["D3",  "F#3", "A3"]),
        ("A",  "A1",  ["A3",  "C#4", "E4"]),
    ]

    # Jig melody: 6 eighth notes per bar (16 bars * 3 beats = 48 beats per cycle).
    # Each 8th = 0.5 beat. Each bar = 3 beats = 6 eighths.
    flute_line = [
        # Bar 1 (D)
        ("D5", 0.5),  ("F#5", 0.5), ("A5", 0.5), ("D6", 0.5),  ("A5", 0.5), ("F#5", 0.5),
        # Bar 2 (G)
        ("G5", 0.5),  ("B5", 0.5),  ("D6", 0.5), ("G5", 0.5),  ("D5", 0.5), ("B4", 0.5),
        # Bar 3 (D)
        ("F#5", 0.5), ("A5", 0.5),  ("D6", 0.5), ("A5", 0.5),  ("F#5", 0.5),("D5", 0.5),
        # Bar 4 (A)
        ("E5", 0.5),  ("A5", 0.5),  ("C#6", 0.5),("A5", 0.5),  ("E5", 0.5), ("C#5", 0.5),
        # Bar 5 (Bm)
        ("B4", 0.5),  ("D5", 0.5),  ("F#5", 0.5),("B5", 0.5),  ("F#5", 0.5),("D5", 0.5),
        # Bar 6 (G)
        ("D5", 0.5),  ("G5", 0.5),  ("B5", 0.5), ("D6", 0.5),  ("B5", 0.5), ("G5", 0.5),
        # Bar 7 (D)
        ("A5", 0.5),  ("F#5", 0.5), ("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 0.5),
        # Bar 8 (A) - turnaround
        ("E5", 0.5),  ("C#5", 0.5), ("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 0.5),
        # Bar 9 (D)
        ("D5", 0.5),  ("F#5", 1),   ("A5", 0.5), ("F#5", 0.5),("D5", 0.5),
        # Bar 10 (G)
        ("G5", 0.5),  ("B5", 1),    ("G5", 0.5), ("D5", 0.5),  ("B4", 0.5),
        # Bar 11 (D)
        ("F#5", 0.5), ("A5", 0.5),  ("D6", 0.5), ("F#6", 0.5), ("D6", 0.5), ("A5", 0.5),
        # Bar 12 (A)
        ("C#6", 0.5), ("E6", 0.5),  ("A5", 0.5), ("E5", 0.5),  ("C#5", 0.5),("A4", 0.5),
        # Bar 13 (Bm)
        ("D5", 0.5),  ("F#5", 0.5), ("B5", 0.5), ("D6", 0.5),  ("F#6", 0.5),("D6", 0.5),
        # Bar 14 (G)
        ("B5", 0.5),  ("D6", 0.5),  ("G6", 0.5), ("D6", 0.5),  ("B5", 0.5), ("G5", 0.5),
        # Bar 15 (D)
        ("F#5", 0.5), ("D5", 0.5),  ("A5", 0.5), ("F#5", 0.5), ("D6", 0.5), ("A5", 0.5),
        # Bar 16 (A) - cadence
        ("E5", 0.5),  ("A4", 0.5),  ("C#5", 0.5),("E5", 0.5),  ("A5", 1),
    ]

    # Fiddle countermelody (alternating cycles)
    fiddle_drone = [
        ("A4", 3), ("D4", 3),    # bars 1-2
        ("A4", 3), ("E4", 3),    # bars 3-4
        ("F#4", 3), ("D4", 3),   # bars 5-6
        ("A4", 3), ("E4", 3),    # bars 7-8
    ]

    for rep in range(reps):
        rep_start = rep * 48  # 16 bars * 3 beats per bar

        # Flute always plays after intro (cycle 0 = bodhran + harp only).
        if rep >= 1:
            seq.play_line(flute, rep_start, flute_line, vel=82)

        # Fiddle drone every 3rd cycle for variation.
        if rep >= 2 and rep % 3 == 0:
            seq.play_line(fiddle, rep_start, fiddle_drone, vel=58)
            seq.play_line(fiddle, rep_start + 24, fiddle_drone, vel=58)

        for ci, (cname, root, ch_pitches) in enumerate(progression):
            chord_start = rep_start + ci * 3  # 1 bar = 3 beats

            # Harp: arpeggiated chord (6 eighths) in 8va register.
            arp = [n(p) for p in ch_pitches] + [n(p) + 12 for p in ch_pitches[1:]]
            for i in range(6):
                seq.add_note(harp, chord_start + i * 0.5, 0.42, arp[i % len(arp)], vel=52)

            # Bodhran: low tom on beats 1 and 2 (the dotted-quarter pulses), light "buzz" on offbeats.
            seq.add_drum(drums, chord_start + 0, TOM_LOW, vel=88)
            seq.add_drum(drums, chord_start + 1.5, TOM_LOW, vel=72)
            seq.add_drum(drums, chord_start + 0.5, CLOSED_HAT, vel=42)
            seq.add_drum(drums, chord_start + 1, CLOSED_HAT, vel=38)
            seq.add_drum(drums, chord_start + 2, CLOSED_HAT, vel=42)
            seq.add_drum(drums, chord_start + 2.5, CLOSED_HAT, vel=38)

    return seq.to_smf()


# =============================================================================
# 33. FLUTE - SHAKUHACHI MEDITATION: sparse pentatonic Zen (60 BPM, A min pent)
# =============================================================================
def make_flute_shakuhachi_meditation(reps: int = 4) -> bytes:
    seq = Sequence(bpm=60)
    shakuhachi = seq.add_track("Shakuhachi", 77, 0)  # GM Shakuhachi
    koto = seq.add_track("Koto", 107, 1)  # GM Koto
    pad = seq.add_track("Pad", GM_PAD_WARM, 2)
    bell = seq.add_track("Bell", GM_GLOCKENSPIEL, 3)

    # Pentatonic A minor (A C D E G), held chord vamps for atmosphere.
    # 8 bars per cycle (~32s/cycle). Two 4-bar sections per cycle.
    chords = [
        ("Am",  "A2",  ["A3",  "C4",  "E4"]),
        ("Am",  "A2",  ["A3",  "C4",  "E4"]),
        ("Em",  "E2",  ["E3",  "G3",  "B3"]),
        ("Em",  "E2",  ["E3",  "G3",  "B3"]),
        ("Dm",  "D2",  ["D3",  "F3",  "A3"]),
        ("Dm",  "D2",  ["D3",  "F3",  "A3"]),
        ("Am",  "A2",  ["A3",  "C4",  "E4"]),
        ("Am",  "A2",  ["A3",  "C4",  "E4"]),
    ]

    # Sparse pentatonic shakuhachi line - long held notes with breathing space.
    flute_line = [
        # Bar 1-2 (Am): long held A4 then bend up
        ("A4", 3),     (None, 1),
        ("E5", 2),     ("D5", 1),    ("C5", 1),
        # Bar 3-4 (Em): higher register
        ("E5", 2),     ("G5", 2),
        ("A5", 1.5),   ("G5", 0.5),  ("E5", 2),
        # Bar 5-6 (Dm): introspective
        ("D5", 2),     ("F5", 2),
        ("E5", 1),     ("D5", 1),    ("A4", 2),
        # Bar 7-8 (Am): resolve down
        ("E5", 1),     ("C5", 1),    ("A4", 2),
        ("E4", 4),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(shakuhachi, rep_start, flute_line, vel=72)

        for ci, (cname, root, ch_pitches) in enumerate(chords):
            bar_start = rep_start + ci * 4

            # Pad: held chord, very soft.
            seq.add_chord(pad, bar_start, 4, [n(p) for p in ch_pitches], vel=42)

            # Koto: sparse plucked notes - single root + 5th, every 2 beats.
            seq.add_note(koto, bar_start + 0, 1.8, n(root), vel=68)
            seq.add_note(koto, bar_start + 2, 1.8, n(root) + 7, vel=58)

            # Bell: occasional zen accent (only on bar 4 and 8 of each cycle)
            if ci in (3, 7):
                seq.add_note(bell, bar_start + 3, 1, n(ch_pitches[0]) + 24, vel=48)

    return seq.to_smf()


# =============================================================================
# 34. FLUTE - BAROQUE SONATA: Telemann-style recorder + harpsichord (110 BPM, G maj)
# =============================================================================
def make_flute_baroque_sonata(reps: int = 8) -> bytes:
    seq = Sequence(bpm=110)
    recorder = seq.add_track("Recorder", 74, 0)  # GM Recorder
    harpsichord = seq.add_track("Harpsichord", 6, 1)  # GM Harpsichord
    cello = seq.add_track("Cello", 42, 2)  # GM Cello

    # G major progression in 8 bars: G - D - Em - Bm - C - G - C - D
    chords = [
        ("G",   "G2",  ["G3",  "B3",  "D4"]),
        ("D",   "D2",  ["D3",  "F#3", "A3"]),
        ("Em",  "E2",  ["E3",  "G3",  "B3"]),
        ("Bm",  "B1",  ["B3",  "D4",  "F#4"]),
        ("C",   "C2",  ["C3",  "E3",  "G3"]),
        ("G",   "G2",  ["G3",  "B3",  "D4"]),
        ("C",   "C2",  ["C3",  "E3",  "G3"]),
        ("D",   "D2",  ["D3",  "F#3", "A3"]),
    ]

    # Baroque recorder line: running 8th notes with ornaments.
    recorder_line = [
        # Bar 1 (G): G major 8th-note arpeggio with ornament
        ("G5", 0.5),  ("B5", 0.5),  ("D6", 0.5),  ("G5", 0.5),
        ("A5", 0.5),  ("B5", 0.5),  ("C6", 0.5),  ("A5", 0.5),
        # Bar 2 (D): D major
        ("F#5", 0.5), ("A5", 0.5),  ("D6", 0.5),  ("A5", 0.5),
        ("G5", 0.5),  ("F#5", 0.5), ("E5", 0.5),  ("D5", 0.5),
        # Bar 3 (Em): minor
        ("E5", 0.5),  ("G5", 0.5),  ("B5", 0.5),  ("E6", 0.5),
        ("D6", 0.5),  ("B5", 0.5),  ("G5", 0.5),  ("E5", 0.5),
        # Bar 4 (Bm): minor
        ("B4", 0.5),  ("D5", 0.5),  ("F#5", 0.5), ("B5", 0.5),
        ("A5", 0.5),  ("F#5", 0.5), ("D5", 0.5),  ("B4", 0.5),
        # Bar 5 (C): C major
        ("C5", 0.5),  ("E5", 0.5),  ("G5", 0.5),  ("C6", 0.5),
        ("B5", 0.5),  ("A5", 0.5),  ("G5", 0.5),  ("E5", 0.5),
        # Bar 6 (G): cadential
        ("D5", 0.5),  ("G5", 0.5),  ("B5", 0.5),  ("D6", 0.5),
        ("G6", 0.5),  ("D6", 0.5),  ("B5", 0.5),  ("G5", 0.5),
        # Bar 7 (C): pre-cadence
        ("E5", 0.5),  ("G5", 0.5),  ("C6", 0.5),  ("E6", 0.5),
        ("D6", 0.5),  ("C6", 0.5),  ("B5", 0.5),  ("A5", 0.5),
        # Bar 8 (D7): cadence
        ("F#5", 0.5), ("A5", 0.5),  ("D6", 0.5),  ("F#6", 0.5),
        ("E6", 0.5),  ("D6", 0.5),  ("C6", 0.5),  ("A5", 0.5),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        # Recorder plays the head melody throughout (it's the soloist).
        seq.play_line(recorder, rep_start, recorder_line, vel=82)

        for ci, (cname, root, ch_pitches) in enumerate(chords):
            bar_start = rep_start + ci * 4

            # Cello: walking bass quarters (basso continuo).
            cello_walk = [n(root), n(root) + 7, n(ch_pitches[1]), n(root) + 7]
            for i, p in enumerate(cello_walk):
                seq.add_note(cello, bar_start + i, 0.9, p, vel=72)

            # Harpsichord: figured-bass realization - chord on each beat (8th-note pulse).
            for off in (0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5):
                vel = 64 if off == int(off) else 50
                seq.add_chord(harpsichord, bar_start + off, 0.45,
                              [n(p) for p in ch_pitches], vel=vel)

    return seq.to_smf()


# =============================================================================
# 35. FLUTE - NATIVE SPIRIT: plaintive pentatonic with frame drum (66 BPM, F# min)
# =============================================================================
def make_flute_native_spirit(reps: int = 5) -> bytes:
    seq = Sequence(bpm=66)
    flute = seq.add_track("Native Flute", GM_PAN_FLUTE, 0)
    drone = seq.add_track("Drone Pad", GM_PAD_NEW_AGE, 1)
    drum = seq.add_track("Frame Drum", 0, 9)
    rattle = seq.add_track("Bell Tree", GM_GLOCKENSPIEL, 2)

    # F# minor pentatonic drone (F# A B C# E). Slow chord vamp.
    # 8 bars per cycle (~29.1s). Two 4-bar phrases.
    chords = [
        ("F#m",  "F#2",  ["F#3", "A3",  "C#4"]),
        ("F#m",  "F#2",  ["F#3", "A3",  "C#4"]),
        ("D",    "D2",   ["D3",  "F#3", "A3"]),
        ("E",    "E2",   ["E3",  "G#3", "B3"]),
        ("F#m",  "F#2",  ["F#3", "A3",  "C#4"]),
        ("A",    "A2",   ["A3",  "C#4", "E4"]),
        ("B",    "B1",   ["B3",  "D4",  "F#4"]),
        ("F#m",  "F#2",  ["F#3", "A3",  "C#4"]),
    ]

    # Pentatonic-only flute line (F# A B C# E pentatonic).
    flute_line = [
        # Bar 1-2 (F#m): plaintive opening
        ("F#5", 2),    ("A5", 2),
        ("C#6", 1.5),  ("B5", 0.5),  ("A5", 2),
        # Bar 3-4 (D, E): rising phrase
        ("F#5", 1),    ("A5", 1),    ("B5", 2),
        ("C#6", 2),    ("E6", 2),
        # Bar 5-6 (F#m, A): peak
        ("F#6", 2),    ("E6", 2),
        ("C#6", 1.5),  ("A5", 0.5),  ("F#5", 2),
        # Bar 7-8 (B, F#m): descend & rest
        ("E5", 1),     ("F#5", 1),   ("B5", 2),
        ("A5", 2),     ("F#5", 2),
    ]

    for rep in range(reps):
        rep_start = rep * 32

        if rep >= 1:
            seq.play_line(flute, rep_start, flute_line, vel=74)

        for ci, (cname, root, ch_pitches) in enumerate(chords):
            bar_start = rep_start + ci * 4

            # Drone pad: held chord across the bar, very soft.
            seq.add_chord(drone, bar_start, 4, [n(p) for p in ch_pitches], vel=42)

            # Frame drum: heartbeat pulse - low tom on 1 + 2.5 (loose two-beat feel).
            seq.add_drum(drum, bar_start + 0, TOM_LOW, vel=80)
            seq.add_drum(drum, bar_start + 2, TOM_LOW, vel=68)
            # Soft shake on offbeats.
            seq.add_drum(drum, bar_start + 1, TAMBOURINE, vel=44)
            seq.add_drum(drum, bar_start + 3, TAMBOURINE, vel=44)

            # Sparse bell-tree shimmer at start of every other 4-bar phrase.
            if ci in (3, 7) and rep >= 2:
                seq.add_note(rattle, bar_start + 3.5, 0.5, n("F#6"), vel=44)
                seq.add_note(rattle, bar_start + 3.75, 0.5, n("A6"), vel=42)

    return seq.to_smf()


CATEGORIES: dict[str, list[tuple[str, "Callable[[], bytes]", str]]] = {  # type: ignore[name-defined]
    # Retro game tunes, all >= 2 minutes (~125-130s, looped from short phrases).
    "retro": [
        ("Retro - Pixel Quest.mid",     lambda: make_pixel_quest(reps=9),     "Pixel Quest (~130s, 9x)"),
        ("Retro - Neon Arcade.mid",     lambda: make_neon_arcade(reps=21),    "Neon Arcade (~126s, 21x)"),
        ("Retro - Castle Crawl.mid",    lambda: make_castle_crawl(reps=12),   "Castle Crawl (~125s, 12x)"),
        ("Retro - Boss Showdown.mid",   lambda: make_boss_showdown(reps=22),  "Boss Showdown (~125s, 22x)"),
        ("Retro - Victory Fanfare.mid", lambda: make_victory_fanfare(reps=8), "Victory Fanfare (~128s, 8x)"),
    ],
    # Programming-background tunes: chill, focus-friendly, all >= 2 minutes.
    "programming": [
        ("Programming - Lo-Fi Loops.mid",         lambda: make_lofi_loops(),        "Lo-Fi Loops (~148s, 78 BPM, A min)"),
        ("Programming - Ambient Drift.mid",       lambda: make_ambient_drift(),     "Ambient Drift (~128s, 60 BPM, D maj)"),
        ("Programming - Coffee Shop Jazz.mid",    lambda: make_coffee_shop_jazz(),  "Coffee Shop Jazz (~153s, 88 BPM, F maj)"),
        ("Programming - Synthwave Cruise.mid",    lambda: make_synthwave_cruise(),  "Synthwave Cruise (~141s, 95 BPM, E min)"),
        ("Programming - Forest Meditation.mid",   lambda: make_forest_meditation(), "Forest Meditation (~133s, 72 BPM, A maj)"),
    ],
    # Solo-piano pieces (originals, not transcriptions), all >= 2 minutes.
    "piano": [
        ("Piano - Classical Etude.mid",      lambda: make_classical_etude(),     "Classical Etude (~125s, 92 BPM, C maj)"),
        ("Piano - Romantic Nocturne.mid",    lambda: make_romantic_nocturne(),   "Romantic Nocturne (~128s, 60 BPM, Bb maj)"),
        ("Piano - Ragtime Stride.mid",       lambda: make_ragtime_stride(),      "Ragtime Stride (~128s, 105 BPM, C maj)"),
        ("Piano - Minimalist Patterns.mid",  lambda: make_minimalist_patterns(), "Minimalist Patterns (~154s, 100 BPM, A min)"),
        ("Piano - Blues.mid",                lambda: make_blues_piano(),         "Blues Piano (~125s, 92 BPM, F)"),
    ],
    # Disco tunes - 4-on-the-floor, octave bass, strings, brass, all >= 2 minutes.
    "disco": [
        ("Disco - Saturday Groove.mid",   lambda: make_disco_saturday_groove(),   "Saturday Groove (~131s, 118 BPM, D maj)"),
        ("Disco - Night Fever.mid",       lambda: make_disco_night_fever(),       "Night Fever (~133s, 116 BPM, F# min)"),
        ("Disco - Funky Strut.mid",       lambda: make_disco_funky_strut(),       "Funky Strut (~125s, 108 BPM, E min)"),
        ("Disco - Diva Anthem.mid",       lambda: make_disco_diva_anthem(),       "Diva Anthem (~138s, 112 BPM, A min)"),
        ("Disco - Dance Floor Heat.mid",  lambda: make_disco_dance_floor_heat(),  "Dance Floor Heat (~135s, 128 BPM, C min)"),
    ],
    # Jazz tunes - swing, bebop, bossa, waltz, modal, all >= 2 minutes.
    "jazz": [
        ("Jazz - Smooth Standards.mid",   lambda: make_jazz_smooth_standards(),   "Smooth Standards (~144s, 80 BPM, F maj)"),
        ("Jazz - Bebop Burner.mid",       lambda: make_jazz_bebop_burner(),       "Bebop Burner (~140s, 192 BPM, C maj)"),
        ("Jazz - Bossa Nova.mid",         lambda: make_jazz_bossa_nova(),         "Bossa Nova (~128s, 120 BPM, D min)"),
        ("Jazz - Waltz.mid",              lambda: make_jazz_waltz(),              "Jazz Waltz (~135s, 96 BPM, A min, 3/4)"),
        ("Jazz - Cool Blue.mid",          lambda: make_jazz_cool_blue(),          "Cool Blue (~154s, 100 BPM, modal)"),
    ],
    # Thug-life tunes - g-funk, boom-bap, trap, soul, lowrider, all >= 2 minutes.
    "thuglife": [
        ("Thug Life - G-Funk Cruise.mid",        lambda: make_thug_g_funk_cruise(),         "G-Funk Cruise (~125s, 92 BPM, E min)"),
        ("Thug Life - East Coast Boom Bap.mid",  lambda: make_thug_east_coast_boom_bap(),   "East Coast Boom Bap (~131s, 88 BPM, A min)"),
        ("Thug Life - Trap Anthem.mid",          lambda: make_thug_trap_anthem(),           "Trap Anthem (~137s, 70 BPM, G min)"),
        ("Thug Life - Block Party.mid",          lambda: make_thug_block_party(),           "Block Party (~134s, 100 BPM, G maj)"),
        ("Thug Life - Lowrider Smooth.mid",      lambda: make_thug_lowrider_smooth(),       "Lowrider Smooth (~144s, 80 BPM, Bb min)"),
    ],
    # Flute pieces - five different flute family members, all >= 2 minutes.
    "flute": [
        ("Flute - Pan Flute Pastorale.mid",        lambda: make_flute_pan_flute_pastorale(),     "Pan Flute Pastorale (~128s, 75 BPM, A maj)"),
        ("Flute - Celtic Reel.mid",                lambda: make_flute_celtic_reel(),             "Celtic Reel (~128s, 180 BPM, D maj, 6/8)"),
        ("Flute - Shakuhachi Meditation.mid",      lambda: make_flute_shakuhachi_meditation(),   "Shakuhachi Meditation (~128s, 60 BPM, A min pent)"),
        ("Flute - Baroque Sonata.mid",             lambda: make_flute_baroque_sonata(),          "Baroque Sonata (~140s, 110 BPM, G maj)"),
        ("Flute - Native Spirit.mid",              lambda: make_flute_native_spirit(),           "Native Spirit (~145s, 66 BPM, F# min pent)"),
    ],
}


def main() -> None:
    import sys
    out_dir = os.path.dirname(os.path.abspath(__file__))

    args = [a for a in sys.argv[1:]]
    force = False
    requested: list[str] = []
    for a in args:
        la = a.lower()
        if la in ("-f", "--force", "--all"):
            force = True
        elif la in ("-h", "--help"):
            print("Usage: gen_retro_midi.py [CATEGORY ...] [--force]")
            print(f"  Categories: {', '.join(CATEGORIES.keys())}")
            print("  Without --force, files that already exist are skipped.")
            print("  Without arguments, all categories are processed.")
            return
        elif la.startswith("-"):
            print(f"  Unknown option: {a}")
            return
        else:
            if la not in CATEGORIES:
                print(f"  Unknown category: {a}. Available: {', '.join(CATEGORIES.keys())}")
                return
            requested.append(la)

    selected = requested if requested else list(CATEGORIES.keys())

    written = 0
    skipped = 0
    for cat in selected:
        for fname, builder, desc in CATEGORIES[cat]:
            path = os.path.join(out_dir, fname)
            if os.path.exists(path) and not force:
                print(f"  Skip  {fname:46s} (exists)")
                skipped += 1
                continue
            data = builder()
            with open(path, "wb") as f:
                f.write(data)
            print(f"  Wrote {fname:46s} {len(data):7d} bytes  | {desc}")
            written += 1

    print(f"\nWrote {written} file(s), skipped {skipped} existing in: {out_dir}")
    if not force and skipped > 0:
        print("(Pass --force to regenerate existing files; pass category names like 'disco' to scope.)")


if __name__ == "__main__":
    main()
