"""Render the lightning spinning slash. Run: python spinning_slash.py OUTPUT.wav"""

from pathlib import Path
import sys

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt


RATE = 48000
DURATION = .90


def band(samples, low, high):
    sos = butter(3, (low, high), btype="bandpass", fs=RATE, output="sos")
    return sosfilt(sos, samples)


def color(rng, count, low, high):
    filtered = band(rng.standard_normal(count), low, high)
    return filtered / max(1e-9, np.sqrt(np.mean(filtered ** 2)))


def ease(time, start, end):
    position = np.clip((time - start) / (end - start), 0, 1)
    return position ** 2 * (3 - 2 * position)


def add_arc(mix, at, strength, brightness, rng):
    # A channel opens, forks and momentarily re-strikes along the same path.
    # The short pressure transient and longer ion hiss have different spectra.
    offsets = (0, rng.uniform(.0013, .0036), rng.uniform(.0052, .010),
               rng.uniform(.012, .021), rng.uniform(.024, .036))
    for i, (offset, factor) in enumerate(zip(offsets, (1, .67, .41, .24, .14))):
        length = .058
        t = np.arange(round(length * RATE)) / RATE
        tau = rng.uniform(.0022, .0049) * (1 if i == 0 else .78)
        hot = color(rng, len(t), 850, 2800 + 3800 * brightness)
        core = color(rng, len(t), 240, 1700)
        body = np.sin(2 * np.pi * rng.uniform(95, 145) * t) * np.exp(-t / .011)
        channel = (.80 * np.tanh(hot * 1.14) * np.exp(-t / tau)
                   + .58 * np.tanh(core * 1.33) * np.exp(-t / (tau * 2.6))
                   + .35 * body)
        channel *= (1 - np.exp(-t / .00022)) * np.minimum(1, (length - t) / .003)
        index = round((at + offset) * RATE)
        end = min(len(mix), index + len(t))
        mix[index:end] += strength * factor * channel[:end - index]


def render(path):
    rng = np.random.default_rng(912036)
    t = np.arange(round(RATE * DURATION)) / RATE

    # The blade's leading edge approaches, passes the listener, and recedes.
    # Its aspect changes the proportion of turbulent low, mid and bright air.
    azimuth = np.clip((t - .18) / .4, 0, 1) * 2 * np.pi
    proximity = (.5 - .5 * np.cos(azimuth)) ** 1.1
    blade = ease(t, 0, .21) * (1 - ease(t, .46, .71))
    fluctuation = color(rng, len(t), 6, 29)
    air = (.26 * color(rng, len(t), 95, 740)
           + (.13 + .19 * proximity) * color(rng, len(t), 690, 2100)
           + (.025 + .095 * proximity) * color(rng, len(t), 2400, 5900))
    mix = air * blade * (.21 + .79 * proximity) * (1 + .14 * np.tanh(fluctuation))

    # A ragged current follows the sword in one continuous orbit between arcs.
    drift = color(rng, len(t), 16, 78)
    frequency = 105 + 73 * proximity + 24 * np.sin(azimuth) + 18 * np.tanh(drift)
    phase = 2 * np.pi * np.cumsum(frequency) / RATE
    buzz = np.tanh(1.7 * np.sin(phase) + .72 * np.sin(phase * 3 + .4))
    current = band(buzz + .28 * np.sin(phase * 5.37 + 1.18 * buzz), 220, 3100)
    mix += .35 * current * blade * (.16 + .84 * proximity)

    # Two small leaders announce the luminous blade. The main branches span
    # the preview's 0.18–0.58 s trail window, followed by three smaller sparks.
    arcs = ((.119, .10), (.165, .21), (.195, .63), (.248, .49),
            (.288, .75), (.326, .59), (.363, 1.22), (.410, 1.01),
            (.452, .67), (.496, .83), (.546, .60), (.574, .46),
            (.631, .32), (.691, .16), (.761, .08))
    for at, level in arcs:
        add_arc(mix, at, level, .27 + .65 * proximity[round(at * RATE)], rng)

    # Short room reflections join the separate discharges without obscuring
    # the last small sparks. Mobile playback retains the midrange blade hiss.
    dry = mix.copy()
    for delay, gain in ((.014, .12), (.026, .073), (.048, .046), (.072, .026)):
        offset = round(delay * RATE)
        mix[offset:] += gain * dry[:-offset]
    mix = np.tanh(band(mix, 55, 7500) * 1.13) * ease(t, 0, .006) * (1 - ease(t, .84, .90))
    mix *= 10 ** (-4/20) / np.max(np.abs(mix))
    path.parent.mkdir(parents=True, exist_ok=True)
    wavfile.write(path, RATE, np.rint(mix * 32767).astype("<i2"))


if __name__ == "__main__":
    render(Path(sys.argv[1]))
