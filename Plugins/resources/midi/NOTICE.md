# MIDI Library — Notices and Credits

The `Plugins/resources/midi/` folder contains two clearly separate
groups of `.mid` files, each with a different copyright status. Read
this file before redistributing any of the bundled audio outside of
the normal Noted application.

This document is an attribution / status summary, not legal advice.

---

## 1. Originals created for Noted

Every file **except** those whose name begins with `Bach - `,
`Mozart - `, `Albeniz - ` or `Brahms - ` is an original made for this project,
produced procedurally by the script `gen_retro_midi.py` in this same
folder (developed with AI assistance). They are short, looping
retro / disco / jazz / hip-hop / flute / focus / 70s tracks created
to give the in-app MIDI Player something to play out of the box.

These originals are the work of the Noted authors and are distributed
under the same license as the rest of the Noted source code (see the
project's top-level `LICENSE` file, if any). Within those terms they
may be embedded, redistributed and modified.

The categories below are all originals:

```
Retro - *.mid               Programming - *.mid
Piano - *.mid               Disco - *.mid
Jazz - *.mid                Thug Life - *.mid
Flute - *.mid               70s - *.mid
70s Dance - *.mid           Plumber Groove - *.mid
Focus - *.mid               Trance - *.mid
```

---

## 2. Third-party classical transcriptions

The files in the following groups are **third-party MIDI
transcriptions of public-domain piano works**:

```
Bach - *.mid       (Preludes from BWV 846, 847, 850)
Mozart - *.mid     (Piano Sonatas K.311, K.330, K.331, K.332, K.333, K.545, K.570)
Albeniz - *.mid    (España Op.165 selections; Suite Espanola Op.47 in full)
Brahms - *.mid     (Sonata Op.1; Fantasien Op.116; Intermezzi Op.117; Klavierstücke Op.119)
```

Each file embeds its original copyright and source information as
standard MIDI text / copyright meta events. Open the file in any
sequencer or use a small script to read those events to see the
authoritative attribution and source URL for that specific file.

### What is and is not in the public domain

* The **musical compositions** themselves — the notes, melodies and
  harmonies written by J. S. Bach, W. A. Mozart, Isaac Albéniz and
  Johannes Brahms — are in the public domain worldwide. Anyone is
  free to perform, re-record or independently transcribe them.

* The **specific MIDI realisations** in this folder are *not* simply
  the public-domain works on their own. They are the result of
  detailed manual sequencing, voicing, dynamics and editing work by a
  third-party transcriber, which constitutes a separate creative
  effort and is protected by that transcriber's copyright. Those
  files are subject to the terms of use stated by their original
  source (see the embedded metadata in each `.mid` file).

### No grant of rights from this project

These transcriptions are bundled with Noted purely as a convenience so
that users have something pleasant to listen to in the MIDI Player
plugin. **The Noted project does not own these MIDI files and cannot
grant any license to them.** Their inclusion here should not be read
as permission, sub-license or warranty of any kind to redistribute,
broadcast, mirror, sell, sample, repackage or otherwise reuse them
outside of running the Noted application.

If you want to redistribute or otherwise reuse these specific MIDI
files, you must:

1. Obtain them directly from their original source (identified in the
   embedded metadata of each file); and
2. Comply with the terms of use stated by that original source at the
   time you obtain them.

If the original transcriber or any other rights holder objects to the
inclusion of these transcriptions in Noted, the affected files will be
removed on request.

---

## 3. Disclaimer

This file is a good-faith summary intended to make the situation
transparent. It is **not legal advice**. If your intended use goes
beyond running Noted on your own machine — for example, redistributing
the application's resources separately, hosting them online, using
them in a commercial product, or sampling them in a derivative work —
please consult:

* The Noted project's own license terms;
* The original source's terms of use (see the embedded metadata in
  each affected `.mid` file); and
* Qualified legal counsel where appropriate.
