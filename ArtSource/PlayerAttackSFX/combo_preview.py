"""Place the three exported swings on the earliest queued animation sequence."""
from pathlib import Path
import wave

import numpy as np


SOURCE = Path(__file__).resolve().parent
ASSETS = SOURCE.parents[1] / "NetmarbleNeo/Assets/Audio/PlayerCombat"
RATE = 48_000


def render():
    # ComboTransitionOpen markers set the start of stages 2 and 3. Within
    # each stage the attack peak follows the fastest sampled RightHandT motion.
    starts = (0.0, .8666667, .8666667 + 1.1666666)
    hand_peaks = (.614, .850, .716)
    placements = []
    window = round(.015 * RATE)
    for stage, (start, hand_peak) in enumerate(zip(starts, hand_peaks), 1):
        with wave.open(str(ASSETS / f"SwordSwing{stage}.wav"), "rb") as clip:
            assert (clip.getnchannels(), clip.getsampwidth(), clip.getframerate()) == (1, 2, RATE)
            samples = np.frombuffer(clip.readframes(clip.getnframes()), "<i2")
        energy = np.convolve(samples.astype(np.float64)**2, np.ones(window), mode="valid")
        peak = int(np.argmax(energy)) + window // 2
        offset = round((start + hand_peak) * RATE) - peak
        placements.append((offset, samples))

    length = max(at + len(samples) for at, samples in placements) + round(RATE * .10)
    timeline = np.zeros(length, dtype=np.int32)
    for at, samples in placements:
        timeline[at:at + len(samples)] += samples
    assert np.max(np.abs(timeline)) < 32767
    with wave.open(str(SOURCE / "SwordComboPreview.wav"), "wb") as output:
        output.setparams((1, 2, RATE, length, "NONE", "PCM"))
        output.writeframes(timeline.astype("<i2").tobytes())


if __name__ == "__main__":
    render()
