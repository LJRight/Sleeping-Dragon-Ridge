"""War drums, bowed-metal accents and snare orchestration for the boss cue."""

from functools import lru_cache

import numpy as np
from scipy.signal import lfilter

from score import RATE


# Sixteenth-note grids. The uppercase strokes carry the weight of the pattern,
# while the ghost strokes suggest the drummer's other hand.
VELOCITY = {"O": 1.0, "o": .71, "+": .45, "'": .24}
CHASE = {
    "war": ("O... ..o. .... o...", "O... .... ..o. ...+", "O... ..o. .... o..+", "O... .... o... ...."),
    "tom": ("...+ .... o..+ ....", ".... .+o. o... ..+.", ".... ...+ +..o ....", "..+. o... ..+o o..+"),
    "rim": (".... o... ...+ o...", ".... o... .... o..+", ".... o... .... o...", ".... o... .+.. o..."),
}
FIRST_DROP = {
    "war": ("O... ..o. .... .o..", "O... .... ...o ..o.", "O..+ ..o. .... o...", "O... ...o .... ..o."),
    "snare": (".... .... O... ...'", ".... .+.. O... ..+.", ".... .... O... ...'", "...+ .... O... +..+"),
    "tom": (".... .+.. .... ..+.", "..o. .... .... o...", ".... .+.. .o.. ....", ".... ..+. ..o. .o.+"),
}
SECOND_DROP = {
    "war": ("O..o .... O... ..o.", "O... ..o. O..+ ....", "O..o .... ..O. o...", "O... ...o O... ..o+"),
    "snare": (".... O... ...' O...", ".... O..+ .... O..'", ".... O... ..+. O...", ".... O... .+.. O..."),
    "tom": ("..+. ..o. ..+. .o..", "..o. ...+ ..o. ..+.", "..+. ..o. '+.. ..o.", "..+. .o.. .... .++o"),
}
RETURN = {
    "war": ("O... .... ..o. ....", "O... ..+. .... o...", "O... .... ..o. ....", "O... ...+ o... ...."),
    "tom": ("...+ ..o. .... ..+.", ".... ...+ o... ...+", "...+ .... .+.. o...", ".... o..+ .+.. ...o"),
    "rim": (".... o... .... +...", ".... o... .'.. o...", ".... o... .... +..'", ".... o... .... o..."),
}


@lru_cache(maxsize=200)
def instrument(voice, note, hand):
    """Render three stick positions for each head, plus untuned iron and shell."""
    rng = np.random.default_rng(5829 + note * 347 + hand * 733)
    duration = {"war": .93, "tom": .43, "snare": .32, "rim": .11, "iron": .58}[voice]
    t = np.arange(round(RATE * duration)) / RATE
    angle = 2 * np.pi
    noise = rng.normal(size=len(t))
    # The beater noise is short and mid-rangy; the heads have enough overtones
    # to retain their pitch and impact through a small phone speaker.
    felt = lfilter([.19], [1, -.81], noise)
    if voice in ("war", "tom"):
        large = voice == "war"
        fundamental = 440 * 2 ** ((note - 69) / 12) * (1 + .005 * (hand - 1))
        bend = .010 if large else .014
        phase = angle * fundamental * (t + bend * (1 - np.exp(-32*t)))
        modes = ((1, .94), (1.48, .41), (2.03, .37), (2.63, .18), (3.74, .12))
        rate = 4.8 if large else 8
        head = sum(weight * np.sin(mode * phase) * np.exp(-rate * (mode**.82) * t)
                   for mode, weight in modes)
        stick = felt * np.exp(-48*t) + .27 * np.sin(phase * 6.8) * np.exp(-65*t)
        tone = (.34 if large else .29) * (head + stick)
    elif voice == "snare":
        # A broad noisy rattle decays independently from the two membrane modes.
        wires = lfilter([.68, -.68], [1, -.36], noise)
        wires = lfilter([.45], [1, -.55], wires)
        shell = .60*np.sin(angle*(177 + 3*hand)*t)
        shell += .29*np.sin(angle*(291 - 2*hand)*t)
        tone = .30 * shell * np.exp(-21*t) + .50 * wires * np.exp(-18*t)
    elif voice == "rim":
        wood = np.sin(angle*(893 + 15*hand)*t) + .53*np.sin(angle*1511*t)
        tone = .18 * wood * np.exp(-72*t) + .09*felt*np.exp(-91*t)
    else:
        # Several uncoupled modes give the iron its irregular ring; the low
        # strike is present even when the highest partials are masked by SFX.
        modes = ((407, .70, 9), (965, .62, 10), (1422, .35, 14),
                 (1966, .26, 17), (2813, .14, 23), (4211, .08, 29))
        tone = sum(weight*np.cos(angle*(freq + hand*1.7)*t) * np.exp(-decay*t)
                   for freq, weight, decay in modes)
        tone = .17 * tone + .07*felt*np.exp(-74*t)
    attack = 210 if voice == "war" else 720
    window = (1 - np.exp(-attack*t)) * np.minimum(1, (duration-t)/.024)**2
    return (tone * window).astype(np.float32)


