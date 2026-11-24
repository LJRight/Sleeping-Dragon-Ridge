"""Render the original score for 용흔의 능선. Run: python compose.py OUTPUT.wav

Requires NumPy and the installed GarageBand orchestral sampler files on macOS.
Only the arranged, mixed performance is exported. Source notes use scientific
pitch notation (C4 = middle C); durations and onsets are quarter-note beats.
"""
import argparse
from functools import lru_cache
from pathlib import Path
import re
import struct
import wave

import numpy as np


RATE = 44100
TEMPO = 96
BEAT = 60 / TEMPO
BARS = 48
FRAMES = round(RATE * BEAT * 4 * BARS)
SAMPLES = Path("/Library/Application Support/GarageBand/Instrument Library/Sampler/Sampler Files")
PITCH_CLASSES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def midi(name):
    letter, accidental, octave = re.fullmatch(r"([A-G])([b#]?)(\d)", name).groups()
    return 12 * (int(octave) + 1) + PITCH_CLASSES[letter] + {"": 0, "b": -1, "#": 1}[accidental]


def sample_key(path):
    # GarageBand's smpl header stores the recorded pitch and its fractional tuning.
    with path.open("rb") as stream:
        stream.seek(12)
        while header := stream.read(8):
            tag, size = struct.unpack("<4sI", header)
            if tag == b"smpl":
                fields = struct.unpack("<9I", stream.read(36))
                return fields[3] + fields[4] / 2**32
            stream.seek(size + size % 2, 1)
    raise ValueError(f"No sampler tuning in {path}")


@lru_cache(maxsize=None)
def sample_map(part):
    patterns = {
        "strings": ["String Ensemble/*[mn]*.wav"],
        "stringsF": ["String Ensemble/*f*.wav"],
        "horn": ["Horn Solo/*sus_mf/*.wav"],
        "horns": ["French Horns/*sus_mf/*.wav"],
        "trombones": ["Trombones/*sus_mf/*.wav"],
        "flute": ["Flute Solo/*sus_mf/*.wav"],
    }
    files = [p for pattern in patterns[part] for p in sorted(SAMPLES.glob(pattern))]
    if not files:
        raise FileNotFoundError(f"Missing {part} in {SAMPLES}")
    return [(sample_key(p), p) for p in files]


