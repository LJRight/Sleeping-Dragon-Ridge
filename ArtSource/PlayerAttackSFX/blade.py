"""Render three dry sword passes. Usage: python blade.py OUTPUT_DIRECTORY."""

from argparse import ArgumentParser
from dataclasses import dataclass
from pathlib import Path
import wave

import numpy as np
from scipy.signal import butter, sosfilt


RATE = 48000


@dataclass(frozen=True)
class Stroke:
    seconds: float
    closest: float
    approach: float
    retreat: float
    edge_hz: float
    body_hz: float
    air_weight: float
    body_weight: float
    wake_weight: float
    peak_db: float
    seed: int


STROKES = (
    Stroke(.248, .107, .049, .041, 2150, 800, .69, .60, .22, -5.8, 52809),
    Stroke(.326, .158, .063, .056, 1580, 610, .56, .79, .31, -5.1, 26018),
    Stroke(.426, .197, .080, .078, 1350, 530, .60, .93, .43, -4.4, 92211),
)


def passing_noise(rng, rates, band):
    """Move a band of turbulent noise in pitch with the blade's changing speed."""
    read_position = np.cumsum(rates)
    lead = RATE // 20
    raw = rng.standard_normal(lead + int(np.ceil(read_position[-1])) + 2)
    filt = butter(3, band, fs=RATE, btype="bandpass", output="sos")
    recorded = sosfilt(filt, raw)[lead:]
    recorded /= np.sqrt(np.mean(recorded**2))
    return np.interp(read_position, np.arange(len(recorded)), recorded)


def air_pulse(time, center, attack, release):
    spread = np.where(time < center, attack, release)
    return np.exp(-((time - center) / spread)**2)


def synthesize(stroke):
    rng = np.random.default_rng(stroke.seed)
    t = np.arange(round(stroke.seconds * RATE)) / RATE
    relative = t - stroke.closest
    approach = np.exp(-((relative + .025) / stroke.approach)**2)
    receding = (1 + np.tanh(relative / .024)) * .5
    # The tip accelerates into the arc, then the close-pass shift lowers both
    # the whistling edge and the broad turbulence. No tonal blade ringing.
    speed = .82 + .51 * approach - .29 * receding
    edges = passing_noise(rng, speed, (.71*stroke.edge_hz, 1.52*stroke.edge_hz))
    air = passing_noise(rng, speed*.84 + .16, (1900, 6850))
    body = passing_noise(rng, .25 + .75*speed, (.40*stroke.body_hz, 1.64*stroke.body_hz))
    wake = passing_noise(rng, .52 + .48*speed, (125, 560))
    # Let the brighter leading edge arrive before the thicker displaced air.
    bow = air_pulse(t, stroke.closest-.013, stroke.approach*.84, stroke.retreat*.66)
    mass = air_pulse(t, stroke.closest+.007, stroke.approach, stroke.retreat)
    tail = air_pulse(t, stroke.closest+.032, stroke.approach*1.32, stroke.retreat*1.31)
    eddies = np.interp(t, np.linspace(0, t[-1], 22), rng.uniform(.84, 1.12, 22))
    sound = (bow * (edges + .38*air) * stroke.air_weight
             + mass * body * stroke.body_weight
             + tail * wake * stroke.wake_weight) * eddies
    sound = sosfilt(butter(2, 106, btype="highpass", fs=RATE, output="sos"), sound)
    # Gently tame stochastic outliers so the middle of the pass stays audible.
    sound = np.tanh(sound / 3.0)
    gate = np.minimum(t / .007, (stroke.seconds - 1/RATE - t) / .014)
    sound *= np.sin(np.pi/2 * np.clip(gate, 0, 1))**2
    sound *= 10**(stroke.peak_db/20) / np.max(np.abs(sound))
    return np.rint(32767 * sound).astype("<i2")


def main():
    parser = ArgumentParser(description=__doc__)
    parser.add_argument("output_directory", type=Path)
    directory = parser.parse_args().output_directory
    directory.mkdir(parents=True, exist_ok=True)
    for number, stroke in enumerate(STROKES, 1):
        audio = synthesize(stroke)
        with wave.open(str(directory / f"SwordSwing{number}.wav"), "wb") as wav:
            wav.setparams((1, 2, RATE, len(audio), "NONE", "PCM"))
            wav.writeframes(audio.tobytes())


if __name__ == "__main__":
    main()
