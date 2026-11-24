"""용이 눈을 뜨면. A 160 BPM boss cue in F-sharp. Run boss.py OUTPUT.wav."""

from pathlib import Path
import sys

import numpy as np

from score import RATE, Score
from boss_percussion import percussion


BPM = 160
# Step changes under a fixed F# pedal. The semitone and tritone, together
# with the gap after the second accent, are the core of the boss's riff.
FS = 30
SECTIONS = (
    ("chase",  "0 0 1 0 0 -2 1 0  0 3 0 -2 1 1 6 0"),
    ("build1", "0 0 1 1 3 3 6 6"),
    ("drop1",  "0 0 0 1  0 -2 1 6  0 0 3 1  -2 -2 1 0"),
    ("prowl",  "0 0 -2 -2  1 1 0 0  -3 -3 0 1  3 -2 1 0"),
    ("build2", "0 0 1 0  -2 1 3 1  0 1 3 3  5 5 6 6"),
    ("drop2",  "0 1 0 0  -2 3 1 6  0 0 -3 -2  3 1 6 0"),
    ("return", "0 1 0 -2  0 3 1 6  0 -2 1 0"),
)

# Each cello attack has a sixteenth-note position and an interval above
# the current pedal. The clipped offbeats let the drum answer the bow.
CHASE = (
    ((0,0), (2,0), (3,1), (5,0), (8,6), (10,6), (13,1), (15,0)),
    ((0,0), (3,1), (4,0), (7,0), (9,6), (11,1), (14,0)),
    ((0,0), (1,0), (4,1), (6,0), (9,6), (12,0), (14,1)),
    ((0,0), (3,1), (6,0), (8,6), (11,1), (12,0), (15,-1)),
)
# Downbeats stretch in the first drop; the second drop bites back twice
# in the space the listener initially heard as one long accent.
FIRST_DROP = (
    ((0,0,2.6), (5,0,1.4), (7,1,1.1), (10,6,2.2), (14,0,1.45)),
    ((0,0,2.1), (4,0,1.1), (6,1,1.6), (10,0,1.5), (13,-2,2.4)),
    ((0,0,3.3), (6,6,1.6), (9,1,1.2), (11,0,2.1), (15,1,.9)),
    ((0,0,2.8), (5,1,1.4), (8,0,1.7), (12,6,2.7)),
)
SECOND_DROP = (
    ((0,0,1.6), (2,0,.85), (3,1,.9), (5,0,1.6), (8,6,1.6), (10,0,.9), (12,1,1.5), (15,0,.9)),
    ((0,0,1.8), (3,6,1.5), (5,1,.9), (7,0,.95), (9,0,2), (12,-2,1.4), (14,0,1.3)),
    ((0,0,2.3), (4,1,1), (5,0,1.6), (8,6,1.4), (10,6,1), (11,1,1.6), (14,0,1.5)),
    ((0,0,1.5), (2,0,.85), (3,1,1), (6,6,2), (10,1,1.4), (12,0,2.4), (15,-1,.85)),
)


def bass(score, beat, key, gate, velocity, bend=0):
    """A centered, lightly driven bass with audible harmonics on small speakers."""
    held = gate * score.seconds_per_beat
    release = .09
    t = np.arange(round((held + release) * RATE)) / RATE
    hz = 440 * 2 ** ((key - 69) / 12)
    # Pitch bends occur on phrase endings. Leave the note's attack in tune.
    slide = np.clip((t - held * .55) / max(held * .45, .001), 0, 1)
    cycles = np.cumsum(hz * 2 ** (bend * slide / 12)) / RATE
    phase = 2 * np.pi * cycles
    edge = .57 + .40 * np.exp(-10 * t)
    body = np.sin(phase) + edge * np.sin(2 * phase) + .34 * np.sin(3 * phase) + .18 * np.sin(5 * phase)
    tone = .28 * np.tanh(1.22 * body) + .045 * np.sin(4 * phase + .55 * np.sin(phase))
    contour = (1 - np.exp(-170 * t)) * (.72 + .28 * np.exp(-9 * t))
    contour *= np.clip((held + release - t) / release, 0, 1) ** 2
    signal = (velocity * tone * contour).astype(np.float32)[:, None]
    at = round(beat * score.seconds_per_beat * RATE) % score.frames
    count = min(len(t), score.frames - at)
    score.mix[at:at+count] += signal[:count]
    if count < len(t):
        score.mix[:len(t)-count] += signal[count:]