@lru_cache(maxsize=128)
def recording(path):
    with wave.open(str(path)) as wav:
        if wav.getsampwidth() != 2 or wav.getframerate() != RATE:
            raise ValueError(f"Unexpected sampler format in {path}")
        data = np.frombuffer(wav.readframes(wav.getnframes()), "<i2")
        data = data.reshape(-1, wav.getnchannels()).astype(np.float32) / 32768
    # Calibrate instruments from the sustained body, retaining natural bell/bow attacks.
    body = data[RATE // 3:RATE]
    gain = 0.155 / max(0.035, np.sqrt(np.mean(body * body)))
    return data * gain


# Bass followed by close inner string voicing. Inversion changes carry the
# bass line down or up by step while the high parts retain common tones.
CHORDS = {
    "Dm":  "D2 A3 D4 F4",    "Dm/C": "C2 A3 D4 F4",
    "Bb":  "Bb1 Bb3 D4 F4",  "F/A":  "A1 A3 C4 F4",
    "F":   "F2 A3 C4 F4",    "C/E":  "E2 G3 C4 E4",
    "C":   "C2 G3 C4 E4",    "Cs":   "C2 G3 C4 F4",
    "Gm":  "G2 G3 Bb3 D4",   "Dm/F": "F2 A3 D4 F4",
    "Emh": "E2 G3 Bb3 D4",   "A":    "A1 A3 C#4 E4",
    "As":  "A1 A3 D4 E4",    "Gm/Bb":"Bb1 G3 Bb3 D4",
}
PROGRESSION = (
    "Dm Dm/C Bb Cs "
    "Dm Bb F C Gm Dm/F Bb A "
    "Dm Bb C F Gm Bb Emh A "
    "F C/E Dm Bb Gm Bb Cs C "
    "Bb C Dm F/A Bb Gm C A "
    "Dm Bb F C Gm Dm/F Bb A "
    "Dm C/E Gm/Bb As"
).split()

# Each phrase is written out independently. A colon separates pitch and
# duration in beats. '-' creates space for a breath or instrumental answer.
THEME = [
    "D4:1.5 A4:1 F4:.5 E4:1", "F4:2 D4:1 -:.5 F4:.5",
    "A4:1.5 G4:.5 F4:1 C4:1", "E4:2.75 -:.75 D4:.5",
    "G4:1.5 D4:.75 F4:.75 G4:1", "A4:1.75 F4:.75 D4:1 -:.5",
    "F4:1.5 D4:.5 C4:1 Bb3:1", "A3:1 E4:1 C#4:1.5 -:.5",
]
ANSWER = [
    "D4:1.5 A4:.75 G4:.75 F4:1", "D4:1 F4:1 Bb4:1.5 A4:.5",
    "G4:2 E4:1 G4:1", "A4:2.75 -:.75 A3:.5",
    "Bb3:1 D4:1 G4:1.5 F4:.5", "F4:1.5 D4:.5 Bb3:1 F4:1",
    "G4:1.5 E4:.5 D4:1 Bb3:1", "C#4:2 A3:1 -:1",
]
HORIZON = [
    "C4:1 F4:1 A4:2", "G4:1.5 E4:.5 C4:1 E4:1",
    "F4:2.5 E4:.5 D4:1", "F4:1.5 Bb4:1.5 A4:1",
    "G4:2 D4:1 Bb3:1", "D4:1 F4:1 A4:1 Bb4:1",
    "G4:1.5 F4:.5 D4:1 G4:1", "E4:2.5 -:1 C4:.5",
]
ASCENT = [
    "D4:1 F4:1 Bb4:1.5 A4:.5", "G4:1.5 E4:.5 G4:1 C5:1",
    "A4:2 F4:1 D4:1", "C4:.5 F4:1 A4:.5 C5:1.5 -:.5",
    "Bb4:2 D5:1 C5:1", "Bb4:1.5 G4:.5 F4:1 D4:1",
    "E4:1 G4:1 C5:1.5 B4:.5", "A4:1.5 E4:.5 C#4:1 -:1",
]
RETURN = [
    "D4:1.5 A4:1 F4:.5 E4:1", "F4:2 D4:.75 F4:.75 A4:.5",
    "C5:1.5 A4:.5 G4:1 F4:1", "G4:2.5 E4:.5 -:1",
    "Bb4:1.5 G4:.5 D4:1 G4:1", "A4:1.5 F4:.5 D4:1 -:.5 F4:.5",
    "F4:1.5 D4:.5 C4:1 Bb3:1", "A3:1 E4:1 C#4:1.5 -:.5",
]
LEAD = (
    ["-:3 A3:1", "D4:2.25 -:1.75", "-:2 F4:1 D4:1", "C4:2 -:2"]
    + THEME + ANSWER + HORIZON + ASCENT + RETURN
    + ["D4:2 A3:1 -:1", "C4:1.5 G3:.5 -:2",
       "-:2 G3:1 Bb3:1", "A3:2 -:2"]
)


class Orchestra:
    def __init__(self):
        self.mix = np.zeros((FRAMES, 2), dtype=np.float32)
        self.random = np.random.default_rng(916117)

    def place(self, beat, sound, level, pan):
        at = round(beat * BEAT * RATE) % FRAMES
        balance = np.sqrt([1 - pan, 1 + pan]).astype(np.float32)
        sound = sound * (balance * level)
        end = min(len(sound), FRAMES - at)
        self.mix[at:at + end] += sound[:end]
        if end < len(sound):
            self.mix[:len(sound) - end] += sound[end:]

    def bowed(self, part, beat, note, beats, level, pan, *, short=False, swell=0.13):
        root, path = min(sample_map(part), key=lambda slot: abs(slot[0] - note))
        data = recording(path)
        # Small deterministic tuning differences bring out separately seated sections.
        ratio = 2 ** ((note - root + self.random.normal(0, 0.018)) / 12)
        held = beats * BEAT
        release = 0.075 if short else min(0.25, held * 0.23)
        t = np.arange(round((held + release) * RATE)) / RATE
        skip = self.random.uniform(0.065, 0.115) if short else 0
        pos = (t + skip) * RATE * ratio
        # Events are shorter than the source recordings; sustain loops are unnecessary.
        index = np.minimum(pos.astype(np.int32), len(data) - 2)
        frac = (pos - index).clip(0, 1).astype(np.float32)[:, None]
        samples = data[index] + frac * (data[index + 1] - data[index])
        attack = np.minimum(1, t / (0.017 if short else 0.027)) ** 1.3
        decay = np.clip((held + release - t) / release, 0, 1) ** 2
        shape = (1 - swell) + swell * np.sin(np.pi * np.minimum(t / held, 1))
        self.place(beat, samples * (attack * decay * shape).astype(np.float32)[:, None], level, pan)

    def harp(self, beat, note, level):
        # Three lightly inharmonic plucked strings with staggered decay rates.
        t = np.arange(round(RATE * 1.9)) / RATE
        hz = 440 * 2 ** ((note - 69) / 12)
        tone = np.zeros_like(t)
        for part, strength, decay in [(1, 1, 2.9), (2, .52, 4.6), (3, .21, 6.2), (5, .12, 9.4)]:
            tone += strength * np.sin(2 * np.pi * hz * (part + .00015 * part**3) * t) * np.exp(-t * decay)
        env = (1 - np.exp(-t * 130)) * np.minimum(1, (1.9 - t) / .09)
        self.place(beat, (tone * env * .21)[:, None], level, -.28)

    def timpani(self, beat, note, level):
        t = np.arange(round(RATE * 2.0)) / RATE
        hz = 440 * 2 ** ((note - 69) / 12)
        # Modal ratios of a tuned membrane, with a brief mallet-induced pitch drop.
        phase = 2 * np.pi * hz * (t + .0026 * (1 - np.exp(-30 * t)))
        tone = sum(g * np.sin(k * phase) * np.exp(-t * d)
                   for k, g, d in [(1, 1, 2.7), (1.5, .5, 3.8), (1.99, .45, 4.6),
                                    (2.47, .24, 6.7), (2.96, .29, 8.4), (3.86, .13, 11)])
        skin = self.random.normal(0, 1, len(t))
        skin = np.convolve(skin, np.ones(17) / 17, mode="same") * np.exp(-75 * t)
        env = (1 - np.exp(-140 * t)) * np.minimum(1, (2 - t) / .09)
        self.place(beat, ((tone + .9 * skin) * env * .31)[:, None], level, .16)


def arrange(orchestra):
    assert len(PROGRESSION) == len(LEAD) == BARS
    for bar, (symbol, phrase) in enumerate(zip(PROGRESSION, LEAD)):
        downbeat = bar * 4
        bass, *chord = [midi(p) for p in CHORDS[symbol].split()]
        strength = np.interp(bar, [0, 4, 12, 19, 20, 28, 33, 36, 40, 44, 47],
                             [.57, .72, .80, .86, .70, .92, 1.09, 1.03, .95, .65, .52])
        beat = downbeat
        part = "horns" if 28 <= bar < 44 else "horn"
        for token in phrase.split():
            name, count = token.split(":")
            duration = float(count)
            if name != "-":
                orchestra.bowed(part, beat + .015, midi(name), duration * .91,
                                strength * .97, .08, swell=.22)
            beat += duration
        assert beat == downbeat + 4

        # Cellos articulate the footsteps in pairs. Their underlying bass is bowed
        # separately so the pulse is clear even on a small, bass-light speaker.
        floor = bass + 12 if bass < 35 else bass
        fifth = floor + (6 if symbol == "Emh" else 7)
        orchestra.bowed("strings", downbeat, bass, 3.53, .47 * strength, .08)
        celloline = [(0, floor, .64, .66), (.83, fifth, .32, .39),
                     (1.52, floor + 12, .37, .48), (2, floor, .63, .63),
                     (2.83, fifth, .33, .42), (3.5, floor + 12, .34, .45)]
        if bar in (3, 11, 19, 27, 35, 43, 47):
            celloline = celloline[:4] + [(3.0, fifth, .7, .53)]
        for pos, key, length, gain in celloline:
            orchestra.bowed("strings", downbeat + pos + orchestra.random.uniform(0, .018),
                            key, length, gain * strength, -.22, short=True)

        for i, pitch in enumerate(chord):
            orchestra.bowed("strings", downbeat + .023 * i, pitch, 3.72 - .03 * i,
                            [.30, .29, .32][i] * strength, [-.31, .20, -.49][i], swell=.4)

        # Suspensions resolve late in the bar, most audibly in the final turnaround.
        if symbol in ("Cs", "As"):
            third = midi("E4" if symbol == "Cs" else "C#4")
            orchestra.bowed("flute", downbeat + 2.9, third + 12, .85, .29 * strength, -.12)

        # Interlocking arpeggios give the upper register movement between horn accents.
        harp_notes = [chord[0], chord[2], chord[1] + 12, chord[2] + 12]
        counts = [0.02, 1.51, 2.52, 3.52] if bar % 2 == 0 else [.5, 1.52, 2.51, 3.0]
        for i, (pos, pitch) in enumerate(zip(counts, harp_notes)):
            if bar < 4 and i == 3 or bar >= 44 and i in (1, 3):
                continue
            orchestra.harp(downbeat + pos, pitch, (.20 if i in (0, 2) else .15) * strength)

        # Rising counterline for strings, punctuated by short woodwind answers.
        if 12 <= bar < 44:
            upper = [chord[1]+12, chord[2]+12, chord[0]+12]
            if bar % 2:
                upper.reverse()
            for pos, length, key in zip([.04, 1.55, 2.54], [1.4, .83, 1.27], upper):
                orchestra.bowed("stringsF", downbeat + pos, key, length,
                                (.30 if bar >= 28 else .19) * strength, -.47, swell=.32)
        if 20 <= bar < 36 and bar % 2 == 1 or bar in (3, 7, 11, 39, 45):
            answers = [chord[1] + 12, chord[2] + 12, chord[0] + 12]
            for pos, length, key in zip([2.25, 2.95, 3.45], [.57, .37, .47], answers):
                orchestra.bowed("flute", downbeat + pos, key, length, .37 * strength, -.10)

        if 28 <= bar < 44:
            orchestra.bowed("trombones", downbeat + .04, chord[0] - 12, 1.68,
                            .49 * strength, .37, swell=.27)
            if bar % 2 == 0:
                orchestra.bowed("horns", downbeat + 2.03, chord[1], 1.54,
                                .35 * strength, .27)
        drum = bass if bass >= 38 else bass + 12
        if (12 <= bar < 44 and bar % 2 == 0) or bar in (0, 4, 8, 44):
            orchestra.timpani(downbeat + .014, drum, .31 * strength)
        if 28 <= bar < 40:
            orchestra.timpani(downbeat + 2.54, drum + 7, .18 * strength)
        if bar in (11, 19, 27, 35):
            for pos, level in [(3, .14), (3.25, .12), (3.5, .17), (3.75, .20)]:
                orchestra.timpani(downbeat + pos, drum, level * strength)


def concert_hall(dry):
    # Circular overlap-add includes the preceding loop's reflections at frame zero.
    rng = np.random.default_rng(533)
    n_ir = round(RATE * 1.65)
    block = 32768
    fft_size = 1 << (n_ir + block - 2).bit_length()
    mix = dry.copy()
    for channel in (0, 1):
        t = np.arange(n_ir) / RATE
        diffuse = rng.normal(size=n_ir)
        diffuse = np.convolve(diffuse, [0.12, 0.23, 0.30, 0.23, 0.12], mode="same")
        impulse = diffuse * np.exp(-4.45 * t) * (1 - np.exp(-17*t))
        impulse[:round(.031*RATE)] = 0
        impulse *= .16 / np.linalg.norm(impulse)
        for delay, gain in [(.023, .14), (.047, .10), (.079, .071), (.113, .04)]:
            impulse[round((delay + .003*channel) * RATE)] += gain
        room = np.fft.rfft(impulse, fft_size)
        send = .66 * dry[:,channel] + .34 * dry[:,1-channel]
        for start in range(0, FRAMES, block):
            part = send[start:start+block]
            wet = np.fft.irfft(np.fft.rfft(part, fft_size) * room, fft_size)[:len(part)+n_ir-1]
            limit = min(len(wet), FRAMES-start)
            mix[start:start+limit,channel] += wet[:limit]
            mix[:len(wet)-limit,channel] += wet[limit:]
    return mix


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    band = Orchestra()
    arrange(band)
    mix = concert_hall(band.mix)
    # Soft peak rounding and triangular dither at the 16-bit output stage.
    mix = np.tanh(mix * 1.08)
    mix *= .84 / np.max(np.abs(mix))
    dither = np.random.default_rng(2406)
    with wave.open(str(args.output), "wb") as out:
        out.setparams((2, 2, RATE, FRAMES, "NONE", "PCM"))
        for start in range(0, FRAMES, 65536):
            data = mix[start:start+65536] * 32767
            noise = dither.random(data.shape) - dither.random(data.shape)
            out.writeframesraw(np.rint(data + noise).astype("<i2").tobytes())


if __name__ == "__main__":
    main()
