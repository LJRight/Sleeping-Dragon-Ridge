"""Lightning Uppercut. Render with python uppercut.py OUTPUT.wav."""
import sys
import wave
from pathlib import Path

import numpy as np
from scipy.signal import butter, sosfilt


RATE = 48_000
DISCHARGE = .286


def render(path):
    rng = np.random.default_rng(719042)
    t = np.arange(round(.81 * RATE)) / RATE

    def air(low, high):
        # Normalize each turbulence band before altering its position in the mix.
        shape = butter(2, (low, high), btype="bandpass", fs=RATE, output="sos")
        band = sosfilt(shape, rng.normal(size=t.size))
        return band / np.sqrt(np.mean(band**2))

    # A blade gathering charge: broad overlapping bands climb in frequency
    # with the stroke. The air thins just before the flash.
    lift = np.clip(t / (DISCHARGE - .018), 0, 1)
    ramp = np.sin(lift * np.pi / 2) ** 2
    crest = np.exp(-np.maximum(t - DISCHARGE + .038, 0) * 43)
    sweep = np.zeros_like(t)
    for position, (low, high, strength) in enumerate(
        [(95, 460, .15), (250, 820, .17), (470, 1490, .19),
         (820, 2640, .18), (1450, 4250, .14)]
    ):
        passing = np.exp(-.5 * ((4 * lift - position) / .77)**2)
        sweep += strength * passing * air(low, high)
    sweep *= ramp * crest * (1 + .12 * np.tanh(air(14, 27)))
    # Faint irregular ionization ticks stay inside the gathering air.
    corona = air(1800, 6400)
    for offset, strength in ((.148, .029), (.185, .041), (.208, .048),
                             (.235, .057), (.249, .072), (.266, .055)):
        dt = np.maximum(t - offset, 0)
        sweep += strength * corona * (1 - np.exp(-1900*dt)) * np.exp(-215*dt)

    # The return stroke is a single short pressure front. Its microreflections
    # sit within 16 ms, so the attack reads as one electric report.
    dt = np.maximum(t - DISCHARGE, 0)
    ion = air(630, 7800)
    mid = air(250, 2940)
    front = (1 - np.exp(-1950*dt)) * (
        .64 * ion * np.exp(-78*dt) + .67 * mid * np.exp(-36*dt))
    for delay, gain in ((.0042, -.31), (.0091, .23), (.0154, -.12)):
        shift = round(delay * RATE)
        front[shift:] += gain * front[:-shift].copy()

    # The compact thunder body first shoves air, then breaks into an uneven
    # rumble. A little energy above the bass survives small-speaker playback.
    shove = .57 * (1 - np.exp(-165*dt)) * np.exp(-21*dt)
    phase = 2 * np.pi * (77*dt + 5.8 * (1 - np.exp(-21*dt)))
    mass = shove * (np.sin(phase) + .30*np.sin(1.58*phase + .15))
    body = (1 - np.exp(-140*dt)) * (
        .35 * air(76, 400) * np.exp(-13*dt) +
        .16 * air(180, 1080) * np.exp(-12*dt))
    body *= .82 + .18*np.tanh(air(11, 26))
    for delay, level in ((.038, .08), (.082, .05)):
        pos = round(delay * RATE)
        body[pos:] += level * front[:-pos]

    mix = np.tanh((sweep + front + body + mass) * 1.12)
    mix = sosfilt(butter(2, 50, btype="highpass", fs=RATE, output="sos"), mix)
    mix = sosfilt(butter(3, 8550, fs=RATE, output="sos"), mix)
    mix *= np.minimum(1, t/.004) * np.minimum(1, (len(t)/RATE - t)/.035)**2
    mix *= 10 ** (-3.5/20) / np.max(np.abs(mix))
    pcm = np.rint(32767 * mix).astype("<i2")
    with wave.open(str(path), "wb") as out:
        out.setparams((1, 2, RATE, len(pcm), "NONE", "PCM"))
        out.writeframes(pcm.tobytes())


if __name__ == "__main__":
    render(Path(sys.argv[1]))
