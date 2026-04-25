# MIDI Library — Rules for Adding New Songs

This folder ships bundled with the Noted application. Every `.mid` file here is
embedded in the published `Noted.exe` (see `<Content>` in `Noted.csproj`).

When adding new tunes, follow these rules so they integrate cleanly with the
existing library and pass automated verification.

---

## TL;DR Checklist

A new song must satisfy ALL of the following:

1. **Length ≥ 2:00** — verified by `verify.ps1`
2. **No silent gaps ≥ 1.0s** anywhere in the song — verified by `find_quiet_gaps.py`
3. **Proper cadential ending** — final tonic resolution, not "stops mid-loop"
4. **Filename**: `Category - Song Name.mid` (ASCII only, no diacritics)
5. **Generator entry**: a `make_xxx()` function and a row in the `CATEGORIES` dict
6. **Generator-only**: never modify or regenerate the third-party Bach / Mozart / Albéniz transcriptions
7. **Build verified**: `dotnet build` passes; the new file appears under `bin/Debug/net9.0-windows/Plugins/resources/midi/`

---

## File layout

```
Plugins/resources/midi/
├── AGENTS.md              ← this file
├── gen_retro_midi.py      ← Python generator for our originals
├── verify.ps1             ← Length-verification (Windows MCI)
├── find_quiet_gaps.py     ← Silent-gap detector (pure Python)
├── Bach - *.mid           ← Third-party transcriptions (DO NOT regenerate)
├── Mozart - *.mid         ← Third-party transcriptions (DO NOT regenerate)
├── Albeniz - *.mid        ← Third-party transcriptions (DO NOT regenerate)
└── <Category> - *.mid     ← Generator output (regenerable)
```

The generator only produces files for our own categories. The classical
transcriptions are not in `CATEGORIES` and won't be touched by `--force`.

---

## Quality requirements (in detail)

### 1. Length ≥ 2:00

Calculate target length:

```
seconds = reps * beats_per_cycle * 60 / bpm
```

Note: Windows MCI rounds reported lengths down by up to ~1 second, so target
**at least 122–125s** to be safely above 2:00. If `verify.ps1` reports 1:59,
bump the `reps` parameter by 1.

Examples from existing tunes:

| BPM | beats/cycle | reps | total seconds |
|-----|-------------|------|---------------|
| 120 | 32          | 8    | 128           |
| 100 | 32          | 7    | 134           |
| 70  | 32          | 5    | 137           |
| 180 | 48 (6/8)    | 8    | 128           |
| 96  | 24 (3/4)    | 9    | 135           |

### 2. No quiet time

A "silent gap" is any period during which **no notes are sounding on any
channel**. The threshold is **1.0 second**. The user's complaint pattern was:
"pause for a few seconds and then something else starts" — that must never
happen in a generated song.

Rules to prevent this:

- **Lead plays from cycle 0.** Never use `if rep >= 1: seq.play_line(lead, ...)`
  unless other instruments cover the entire cycle's duration without gaps.
- **Rhythm section is continuous.** Drums, bass, and at least one
  comp/chord instrument fire on every bar of every cycle.
- **The chord-loop iteration must cover the full cycle.** A common bug:
  declaring `rep_start = rep * 48` (16 bars worth) but iterating only 8
  chords (8 bars worth). The remaining 8 bars become silent. Fix: iterate
  the chord progression twice per cycle, OR reduce the cycle length.
- If you want sectional variation, drop a *non-essential* layer (countermelody,
  bell sparkle, fiddle drone) — **never** drop the lead, rhythm guitar, bass,
  or drums together.

After generating, **always run `find_quiet_gaps.py`** to confirm.

### 3. Nice ending

Every song must end with a deliberate musical resolution after the final loop.
The standard pattern (used in `seventies` and `seventiesdance` categories):

```python
# After the for-rep loop:
final = reps * BEATS_PER_CYCLE
TONIC_CHORD = [n("RootLow"), n("Third"), n("Fifth"), n("RootHigh")]
TONIC_BASS  = n("RootLow") - 12  # extra-low octave for weight

seq.add_chord(rhythm_or_pad, final, 6, TONIC_CHORD, vel=82)
seq.add_note(bass, final, 6, TONIC_BASS, vel=104)
seq.add_note(lead, final, 5, n("RootHigh"), vel=92)  # final lead note
seq.add_drum(drums, final, CRASH, vel=120)
seq.add_drum(drums, final, KICK, vel=110)
```

