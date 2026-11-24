"""능선을 따라 / Along the Ridgeline. Run field.py OUTPUT.wav.

A 64-bar D-minor / Dorian field cue in 4/4. The repeated three-three-two
accents come from the cellos, leaving the frame drummer free to play off them.
"""

from dataclasses import dataclass
from pathlib import Path
import random
import sys

from score import Score, midi


@dataclass(frozen=True)
class Section:
    harmony: str
    melody: tuple[str, ...]
    lead: str
    drive: float


# Each voicing lists the bass, its fifth or another consonant neighbour,
# then three playable inner voices. Keep the cellos clear of the melody.
VOICINGS = {
    "Dm":  "D2 A2 A3 D4 F4",
    "DmC": "C2 G2 A3 D4 F4",
    "DmF": "F2 A2 A3 D4 F4",
    "DmA": "A2 D3 A3 D4 F4",
    "C":   "C2 G2 G3 C4 E4",
    "CE":  "E2 G2 G3 C4 E4",
    "F":   "F2 C3 A3 C4 F4",
    "FC":  "C2 G2 A3 C4 F4",
    "G":   "G2 D3 B3 D4 G4",
    "Gm":  "G2 D3 Bb3 D4 G4",
    "GB":  "B2 D3 G3 B3 D4",
    "Bb":  "Bb2 F3 F3 Bb3 D4",
    "Am":  "A2 E3 A3 C4 E4",
    "As":  "A2 E3 A3 D4 E4",
    "A":   "A2 E3 A3 C#4 E4",
    "Em":  "E2 B2 G3 B3 E4",
}

