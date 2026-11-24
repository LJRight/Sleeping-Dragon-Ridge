"""야영지의 아침 — a circular air in three for flute, lute, harp and strings."""

import sys
from pathlib import Path

from score import Score, midi


# Bass, tenor, alto and two possible upper strings. Keep the inner voices close
# while the bass moves by step under the broader flute intervals.
CHORDS = {
    "Dm":  "D3 A3 D4 F4 A4",  "DmC": "C3 A3 D4 F4 A4",
    "DmF": "F3 A3 D4 F4 A4",  "Bb":  "Bb2 F3 D4 F4 A4",
    "F":   "F3 A3 C4 F4 A4",  "FA":  "A2 F3 C4 F4 A4",
    "C":   "C3 G3 C4 E4 G4",  "CE":  "E3 G3 C4 E4 G4",
    "G":   "G2 D3 B3 D4 G4",  "Gm":  "G2 D3 Bb3 D4 G4",
    "Am":  "A2 E3 C4 E4 G4",  "AmE": "E3 A3 C4 E4 G4",
    "Cs":  "C3 G3 C4 F4 G4",  "BbD": "D3 F3 Bb3 D4 F4",
    "Em7": "E3 G3 B3 D4 G4",  "As":  "A2 E3 A3 D4 E4",
}

# The score is an eight-phrase round. Durations are in quarter notes. A rest
# at the head or tail of a line is part of the flute player's breathing.
PHRASES = [
    ("Dm DmC Bb F G DmF Am Dm", [
        "-:.5 D5:1 A5:.75 F5:.75", "E5:1.5 D5:1 -:.5",
        "F5:.75 G5:.25 A5:1.5 G5:.5", "F5:2 -:1",
        "G5:1 D5:1 B4:.5 D5:.5", "F5:1.5 E5:.5 D5:.75 -:.25",
        "C5:1 B4:.5 A4:1 -:.5", "D5:2.25 -:.75",
    ]),
    ("Dm FA Bb C Gm DmF CE Cs", [
        "-:.5 D5:1 A5:1 F5:.5", "E5:1 F5:.5 A5:1.25 -:.25",
        "Bb5:1 A5:.75 G5:.25 F5:1", "G5:1.5 E5:1 -:.5",
        "D5:.75 G5:.75 Bb5:1 A5:.5", "A5:1.5 F5:.5 D5:1",
        "E5:1 G5:1.25 -:.75", "F5:1 E5:1 -:1",
    ]),
    ("F CE Dm Bb FA Gm Bb C", [
        "A4:.5 C5:.5 F5:1.75 -:.25", "G5:1 E5:1 C5:.75 -:.25",
        "F5:1 A5:1 G5:1", "F5:2 D5:.5 F5:.5",
        "C5:1 F5:.5 A5:1.25 -:.25", "Bb5:1.5 G5:.75 F5:.75",
        "D5:1 F5:1 D5:.75 -:.25", "C5:1.5 -:1 G4:.5",
    ]),
    ("F Am Dm DmC Bb Gm Cs C", [
        "A4:.5 C5:.5 F5:1 G5:.5 A5:.5", "E5:1.5 C5:.75 E5:.75",
        "F5:1 A5:.75 C6:.75 A5:.5", "G5:1 E5:.75 F5:1 -:.25",
        "F5:.75 D5:.75 Bb4:1.25 -:.25", "G4:.5 Bb4:.5 D5:1 F5:1",
        "G5:1.5 F5:.75 D5:.75", "E5:2.25 -:.75",
    ]),
    ("BbD DmF G Em7 DmC FA Gm Am", [
        "D5:1 A5:1 F5:.5 E5:.5", "F5:1.5 D5:1 -:.5",
        "B4:.5 D5:.5 G5:1.5 F5:.5", "E5:1.5 B4:1 -:.5",
        "A4:.5 D5:1 F5:1 A5:.5", "C6:1 A5:1 F5:.75 -:.25",
        "Bb5:.75 G5:.75 F5:1 D5:.5", "C5:1.5 E5:.75 -:.75",
    ]),
    ("Dm C G DmF Bb F Gm As", [
        "-:.5 A4:.5 D5:1 F5:1", "E5:1 G5:1 E5:.75 -:.25",
        "D5:.5 G5:1 B5:1 -:.5", "A5:2 F5:1",
        "D5:.75 F5:.75 Bb5:1 A5:.5", "G5:.5 F5:1 A5:1 -:.5",
        "Bb5:1 G5:1 D5:.5 F5:.5", "E5:1.5 D5:.5 -:1",
    ]),
    ("Dm DmC Bb F G DmF Gm Am", [
        "-:.5 D5:1 A5:.75 F5:.75", "E5:1.5 D5:.75 A4:.75",
        "D5:.5 F5:.5 A5:.75 G5:.25 F5:1", "C5:1.75 -:.75 A4:.5",
        "B4:.75 D5:.75 G5:1 A5:.5", "F5:1.5 A5:.5 G5:.5 F5:.5",
        "D5:1.5 Bb4:.75 D5:.75", "C5:1.5 A4:.75 -:.75",
    ]),
    ("Bb FA Gm DmF BbD C DmC AmE", [
        "D5:1 F5:.5 A5:1 -:.5", "A5:.75 G5:.25 F5:1 C5:1",
        "D5:1.5 Bb4:.75 A4:.75", "A4:1 F5:1.5 -:.5",
        "F5:.5 E5:.5 D5:1.75 -:.25", "G4:1 C5:1 E5:.75 -:.25",
        "F5:1 D5:1 A4:.75 -:.25", "C5:1 -:1 A4:1",
    ]),
]