Style-specific touches are encouraged: bell arpeggio fade for psychedelic /
roller-disco, mandolin tremolo for folk-rock, brass tonic chord for soul/dance,
9th-chord for ballads/yacht rock. Pick the one that fits the genre.

The final chord should be held **4–8 beats** so it rings out naturally.

---

## Naming conventions

### Filename format

```
Category - Song Name.mid
```

- Single space and a hyphen surround the genre prefix (`Disco - `, `Jazz - `, `70s - `, `70s Dance - `).
- Title case for the song name.
- **ASCII only** — no `é`, `ñ`, `ö` etc. (use `Albeniz`, `Espana`, `Malaguena`, not `Albéniz`/`España`/`Malagueña`). This keeps filenames git-portable across platforms.
- Avoid emojis or special punctuation other than `()` (e.g. `Bach - Prelude in C major (BWV 846).mid` is OK).
- Hyphens within the song name are fine (`Hi-NRG`, `Boom Bap`).

### Category keys

The CLI category names (dict keys in `CATEGORIES`) are lowercase, single-word identifiers, no spaces or hyphens. Current keys:

```
retro  programming  piano  disco  jazz  thuglife  flute  seventies  seventiesdance
```

When adding a new genre, pick a clean single-word key (e.g. `latin`, `electronic`, `metal`). Don't reuse existing prefixes.

---

## How to add a new song

1. **Decide the category**:
   - Adding to an existing category? Just add a new builder function and a new row in that category's list.
   - New category? Create a new entry in `CATEGORIES` with a fresh key + filename prefix.

2. **Write the builder function** in `gen_retro_midi.py`. Place it near the other tunes for that category. Naming convention: `make_<category>_<song_slug>(reps: int = N) -> bytes`.

3. **Use shared helpers** where applicable to keep code consistent:

   | Helper | Use for |
   |--------|---------|
   | `_disco_drums(seq, drums, bar_start, ...)` | 4-on-the-floor kick, snare on 2/4, closed+open hat |
   | `_disco_bass_octave(seq, bass, bar_start, root)` | 8th-note octave-bouncing bass |
   | `_disco_chicken_scratch(seq, guitar, bar_start, chord)` | 16th-note muted strums |
   | `_jazz_swing_ride(seq, drums, bar_start)` | Spang-a-lang swung ride pattern |
   | `_jazz_walking_bass(seq, bass, bar_start, notes)` | 4 quarter-note walking bass |
   | `_jazz_hat_foot(seq, drums, bar_start)` | Hi-hat pedal on 2 + 4 |
   | `_boom_bap_drums(seq, drums, bar_start)` | Hip-hop kick on 1+3.5, snare on 3 |
   | `_trap_drums(seq, drums, bar_start, fill=False)` | Trap kick + clap + 16th hats with optional 32nd-note rolls |

4. **Apply the structure**:
   ```python
   def make_<category>_<name>(reps: int = N) -> bytes:
       seq = Sequence(bpm=BPM)
       lead    = seq.add_track("Lead",    GM_LEAD_PROGRAM,    0)
       bass    = seq.add_track("Bass",    GM_BASS_PROGRAM,    1)
       comp    = seq.add_track("Comp",    GM_COMP_PROGRAM,    2)
       drums   = seq.add_track("Drums",   0,                  9)

       progression = [
           ("Chord", "RootLow", ["Pitch1", "Pitch2", "Pitch3"]),
           ...  # 4 chords typically
       ]

       melody = [("Note", duration), ...]  # for the full cycle

       for rep in range(reps):
           rep_start = rep * BEATS_PER_CYCLE
           seq.play_line(lead, rep_start, melody, vel=...)   # CYCLE 0 INCLUDED
           for ci, (cname, root, voicing) in enumerate(progression):
               chord_start = rep_start + ci * BARS_PER_CHORD * 4
               # ... bass, drums, comp for this chord ...

       # NICE ENDING (mandatory):
       final = reps * BEATS_PER_CYCLE
       # ... tonic chord, tonic bass, final lead note, crash + kick ...
       return seq.to_smf()
   ```

5. **Register** the song in `CATEGORIES`:
   ```python
   ("Category - Song Name.mid", lambda: make_xxx(), "Description (~SECs, BPM, KEY)"),
   ```