# The main figure, D-A-F-E, comes from the first phrase of the login music.
# A held note leaves a bow change or a breath before the next onset.
TRACK = (
    Section("Dm DmC G C DmF Gm As A", (
        "-:0.75 D4:0.5 A4:0.75 F4:0.5 E4:1.5",
        "D4:1.5 -:0.5 A3:0.75 C4:0.5 D4:0.75",
        "G4:1.5 D4:0.5 B3:1 -:1",
        "-:1 G3:0.5 C4:0.75 E4:1.25 G4:0.5",
        "A4:1.5 F4:0.75 E4:0.75 D4:1",
        "Bb3:1 D4:1 G4:1.25 -:0.75",
        "E4:1.75 D4:0.5 A3:0.75 -:1",
        "-:2.5 E4:0.5 C#4:0.5 A3:0.5",
    ), "strings", .72),
    Section("Dm F C G DmC Gm DmA A", (
        "D4:0.75 A4:1.25 F4:0.75 E4:0.75 D4:0.5",
        "F4:1.5 A4:0.5 C5:1 -:1",
        "G4:0.75 E4:0.75 C4:0.5 E4:1.5 -:0.5",
        "D4:1 G4:1.5 B4:0.5 A4:1",
        "F4:1.5 E4:0.5 D4:0.75 A3:1.25",
        "-:0.5 Bb3:0.5 D4:0.5 F4:0.5 G4:1.5 F4:0.5",
        "E4:0.75 F4:0.75 D4:1.5 A3:1",
        "C#4:1.75 -:1.25 E4:0.5 A4:0.5",
    ), "horn", .86),
    Section("Dm CE F Gm Bb FC G A", (
        "A4:1.5 D5:0.5 C5:1 A4:1",
        "G4:0.75 E4:1.25 C4:1 -:1",
        "F4:0.75 A4:0.75 C5:0.5 D5:1.5 C5:0.5",
        "Bb4:2 G4:1 D4:1",
        "F4:0.5 D4:0.5 Bb3:0.75 D4:0.75 F4:1.5",
        "-:1 A4:0.75 G4:0.5 F4:0.75 C4:1",
        "B3:1 D4:0.5 G4:1.5 A4:1",
        "E4:2 -:1.25 C#4:0.75",
    ), "stringsF", .92),
    Section("DmA Am Dm G DmC CE Gm As", (
        "D5:1.5 -:1.5 A4:1",
        "C5:1 E5:0.75 C5:1.25 -:1",
        "-:0.5 A4:0.5 D5:2 F5:1",
        "E5:1 D5:1.5 B4:0.5 -:1",
        "A4:1.5 -:1 F4:0.75 A4:0.75",
        "G4:2 -:0.75 E4:0.5 G4:0.75",
        "Bb4:0.75 A4:0.75 G4:1.5 D4:1",
        "E4:2 -:1.5 A3:0.5",
    ), "flute", .63),
    Section("F CE Dm G C GB Am As", (
        "F4:1 A4:1.5 C5:0.75 A4:0.75",
        "G4:1.5 E4:1 -:1 C4:0.5",
        "D4:0.75 A4:0.75 F4:0.5 E4:0.5 D4:1.5",
        "B3:1 D4:1.5 G4:1 -:0.5",
        "G4:0.75 C5:1.25 B4:0.5 G4:1.5",
        "D5:1 B4:1.5 G4:0.75 D4:0.75",
        "E4:0.75 A4:1.25 C5:0.75 B4:0.75 A4:0.5",
        "E4:1.5 D4:1 E4:1.5",
    ), "horn", .89),
    Section("Dm Bb FC C Gm DmF Em A", (
        "D4:0.75 A4:0.75 D5:1.5 C5:1",
        "Bb4:1.5 F4:0.5 D4:1 -:1",
        "F4:0.75 A4:0.75 C5:0.5 F5:1 E5:1",
        "D5:0.75 C5:1.25 G4:1.5 -:0.5",
        "D5:1.5 Bb4:0.5 G4:0.75 F4:1.25",
        "A4:1.5 F4:0.75 D4:0.75 F4:1",
        "G4:1 B4:1 E5:0.75 D5:1.25",
        "C#5:1.75 A4:0.75 E4:1 -:0.5",
    ), "stringsF", 1.0),
    Section("Dm DmC G C F Gm DmA A", (
        "D4:1 A4:1 F4:0.75 E4:0.75 D4:0.5",
        "A3:1 -:0.5 C4:0.75 D4:0.75 F4:1",
        "G4:1.5 B4:0.5 D5:1 -:1",
        "C5:1 G4:1.75 E4:0.75 C4:0.5",
        "A4:1.5 F4:0.5 C4:0.75 D4:1.25",
        "G4:1.5 Bb4:0.5 A4:0.75 G4:1.25",
        "F4:1 D4:1 E4:1.5 -:0.5",
        "C#4:1.75 -:1.5 A3:0.75",
    ), "horn", .84),
    Section("Dm DmF Gm CE DmC Gm As A", (
        "-:0.75 D4:0.5 A4:0.75 F4:0.5 E4:1.5",
        "D4:1.5 A3:1 -:1.5",
        "-:1 Bb3:1 D4:0.75 G4:1.25",
        "E4:2 -:1 G3:1",
        "F4:1 D4:0.75 A3:1.25 -:1",
        "G3:0.75 Bb3:0.75 D4:1.5 -:1",
        "E4:1.5 A3:1 -:1.5",
        "-:2.5 A3:0.5 B3:0.5 C#4:0.5",
    ), "strings", .72),
)

# (beat, index in the root-neighbour-octave shape, gate, emphasis)
CELLO = (
    ((0,0,.39,.88), (.76,1,.24,.54), (1.49,2,.37,.79),
     (2.24,1,.27,.49), (2.99,0,.45,.80), (3.55,1,.23,.52)),
    ((0,0,.51,.85), (.99,1,.29,.59), (1.51,2,.45,.78),
     (2.49,1,.28,.59), (3.0,0,.46,.78)),
    ((0,0,.34,.89), (.52,1,.24,.47), (1.49,2,.49,.78),
     (2.24,1,.23,.52), (2.99,0,.32,.82), (3.49,1,.25,.53)),
    ((0,0,.55,.88), (1.0,1,.30,.60), (1.51,2,.36,.77),
     (2.51,1,.27,.57), (3.01,0,.63,.83)),
)

DRUM = (
    ((0,0,.68), (1.48,7,.37), (3.0,0,.54)),
    ((0,0,.60), (.99,12,.26), (2.50,7,.41), (3.48,12,.25)),
    ((0,0,.69), (1.50,7,.40), (2.23,12,.24), (3.0,0,.51)),
    ((0,0,.60), (2.00,7,.37), (2.74,12,.28)),
)


