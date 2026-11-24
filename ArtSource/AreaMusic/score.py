"""Instruments, circular room and stereo PCM export for the three area scores.

Run each arrangement with Python, NumPy, SciPy, FFmpeg and the same local
GarageBand instruments used for LoginExpedition. Times are quarter-note beats.
"""
from functools import lru_cache
from pathlib import Path
import re
import struct
import subprocess
import sys
import tempfile
import wave

import numpy as np
from scipy.signal import lfilter

# Share the login score's sample tuning, gain calibration and pitch convention.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "LoginMusic"))
from compose import RATE, midi, recording, sample_map


@lru_cache(maxsize=None)
def sustain_loop(path):
    with path.open("rb") as stream:
        stream.seek(12)
        while header := stream.read(8):
            tag, size = struct.unpack("<4sI", header)
            if tag == b"smpl" and size >= 60:
                data = stream.read(60)
                if struct.unpack_from("<I", data, 28)[0]:
                    start, end = struct.unpack_from("<II", data, 44)
                    return start, end + 1
                break
            stream.seek(size + size % 2, 1)
    return None


class Score:
    def __init__(self, bpm, bars, beats_per_bar, seed):
        self.seconds_per_beat = 60 / bpm
        self.frames = round(bars * beats_per_bar * self.seconds_per_beat * RATE)
        self.mix = np.zeros((self.frames, 2), dtype=np.float32)
        self.rng = np.random.default_rng(seed)

    def note(self, part, beat, pitch, duration, level, pan=0, *, short=False, swell=.13):
        if duration <= 0 or level <= 0:
            return
        key = midi(pitch) if isinstance(pitch, str) else pitch
        hold = duration * self.seconds_per_beat
        if part in ("harp", "lute", "pizz"):
            sound = self.plucked(part, key, hold)
        elif part in ("drum", "timpani"):
            sound = self.struck(part, key, hold)
        else:
            sound = self.sampled(part, key, hold, short, swell)
        at = round(beat * self.seconds_per_beat * RATE) % self.frames
        balance = np.sqrt([1 - pan, 1 + pan]).astype(np.float32) * level
        sound = sound * balance
        take = min(len(sound), self.frames - at)
        self.mix[at:at + take] += sound[:take]
        if take < len(sound):
            self.mix[:len(sound) - take] += sound[take:]

    def sampled(self, part, key, hold, short, swell):
        root, path = min(sample_map(part), key=lambda item: abs(item[0] - key))
        data = recording(path)
        release = .063 if short else min(.24, max(.085, hold * .20))
        t = np.arange(round((hold + release) * RATE)) / RATE
        skip = self.rng.uniform(.047, .085) if short else 0
        ratio = 2 ** ((key - root + self.rng.normal(0, .014)) / 12)
        pos = (t + skip) * RATE * ratio
        if pos[-1] >= len(data) - 1:
            loop = sustain_loop(path)
            if loop is None:
                pos = np.minimum(pos, len(data) - 2)
            else:
                start, stop = loop
                after = pos >= stop
                pos[after] = start + (pos[after] - start) % (stop - start)
        # Fractional positions preserve the source recording's tuning.
        index = pos.astype(np.int32).clip(0, len(data) - 1)
        next_index = (index + 1).clip(max=len(data) - 1)
        frac = (pos - index).astype(np.float32)[:, None]
        tone = data[index] + frac * (data[next_index] - data[index])
        attack = np.minimum(1, t / (.014 if short else .023)) ** 1.3
        decay = np.clip((hold + release - t) / release, 0, 1) ** 2
        arc = (1 - swell) + swell * np.sin(np.pi * np.minimum(t / hold, 1))
        if short:
            arc *= .74 + .26 * np.exp(-5 * t / hold)
        return tone * (attack * decay * arc).astype(np.float32)[:, None]

    def plucked(self, part, key, hold):
        # Different picking positions and harmonic decay distinguish strings
        # played with the finger, a plectrum, and a short orchestral pizzicato.
        position, damp, brightness, release = {
            "harp": (.145, 1.55, .68, .62),
            "lute": (.215, 2.75, .76, .29),
            "pizz": (.33, 4.20, .58, .15),
        }[part]
        t = np.arange(round((min(hold, 3) + release) * RATE)) / RATE
        hz = 440 * 2 ** ((key - 69) / 12)
        position += self.rng.uniform(-.016, .016)
        tone = np.zeros(len(t))
        for n in range(1, min(11, int(15000 / hz)) + 1):
            weight = np.sin(np.pi * n * position) / (n ** (.9 + .25 * (1 - brightness)))
            phase = 2 * np.pi * hz * n * (1 + .000038 * n * n) * t
            tone += weight * np.sin(phase) * np.exp(-(damp + n ** 1.35 * .47) * t)
            if part == "lute":
                # Paired strings on a lute course sound a few cents apart.
                tone += .33 * weight * np.sin(phase * 1.0016) * np.exp(-(damp + n) * t)
        attack = 1 - np.exp(-t * (230 if part == "pizz" else 500))
        tail = np.clip((len(t) / RATE - t) / release, 0, 1) ** 2
        return (tone * attack * tail * .40).astype(np.float32)[:, None]

    def struck(self, part, key, hold):
        big = part == "timpani"
        decay = 2.55 if big else 6.1
        length = min(hold + (.95 if big else .38), 2.2 if big else 1)
        t = np.arange(round(length * RATE)) / RATE
        hz = 440 * 2 ** ((key - 69) / 12)
        phase = 2 * np.pi * hz * (t + (.003 if big else .007) * (1 - np.exp(-42*t)))
        modes = ((1, 1), (1.5, .53), (2, .32), (2.47, .22), (2.96, .19)) if big else (
            (1, 1), (1.59, .63), (2.14, .31), (2.66, .21), (3.55, .13))
        tone = sum(gain * np.sin(ratio * phase) * np.exp(-decay * ratio * t)
                   for ratio, gain in modes)
        noise = lfilter([.20], [1, -.80], self.rng.normal(size=len(t)))
        tone += (1.15 if big else 1.55) * noise * np.exp(-t * (64 if big else 44))
        env = (1 - np.exp(-260*t)) * np.minimum(1, (length-t) / .06) ** 2
        return (.32 * env * tone).astype(np.float32)[:, None]

    def add_room(self, amount):
        # Circular overlap-add returns reflections from the final notes to bar one.
        dry = self.mix.copy()
        rng = np.random.default_rng(4882)
        t = np.arange(round(RATE * 1.48)) / RATE
        block = 32768
        fft_size = 1 << (len(t) + block - 2).bit_length()
        for ch in (0, 1):
            impulse = lfilter([.25], [1, -.75], rng.normal(size=len(t)))
            impulse *= np.exp(-5.15 * t) * (1 - np.exp(-22*t))
            impulse[:round(.025*RATE)] = 0
            impulse *= .21 / np.linalg.norm(impulse)
            for delay, gain in ((.018, .16), (.039, .12), (.065, .08), (.091, .06)):
                impulse[round((delay + ch * .0031) * RATE)] += gain
            room_fft = np.fft.rfft(impulse * amount, fft_size)
            for start in range(0, self.frames, block):
                x = dry[start:start+block]
                send = .65 * x[:,ch] + .35 * x[:,1-ch]
                wet = np.fft.irfft(np.fft.rfft(send, fft_size) * room_fft, fft_size)
                wet = wet[:len(send) + len(t) - 1]
                take = min(len(wet), self.frames - start)
                self.mix[start:start+take, ch] += wet[:take]
                if take < len(wet):
                    self.mix[:len(wet)-take, ch] += wet[take:]

    def export(self, path, loudness, room):
        self.add_room(room)
        # Measure the actual performance and apply a single gain. A quiet
        # passage keeps its relation to the rest of the score on every loop.
        cmd = ["ffmpeg", "-hide_banner", "-nostats", "-f", "f32le", "-ar", str(RATE),
               "-ac", "2", "-i", "pipe:0", "-af", "ebur128=peak=true:framelog=verbose",
               "-f", "null", "-"]
        measured = subprocess.run(cmd, input=memoryview(self.mix).cast("B"),
                                  stderr=subprocess.PIPE, check=True)
        summary = measured.stderr.decode().rsplit("Summary:", 1)[-1]
        current = float(re.search(r"I:\s*([-\d.]+) LUFS", summary)[1])
        peak = float(re.search(r"Peak:\s*([-\d.]+) dBFS", summary)[1])
        gain = 10 ** (min(loudness - current, -1.6 - peak) / 20)
        path = Path(path)
        with tempfile.NamedTemporaryFile(dir=path.parent, prefix="."+path.stem,
                                         suffix=".tmp", delete=False) as temporary:
            tmp = Path(temporary.name)
        try:
            with wave.open(str(tmp), "wb") as wav:
                wav.setparams((2, 2, RATE, self.frames, "NONE", "PCM"))
                for start in range(0, self.frames, 65536):
                    x = self.mix[start:start+65536] * (gain * 32767)
                    dither = self.rng.random(x.shape) - self.rng.random(x.shape)
                    wav.writeframesraw(np.rint(x + dither).clip(-32768,32767).astype("<i2").tobytes())
            tmp.chmod(0o644)
            tmp.replace(path)
        finally:
            tmp.unlink(missing_ok=True)