6. **Generate it**:
   ```bash
   python Plugins/resources/midi/gen_retro_midi.py <category>
   ```
   The generator skips files that already exist by default. Pass `--force` to regenerate everything in the category.

7. **Verify both** quality requirements:
   ```bash
   powershell -NoProfile -ExecutionPolicy Bypass -File "Plugins/resources/midi/verify.ps1" "Category*.mid"
   python Plugins/resources/midi/find_quiet_gaps.py "Category*.mid"
   ```

   - `verify.ps1` must show **OK (>=2:00)** for the new file.
   - `find_quiet_gaps.py` must show **ok** (no gaps).

   If `verify.ps1` reports SHORT: bump `reps` by 1 and regenerate.
   If `find_quiet_gaps.py` reports a GAP: see the rules in §2 above.

8. **Build**:
   ```bash
   dotnet build Noted.csproj -c Debug -nologo
   ```
   The new `.mid` file should appear in `bin/Debug/net9.0-windows/Plugins/resources/midi/` automatically (no csproj change needed; the `<Content Include="Plugins\resources\midi\*.mid">` glob picks up new files).

---

## Code conventions

### GM program constants

Use the named constants at the top of `gen_retro_midi.py` (e.g. `GM_TENOR_SAX`, `GM_DISTORTION_GUITAR`). If you need a new GM program, add a constant rather than inlining a magic number. Group new constants with the related family (drums, brass, etc.).

### Velocity ranges (suggested)

| Layer | Range |
|-------|-------|
| Lead melody | 78 – 92 |
| Backing chord/comp | 56 – 72 |
| Bass | 78 – 95 |
| Held pad / strings | 42 – 60 |
| Kick (downbeats) | 95 – 115 |
| Snare (backbeat) | 95 – 110 |
| Closed hi-hat | 40 – 60 |
| Open hat | 70 – 85 |
| Crash (ending) | 115 – 127 |
| Hand-clap | 88 – 100 |

Higher velocities for accents and downbeats; lower for "ghost" notes and offbeat hat ticks.

### Tempo + cycle math

A `Sequence` beat is one quarter note. `bpm` is the quarter-note rate. For non-4/4 time signatures, set `time_sig=(num, denom_pow2)`:

| Time sig | Bar = N beats | `time_sig` |
|----------|---------------|------------|
| 4/4 | 4 | `(4, 2)` (default) |
| 3/4 | 3 | `(3, 2)` |
| 6/8 | 3 (six 8ths) | `(6, 3)` |
| 12/8 | 6 (twelve 8ths) | `(12, 3)` |

The actual beat positions you pass to `add_note` / `add_chord` are still
quarter-note units — the SMF time signature is informational metadata.

### Note durations

The `Sequence.add_note` helper subtracts ~4 ticks from each note's end so
consecutive same-pitch notes retrigger cleanly. You don't need to pad
durations manually — pass the natural duration in beats.

### Tracks vs channels

`add_track(name, program, channel)`:
- The first parameter is the **track name** shown in DAWs (cosmetic).
- The third is the **MIDI channel** (0–15). Channel **9 is GM drums**.
- Different tracks can share a channel (e.g. piano LH/RH on the same channel) but typically each track owns its own channel for cleanliness.
- One song uses 4–6 tracks; more is fine, but stay under ~10 to avoid synth-engine voice exhaustion.

---

## CLI reference for the generator

```bash
# Generate everything that doesn't exist yet (default behavior).
python gen_retro_midi.py

# Regenerate ONE category.
python gen_retro_midi.py disco

# Regenerate MULTIPLE categories.
python gen_retro_midi.py jazz thuglife

# Force-regenerate (overwrite existing files).
python gen_retro_midi.py --force
python gen_retro_midi.py disco --force

# Help.
python gen_retro_midi.py --help
```

The default behavior of skipping existing files is critical — it prevents you from accidentally clobbering manually-edited or hand-tuned MIDI content.

---

## Verification scripts

### `verify.ps1` (length check, Windows-only)

Uses `winmm.dll`'s MCI sequencer (the same path the in-app MIDI Player plugin uses) to open each file and query its length.

```bash
# All files
powershell -NoProfile -ExecutionPolicy Bypass -File verify.ps1

# Glob filter (use quotes — PowerShell is picky about wildcards)
powershell -NoProfile -ExecutionPolicy Bypass -File verify.ps1 "Disco*.mid"

# Custom minimum-length threshold (in seconds)
powershell -NoProfile -ExecutionPolicy Bypass -File verify.ps1 -MinSeconds 90
```