def arrange(score):
    rng = random.Random(106145)
    voicings = {name: list(map(midi, notes.split())) for name, notes in VOICINGS.items()}
    for section_index, section in enumerate(TRACK):
        for bar, (chord, phrase) in enumerate(zip(section.harmony.split(), section.melody, strict=True)):
            start = (section_index * 8 + bar) * 4
            bass, neighbour, low, middle, upper = voicings[chord]
            emphasis = section.drive * (1.03 if bar in (0, 4) else .95)
            walking = bass + 12 if bass < midi("C3") else bass
            neighbour = neighbour + 12 if neighbour < midi("C3") else neighbour
            steps = (walking, neighbour, walking + 12)

            # Off-beat heel strokes keep the gait legible without a snare or cymbal bed.
            quiet = section_index == 3
            for slot, (offset, degree, gate, gain) in enumerate(CELLO[bar % 4]):
                if quiet and slot not in (0, 2, 4):
                    continue
                score.note("strings", start + offset, steps[degree], gate,
                           gain * emphasis * .75, -.22, short=True)
            score.note("pizz", start + .008, bass, .83, .64 * emphasis, .10)
            if bar % 2 == 0 and not quiet:
                score.note("pizz", start + 2.99, bass, .60, .40 * emphasis, .10)
            if section_index in (2, 4, 5, 6) and bar in (0, 2, 4, 6):
                score.note("strings", start + .035, bass, 2.92, .32 * emphasis, .12, swell=.26)

            beat = start
            for token in phrase.split():
                name, count = token.split(":")
                length = float(count)
                if name != "-":
                    gate = .81 if length <= .75 else .9
                    score.note(section.lead, beat + rng.uniform(.012, .029), midi(name),
                               length * gate, (.84 if quiet else .93) * emphasis
                               * rng.uniform(.94, 1.03),
                               -.14 if quiet else .07, swell=.21)
                beat += length
            if beat != start + 4:
                raise ValueError(f"Incorrect melody length at section {section_index}, bar {bar}")

            # Pairs of quiet upper-string voices trade short suspensions against the bass.
            if section_index in (1, 2, 4, 5, 6) or bar % 2 == 0:
                onset = .55 if bar % 2 == 0 else .10
                score.note("strings", start + onset, middle, 2.65, .27 * emphasis, .36, swell=.38)
                score.note("strings", start + onset + .04, low, 2.51, .24 * emphasis, -.38, swell=.33)
            if section_index in (2, 5) and bar in (1, 3, 5):
                score.note("flute", start + 2.51, middle + 12, .58, .36 * emphasis, -.30)
                score.note("flute", start + 3.29, upper + 12, .59, .31 * emphasis, -.30)

            # A plucked offbeat figure takes over in the thinner middle section.
            if quiet or section_index == 4:
                pattern = ((.49, low), (1.01, upper), (2.49, middle + 12), (3.50, upper))
            elif bar % 2 == 1:
                pattern = ((.50, low), (2.49, middle), (3.51, upper))
            else:
                pattern = ((.50, middle), (2.25, upper))
            for i, (offset, pitch) in enumerate(pattern):
                score.note("lute", start + offset, pitch, .95, (.42 if quiet else .32)
                           * emphasis * (1 if i % 2 == 0 else .79), .27)

            strikes = DRUM[bar % 4]
            if quiet:
                strikes = strikes[:1] if bar % 2 == 0 else ()
            for offset, interval, gain in strikes:
                score.note("drum", start + offset + .012, 47 + interval, .62,
                           gain * emphasis * .64, -.07)
            if section_index in (1, 2, 4, 5) and bar == 7:
                for pos, pitch, level in ((3.12, 54, .22), (3.49, 59, .18), (3.73, 54, .28)):
                    score.note("drum", start + pos, pitch, .35, level * emphasis, -.07)


if __name__ == "__main__":
    band = Score(bpm=106, bars=64, beats_per_bar=4, seed=241511)
    arrange(band)
    band.export(Path(sys.argv[1]), loudness=-19.5, room=.7)
