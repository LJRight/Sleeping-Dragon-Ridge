#!/usr/bin/env python3
"""Original procedural creature SFX. Offline; Python + NumPy, no sample assets.

Run with an existing NumPy-equipped Python. Outputs mono PCM16 WAV at 48 kHz.
"""
from pathlib import Path
import hashlib
import json
import math
import wave

import numpy as np

SR = 48000
ROOT = Path(__file__).resolve().parent
OUT = ROOT / "wav"


def curve(t, points):
    """Smooth, non-overshooting contours to avoid sharp parameter changes."""
    xp, yp = np.array(points).T
    i = np.clip(np.searchsorted(xp, t, side="right") - 1, 0, len(xp) - 2)
    u = np.clip((t - xp[i]) / (xp[i + 1] - xp[i]), 0, 1)
    u = u * u * (3 - 2 * u)
    return yp[i] + u * (yp[i + 1] - yp[i])


def noise(rng, n, low, high):
    # Zero-pad to keep FFT wrap-around away from the extracted sound.
    size = 1 << (2 * n - 1).bit_length()
    f = np.fft.rfftfreq(size, 1 / SR)
    shape = (1 - np.exp(-(f / low) ** 4)) * np.exp(-(f / high) ** 4)
    x = np.fft.irfft(np.fft.rfft(rng.normal(size=size)) * shape, n=size)[:n]
    return x / max(np.std(x), 1e-8)


def resonator(x, hz, bandwidth):
    # Causal vocal-tract resonance. Excitation is irregular, not a musical note.
    r = math.exp(-math.pi * bandwidth / SR)
    a = 2 * r * math.cos(2 * math.pi * hz / SR)
    b = r * r
    gain = 1 - r
    prev = prev2 = 0.0
    y = np.empty_like(x)
    for i, value in enumerate(x):
        sample = gain * value + a * prev - b * prev2
        y[i] = sample
        prev2, prev = prev, sample
    return y


def tract(x, formants):
    y = np.zeros_like(x)
    for hz, width, weight in formants:
        y += weight * resonator(x, hz, width)
    return y / max(np.std(y), 1e-8)


def vocal(rng, t, pitch_points, envelope_points, mouth_points, effort=1):
    n = len(t)
    pitch = curve(t, pitch_points)
    # Cycle jitter, irregular flutter, and independent secondary vocal fold.
    knots = np.linspace(0, t[-1], max(5, int(t[-1] * 90)))
    jitter = np.interp(t, knots, rng.normal(0, .07, len(knots)))
    drift = .04 * np.sin(2 * np.pi * (4.1 * t + .7 * t * t))
    phase = np.cumsum(pitch * (1 + jitter + drift)) / SR
    p = phase % 1
    # Abrupt glottal closure excites the throat; no clean sine-wave lead.
    pulse = np.where(p < .62, np.sin(np.pi * np.minimum(p / .62, 1)) ** 1.7, 0)
    source = np.diff(pulse, prepend=pulse[0])
    source /= max(np.std(source), 1e-8)
    flutter = .68 + .32 * np.sin(2 * np.pi * phase * .493 + 1.5 * jitter)
    secondary = np.sin(2 * np.pi * phase * 1.637 + 1.2 * np.sin(2 * np.pi * phase * .23))
    rough = noise(rng, n, 90, 4200)
    excitation = np.tanh(1.25 * (source * flutter + .28 * secondary + .38 * rough))
    closed = tract(excitation, [(230, 150, 1), (510, 210, .7), (1120, 330, .3)])
    opened = tract(excitation, [(390, 210, 1), (920, 320, .7), (1750, 480, .32)])
    mouth = curve(t, mouth_points)
    throat = closed * (1 - mouth) + opened * mouth
    rasp = tract(noise(rng, n, 220, 5400), [(550, 420, 1), (1500, 800, .55)])
    rasp *= .6 + .4 * np.sin(2 * np.pi * phase * .31) ** 2
    chest = np.sin(2 * np.pi * phase * .5 + .2 * rough)
    env = curve(t, envelope_points)
    result = (.55 * throat + .23 * rasp * effort + .17 * chest) * env
    return result


def burst(rng, t, start, duration, low, high, attack=.006):
    u = t - start
    env = np.where((u >= 0) & (u <= duration),
                   (1 - np.exp(-np.maximum(u, 0) / attack))
                   * np.exp(-5 * np.maximum(u, 0) / duration), 0)
    return noise(rng, len(t), low, high) * env


def finish(x, peak_db):
    # Short body reflections, deliberately dry enough for in-game spatial audio.
    dry = x.copy()
    for seconds, gain in [(.011, .10), (.023, .055), (.037, .025)]:
        shift = int(seconds * SR)
        x[shift:] += gain * dry[:-shift]
    x -= x.mean()
    # Remove inaudible DC/sub bass and aggressive ultrasonic content.
    size = 1 << (2 * len(x) - 1).bit_length()
    f = np.fft.rfftfreq(size, 1 / SR)
    filt = (1 - np.exp(-(f / 43) ** 4)) * np.exp(-(f / 7500) ** 6)
    x = np.fft.irfft(np.fft.rfft(x, n=size) * filt, n=size)[:len(x)]
    x = np.tanh(1.1 * x)
    for length, end in [(int(.005 * SR), False), (int(.025 * SR), True)]:
        ramp = np.sin(np.linspace(0, np.pi / 2, length)) ** 2
        if end:
            x[-length:] *= ramp[::-1]
        else:
            x[:length] *= ramp
    x *= 10 ** (peak_db / 20) / max(np.max(np.abs(x)), 1e-8)
    return x