# Quiet answers left in the breaths of the principal tune. During the central
# harp solo, the flute answers from its low register instead.
ANSWERS = {
    1: "2 D5 .45|2.48 F5 .5", 3: "1.6 A4 .43|2.12 C5 .65",
    7: "1.9 A4 .4|2.41 C5 .55", 11: "1.8 E5 .46|2.48 C5 .45",
    15: "1.65 A4 .55|2.42 C5 .52", 19: "1.65 A4 .5|2.28 Bb4 .66",
    23: "1.45 G4 .46|2.05 C5 .72", 29: "1.8 D4 .46|2.43 F4 .48",
    31: "1.5 G4 .54|2.24 E4 .67", 33: "1.55 A4 .55|2.21 D5 .58",
    35: "1.65 G4 .5|2.32 B4 .54", 37: "1.81 A4 .51|2.41 C5 .46",
    39: "1.5 C5 .62|2.26 A4 .61", 43: "1.7 D5 .5|2.29 A4 .53",
    47: "1.84 E5 .45|2.42 A4 .47", 51: "1.47 A4 .43|2.01 C5 .46",
    55: "1.56 C5 .52|2.23 E5 .59", 59: "1.84 A4 .48|2.42 D5 .48",
    63: ".35 E4 .52|1.25 G4 .56|2.42 E4 .58",
}

# Each entry selects an offset, string and relative accent. The accompaniment
# changes hands at phrase boundaries and thins out for each cadence.
PICKING = [
    [(0, 0, 1), (.98, 2, .75), (1.52, 3, .53), (2.03, 1, .63)],
    [(0, 0, .96), (.53, 1, .59), (1.48, 3, .72), (2.05, 2, .68), (2.53, 1, .52)],
    [(0, 0, 1), (.98, 1, .66), (1.51, 3, .63), (2.45, 2, .60)],
    [(0, 0, .92), (.53, 2, .57), (1.53, 3, .64), (2.04, 1, .64)],
]


def render(path):
    score = Score(bpm=76.8, bars=64, beats_per_bar=3, seed=811)
    chord_notes = {key: list(map(midi, notes.split())) for key, notes in CHORDS.items()}
    flute_dynamics = [.80, .86, .82, .89, .72, .84, .87, .76]
    for section, (symbols, lines) in enumerate(PHRASES):
        for measure, (symbol, line) in enumerate(zip(symbols.split(), lines, strict=True)):
            bar = section * 8 + measure
            start = 3 * bar
            pitch = chord_notes[symbol]
            lift = [1, .95, 1.03, .90, 1, 1.035, .98, .86][measure]
            level = flute_dynamics[section] * lift
            beat = 0
            for event in line.split():
                name, duration = event.split(":")
                duration = float(duration)
                if name != "-":
                    if section == 4:
                        score.note("harp", start + beat + .018, name, duration,
                                   level * 1.13, -.10)
                    else:
                        hold = max(.18, duration * (.91 if duration < 1 else .955))
                        accent = 1.025 if beat <= .5 else .92
                        score.note("flute", start + beat + .031, name, hold,
                                   level * accent, .07, swell=.23)
                beat += duration
            if beat != 3:
                raise ValueError(f"Incorrect phrase length at bar {bar + 1}")

            # The lute alone sets the meter. An occasional fretted double stop
            # or pickup replaces the regular pattern at the end of a phrase.
            pattern = PICKING[(measure + section // 2) % len(PICKING)]
            if measure == 7:
                pattern = [(0, 0, 1), (1.02, 2, .65), (1.065, 3, .57)]
            if section == 4:
                pattern = [(0, 0, .95), (1.5, 1, .68), (2.03, 2, .58)]
            for pos, voice, accent in pattern:
                score.note("lute", start + pos + .012, pitch[voice], 1,
                           level * .75 * accent, -.27)
            if section in (1, 2, 3, 5, 6) and measure in (2, 4, 6):
                score.note("harp", start + 1.10, pitch[3] + 12, .85, level * .32, .29)
                score.note("harp", start + 2.57, pitch[2] + 12, .55, level * .29, .29)

            if not (section == 4 and measure % 2) and bar != 63:
                score.note("pizz", start + .014, pitch[0] - 12, .85, level * .46, .05)
            if section in (2, 3, 5, 6) and measure in (1, 5):
                score.note("pizz", start + 2.12, pitch[1] - 12, .58, level * .33, .04)

            # Sustained strings enter like slow breaths in the open middle
            # sections. Preserve broad pockets of silence around the solo.
            if section in (2, 3, 5, 6) or measure in (0, 2, 4, 6):
                sustain = 3.05 if measure in (0, 2, 4) else 2.57
                score.note("strings", start + .085, pitch[2], sustain,
                           level * (.22 if section in (2, 3, 5, 6) else .15), .29, swell=.48)
            if section in (2, 3, 6) and measure in (0, 2, 4, 6):
                score.note("strings", start + .14, pitch[1], 2.82,
                           level * .19, -.16, swell=.43)
            if bar in ANSWERS:
                answering_flute = section == 4
                for event in ANSWERS[bar].split("|"):
                    pos, name, duration = event.split()
                    score.note("flute" if answering_flute else "harp",
                               start + float(pos), name, float(duration),
                               level * (.63 if answering_flute else .69),
                               .07 if answering_flute else .28)
    score.export(path, loudness=-21, room=.69)


if __name__ == "__main__":
    render(Path(sys.argv[1]))