Exit code = number of files that failed to open. 0 means all healthy.

### `find_quiet_gaps.py` (silent-gap detector, cross-platform)

Pure Python, no dependencies. Parses SMF directly and reports any period of total silence ≥ threshold.

```bash
# All files, default 1.0s threshold
python find_quiet_gaps.py

# Glob filter
python find_quiet_gaps.py "Jazz*.mid"

# Tighter threshold (catches shorter rests)
python find_quiet_gaps.py --min 0.5
```

Exit code = number of files with at least one gap above the threshold. 0 means clean.

---

## DO NOT

- ❌ **Don't regenerate** the third-party transcriptions (`Bach - *.mid`, `Mozart - *.mid`, `Albeniz - *.mid`). They are not in `CATEGORIES` and the generator won't touch them — keep it that way.
- ❌ **Don't use** `if rep >= 1: seq.play_line(lead, ...)` patterns that leave cycle 0 silent unless rhythm fully covers it. This was the cause of the 8-second silent gap in the original Celtic Reel.
- ❌ **Don't end** songs at the last note of the last cycle. Always add the cadential ending block.
- ❌ **Don't use diacritics** in filenames (no `é`, `ñ`, `ö`).
- ❌ **Don't add** non-`.mid` files that should ship with the build. The csproj `<Content>` glob is `*.mid` only — `.py` and `.ps1` dev tools stay out of the build.
- ❌ **Don't commit** the `verify.ps1` or `find_quiet_gaps.py` output as files — they're tools, not artifacts.
- ❌ **Don't change** the existing `Noted.csproj` `<Content>` glob unless you know what you're doing — adding new file types here will bundle them into the published exe.

---

## Lessons learned (for future reference)

These are real bugs/issues encountered earlier in this library's history; the
above rules exist to prevent them from recurring.

1. **The 8-second silent gap (Celtic Reel)** — chord-loop iteration covered only half of the cycle. Always make sure your inner loop fills `rep_start` to `rep_start + BEATS_PER_CYCLE`.

2. **The 1:59 length problem (Dance Floor Heat, Jazz Waltz)** — calculated cycles were exactly 120s, but MCI reported 1:59. Always target ≥125s.

3. **Composer name conflicts (piano pieces)** — generated piano tracks were originally to be tagged `Bach - Etude.mid` etc., but that would have collided in sorted listings with the actual Bach transcriptions. Solution: generic `Piano - ` prefix for our originals.

4. **ASCII filenames** — Albéniz files originally proposed with diacritics (`Espa**ñ**a`, `Malague**ñ**a`); changed to ASCII for git portability.

5. **Self-extracting single-file build** — the publish script uses `IncludeAllContentForSelfExtract=true`, so MIDI files are embedded in the exe and extracted to a temp folder at runtime. End users won't see them as visible files alongside the exe — by design. Don't promise users they can browse the .mid files unless we change the publish strategy.

---

## Summary of categories (as of last update)

| Category key | Count | Style overview |
|--------------|------:|----------------|
| `retro` | 5 | 8/16-bit game music (overworld, arcade, dungeon, boss, fanfare) |
| `programming` | 5 | Focus-friendly background (lo-fi, ambient, jazz, synthwave, forest) |
| `piano` | 5 | Solo piano originals (etude, nocturne, ragtime, minimalist, blues) |
| `disco` | 5 | Disco subgenres (Saturday, Night Fever-style, funk, diva, Eurodisco) |
| `jazz` | 5 | Jazz styles (swing ballad, bebop, bossa, waltz in 3/4, modal) |
| `thuglife` | 5 | Hip-hop (g-funk, boom-bap, trap, soul, lowrider) |
| `flute` | 5 | Flute family (pan flute, Celtic, shakuhachi, recorder/baroque, native) |
| `seventies` | 10 | 70s genres (rock, funk, soul, folk, prog, reggae, yacht, glam, country, psych) |
| `seventiesdance` | 10 | 70s dance subgenres (Hustle, Philly soul, Latin, Boogie, Soul Train, Eurodisco, Hi-NRG, jazz-funk, Salsoul, Roller disco) |
| **Generated total** | **55** | |
| Classical (transcriptions) | 11 | Bach × 3, Mozart × 5, Albéniz × 3 |
| **Library total** | **66** | |