def chase(score, bar, root, returning):
    start = bar * 4
    for step, interval in CHASE[bar % 4]:
        pitch = root + 24 + interval
        weight = .74 if step in (0, 6, 8, 12) else .48
        score.note("stringsF", start + step / 4, pitch, .21 if step % 4 else .32,
                   weight, -.28, short=True)
    # A stretched note answers a bar of busy spiccato from the other side.
    if bar % 4 in (1, 3):
        for pos, pitch, dur in ((.50, 18, .63), (2.25, 13, .36), (2.75, 12, .78)):
            score.note("trombones", start+pos, root+pitch, dur, .63, .20, short=dur < .6)
    bass(score, start, root, .81, .72)
    bass(score, start+2.25, root+(1 if bar % 2 else 0), .56, .60)
    if bar % 4 == 0:
        score.note("strings", start+.49, root+42, 1.38, .25, .47, swell=.55)
    if returning and bar >= 96:
        for pos, delta in ((1.50, 13), (3.25, 12)):
            score.note("stringsF", start+pos, root+delta+24, .27, .42, .42, short=True)
    if bar == 99:
        # A sustained leading-tone pickup and the room ring into bar one.
        score.note("trombones", start+3.45, FS+11, .66, .50, .16)
        bass(score, start+3.50, FS-1, .57, .47, bend=1)


def build(score, bar, local, length, root):
    start = bar * 4
    strength = local / (length-1)
    # Leave almost two beats of air after the final choking brass stab.
    stop = (2 if bar == 23 else 1.5) if local == length-1 else 4
    sequence = (0, 1, 0, 6, 7, 6, 1, 0)
    spacing = .50 if strength < .42 else .25
    for i in range(round(stop / spacing)):
        pos = i * spacing
        pitch = root + 24 + sequence[(i+local//2) % 8]
        gain = (.35 + .30 * strength) * (1 if i % 3 == 0 else .71)
        score.note("stringsF", start+pos, pitch, min(.25, spacing*.77),
                   gain, -.24, short=True)
    if stop < 4:
        score.note("trombones", start+.02, root+18, .84, .66, .15, short=True)
        return
    if strength < .65:
        bass(score, start, root, 1.35, .60)
    # As the rolls accelerate, suspend the low end and pull the violins up.
    voice = FS + 36 + (0, 1, 3, 6, 7, 9, 11, 12)[min(7, int(strength * 8))]
    score.note("strings", start+.18, voice, 3.31, .27 + .20 * strength, .39, swell=.51)
    if local % 2 == 0:
        score.note("horns", start+1.05, root+24, 1.75, .35 + .12 * strength, -.04, swell=.53)


def drop(score, bar, root, second):
    start = bar * 4
    riff = (SECOND_DROP if second else FIRST_DROP)[bar % 4]
    for step, interval, length in riff:
        pos, gate = step / 4, length / 4
        onset = start+pos
        down = step == 0
        accent = 1.0 if down else (.78 if step % 2 == 0 else .66)
        bass(score, onset, root+interval, gate, accent * (1.42 if second else 1.35),
             bend=-2 if step >= 12 and bar % 4 == 3 else 0)
        score.note("stringsF", onset+.008, root+12+interval, gate+.045,
                   .80*accent, -.27, short=True)
        # The brass sounds in blocks, with space after each response.
        if down or step in (5, 6, 8, 10, 13) and bar % 4 != 2:
            score.note("trombones", onset+.010, root+12+interval, min(.86, gate+.18),
                       .86*accent, .19, short=True)
            if down or second:
                score.note("horns", onset+.022, root+24+interval, min(.74, gate+.11),
                           .65*accent, -.12, short=True)
    if second:
        # A choked, answering desk in the upper octave adds motion to drop two.
        contour = (7, 6, 1, 0) if bar % 2 == 0 else (1, 0, 7, 6)
        for pos, semitone in zip((.75, 1.75, 2.75, 3.25), contour):
            score.note("stringsF", start+pos+.017, root+36+semitone,
                       .35, .51, .47, short=True)
    elif bar % 4 in (0, 2):
        score.note("strings", start+.25, root+37, 1.08, .34, .49, swell=.4)
        score.note("strings", start+1.44, root+36, 1.52, .37, .49, swell=.4)


def prowl(score, bar, local, root):
    start = bar * 4
    # Keep the pulse under the breakdown; the register and gaps change.
    for pos, interval, hold in ((0, 0, .44), (.75, 1, .24), (2.5, 0, .57)):
        score.note("pizz", start+pos, root+12+interval, hold, .64, -.18)
    if bar % 2 == 0:
        score.note("horn", start+1, root+30, 1.64, .43, .19, swell=.48)
        score.note("strings", start+2.75, root+25, .83, .33, .40, swell=.42)
    if local >= 8:
        bass(score, start, root, 1.21, .60)
        for pos in (1.25, 1.75, 3, 3.5):
            score.note("stringsF", start+pos, root+24+(1 if pos == 1.75 else 0),
                       .28, .45, -.27, short=True)


def compose(path):
    score = Score(bpm=BPM, bars=100, beats_per_bar=4, seed=16538)
    bar = 0
    for section, changes in SECTIONS:
        roots = [FS + int(interval) for interval in changes.split()]
        for local, root in enumerate(roots):
            percussion(score, bar, section, root)
            if section.startswith("build"):
                build(score, bar, local, len(roots), root)
            elif section.startswith("drop"):
                drop(score, bar, root, section == "drop2")
            elif section == "prowl":
                prowl(score, bar, local, root)
            else:
                chase(score, bar, root, section == "return")
            bar += 1
    assert bar == 100
    score.export(path, loudness=-18.5, room=.62)


if __name__ == "__main__":
    compose(Path(sys.argv[1]))