def render(kind, duration, seed):
    rng = np.random.default_rng(seed)
    t = np.arange(round(duration * SR)) / SR
    if kind == "hurt":
        x = vocal(rng, t,
                  [(0, 100), (.07, 155), (.24, 114), (.50, 65), (.8, 52)],
                  [(0, 0), (.025, .9), (.09, 1), (.28, .64), (.55, .18), (.76, 0)],
                  [(0, .4), (.10, .9), (.35, .35), (.8, .05)], 1.2)
        x += .12 * burst(rng, t, .005, .10, 180, 2800)
        return finish(x, -4)
    if kind == "death":
        x = vocal(rng, t,
                  [(0, 120), (.12, 140), (.4, 93), (.8, 61), (1.2, 42), (1.48, 34)],
                  [(0, 0), (.025, .8), (.13, 1), (.4, .7), (.72, .4), (.95, .23), (1.31, 0)],
                  [(0, .6), (.15, .88), (.48, .35), (1.0, .04), (1.48, 0)], 1.05)
        # Last exhalation and a muted body settling sound, both inside despawn time.
        x += .24 * burst(rng, t, .80, .65, 130, 1900, .07)
        x += .10 * burst(rng, t, 1.02, .30, 45, 430, .008)
        return finish(x, -3.5)
    if kind == "bite":
        x = vocal(rng, t,
                  [(0, 62), (.24, 90), (.40, 132), (.53, 72), (.8, 50), (1, 48)],
                  [(0, 0), (.045, .17), (.22, .32), (.38, .6), (.48, .35), (.59, .09), (.82, 0)],
                  [(0, .04), (.25, .4), (.43, .8), (.52, .08), (1, 0)], .8)
        x += .17 * burst(rng, t, .32, .24, 400, 3500, .035)
        x += .65 * burst(rng, t, .495, .14, 220, 5300, .001)
        x += .32 * burst(rng, t, .500, .20, 70, 650, .003)
        x += .13 * burst(rng, t, .522, .10, 650, 3100, .002)
        return finish(x, -4)
    if kind == "alert_roar":
        x = vocal(rng, t,
                  [(0, 52), (.23, 67), (.40, 110), (.68, 136), (1.05, 119),
                   (1.45, 103), (1.85, 78), (2.32, 48)],
                  [(0, 0), (.15, .09), (.27, .17), (.39, .86), (.60, 1),
                   (1.05, .91), (1.47, .73), (1.85, .45), (2.1, .18), (2.29, 0)],
                  [(0, .02), (.25, .2), (.49, 1), (1.2, .85), (1.8, .4), (2.32, .03)], 1.2)
        x += .2 * burst(rng, t, .005, .34, 230, 2800, .07)
        x += .11 * burst(rng, t, 1.94, .35, 120, 2200, .06)
        return finish(x, -3)
    raise ValueError(kind)


def save_wav(path, x):
    pcm = np.rint(np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(pcm.tobytes())
    # Read back actual delivery bytes for validation, not the synthesis buffer.
    with wave.open(str(path), "rb") as f:
        y = np.frombuffer(f.readframes(f.getnframes()), dtype="<i2").astype(float) / 32768
        assert f.getnchannels() == 1 and f.getframerate() == SR
    assert np.all(np.isfinite(y)) and len(y) == len(x)
    assert max(abs(y[0]), abs(y[-1])) < 1 / 32768
    assert np.max(np.abs(y)) < .9 and np.sqrt(np.mean(y*y)) > .01
    return {
        "file": str(path.relative_to(ROOT)), "duration_seconds": len(y) / SR,
        "sample_rate": SR, "channels": 1, "bit_depth": 16,
        "peak_dbfs": round(float(20*np.log10(np.max(np.abs(y)))), 2),
        "rms_dbfs": round(float(20*np.log10(np.sqrt(np.mean(y*y)))), 2),
        "clipped_samples": int(np.count_nonzero(np.abs(y) >= 32767/32768)),
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    clips, report = [], []
    for kind, duration, seed in [("hurt", .80, 1711), ("death", 1.48, 1712),
                                  ("bite", 1.0, 1713), ("alert_roar", 2.32, 1714)]:
        x = render(kind, duration, seed)
        report.append(save_wav(OUT / f"dragon_{kind}.wav", x))
        clips.extend([x, np.zeros(int(.65 * SR))])
    save_wav(ROOT / "dragon_preview.wav", np.concatenate(clips[:-1]))
    (ROOT / "validation.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