def percussion(score, bar, section, root):
    pulse = bar * 4
    hand_rng = np.random.default_rng(731 + bar)

    def strike(voice, offset, level, degree=0):
        if voice == "war":
            note = root + 12 if root < 31 else root
        elif voice == "tom":
            note = root + 15 + degree
        else:
            note = 0
        hand = int(hand_rng.integers(3))
        sound = instrument(voice, note, hand)
        jitter = hand_rng.uniform(-.010, .014) if offset else 0
        at = round((pulse + offset + jitter) * score.seconds_per_beat * RATE) % score.frames
        pan = {"war": 0, "tom": -.12, "snare": .16, "rim": -.28, "iron": .32}[voice]
        if voice == "tom":
            pan += .30 if degree == 5 else -.10
        pan += hand_rng.uniform(-.045, .045)
        level *= hand_rng.uniform(.965, 1.025)
        balance = level * np.sqrt(np.array([1 - pan, 1 + pan], dtype=np.float32))
        stereo = sound[:, None] * balance
        take = min(len(sound), score.frames - at)
        score.mix[at:at + take] += stereo[:take]
        if take < len(sound):
            score.mix[:len(sound) - take] += stereo[take:]

    if section in ("build1", "build2"):
        first_bar = 16 if section == "build1" else 56
        until_drop = (24 if section == "build1" else 72) - bar
        if until_drop == 1:
            # A clipped final roll leaves the approaching downbeat exposed.
            end = 2 if section == "build1" else 1.5
            for i, offset in enumerate(np.arange(0, end, .25)):
                strike("snare", offset, .38 + .055*i)
            strike("rim", end, .63)
            return
        for offset in ((0, 2.5) if until_drop > 3 else (0,)):
            strike("war", offset, .57 if offset == 0 else .36)
        step = 1 if until_drop > 6 else (.5 if until_drop > 3 else .25)
        intensity = .32 + .37 * (bar - first_bar) / (bar - first_bar + until_drop)
        for i, offset in enumerate(np.arange(0, 4, step)):
            accent = 1 if offset % 1 == 0 else (.66 if i % 2 == 0 else .48)
            strike("snare", offset, intensity * accent)
        for offset in ((1.5, 3.25) if bar % 2 else (.75, 2.75)):
            strike("tom", offset, .48, degree=5 if offset > 2 else 0)
        if bar % 4 == 2:
            strike("iron", 3.5, .45)
        return

    if section == "prowl":
        if bar % 2 == 0:
            strike("war", 0, .50)
            strike("tom", 2.75, .34, degree=5)
        for offset in ((1.5, 3.75) if bar % 4 == 1 else (2.25,)):
            strike("rim", offset, .38)
        if bar % 4 == 3:
            strike("tom", 3.25, .41)
        return

    grid = {"chase": CHASE, "drop1": FIRST_DROP, "drop2": SECOND_DROP, "return": RETURN}[section]
    strength = {"chase": .77, "drop1": 1.07, "drop2": 1.07, "return": .73}[section]
    for voice, phrases in grid.items():
        pattern = phrases[bar % 4].replace(" ", "")
        for i, mark in enumerate(pattern):
            if mark != ".":
                strike(voice, i/4, VELOCITY[mark] * strength, degree=5 if i % 4 == 2 else 0)

    if section.startswith("drop"):
        if bar % 4 == 0:
            strike("iron", 0, .95 if bar in (24, 72) else .58)
        if bar % 4 == 3:
            strike("iron", 3.5, .36)
        if bar in (24, 72):
            strike("tom", .035, .86, degree=5)
    if bar in (7, 15, 39, 79, 87, 99):
        for i, pos in enumerate((3.25, 3.5, 3.75)):
            strike("tom", pos, .37 + i*.105, degree=(5, 0, -4)[i])
