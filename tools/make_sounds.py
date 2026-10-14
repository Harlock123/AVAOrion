#!/usr/bin/env python3
"""Synthesises AVAOrion's sound effects and tunes into ../src/AVAOrion/Sounds as 16-bit mono WAV.

Nothing is sampled from any other game: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAOrion", "Sounds")
RNG = random.Random(2300)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))



def chord(seconds, notes, kind="tri", gain=0.4, release=0.8):
    return mix(*[tone(seconds, hz(n), kind=kind, gain=gain, release=release) for n in notes])


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def bars(pattern, times):
    return pattern * times


def main():
    os.makedirs(OUT, exist_ok=True)
    # Battle: beams, missiles, explosions, shields taking a hit.
    write("Laser.wav", cat(*[tone(0.12, 1800, 600, "square", release=0.6, gain=0.6) for _ in range(3)]), 0.4)
    write("Missile.wav", cat(tone(0.5, 300, 900, "noise", attack=0.1, release=0.3), tone(0.25, 900, 200, "noise", release=0.8)), 0.45)
    write("Explosion.wav", mix(tone(1.1, 160, 25, "noise", release=0.4), tone(0.8, 90, 30, "square", release=0.7, gain=0.6), tone(0.1, 2200, 600, "noise", release=1.0, gain=0.5)), 0.7)
    write("Shield.wav", mix(tone(0.35, 600, 900, "sine", release=0.8, vibrato=0.08, vib_rate=30), tone(0.3, 1200, 1500, "tri", release=1.0, gain=0.3)), 0.35)
    # Ships and colonies.
    write("Launch.wav", cat(tone(0.15, 200, 260, "saw", release=0.2, gain=0.5), tone(0.8, 260, 1400, "noise", attack=0.05, release=0.7)), 0.4)
    write("Warp.wav", mix(tone(0.6, 180, 1600, "sine", release=0.5), tone(0.6, 360, 3200, "tri", release=0.7, gain=0.3)), 0.35)
    write("Colonise.wav", cat(*[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["C5", "G5", "E5"]], chord(0.8, ["C5", "E5", "G5", "C6"], kind="tri", gain=0.4, release=1.2)), 0.45)
    write("Built.wav", cat(*[mix(tone(0.05, 1200, 600, "noise", release=0.9), tone(0.08, 440, 420, "square", release=1.0, gain=0.4)) + silence(0.07) for _ in range(3)]), 0.4)
    write("Landing.wav", cat(tone(0.6, 1200, 150, "noise", release=0.4), tone(0.3, 120, 60, "square", release=0.6, gain=0.5)), 0.45)
    write("Alarm.wav", cat(*[cat(tone(0.18, 880, 660, "square", release=0.2, gain=0.5), tone(0.18, 660, 880, "square", release=0.2, gain=0.5)) for _ in range(2)]), 0.35)
    write("Horn.wav", cat(tone(0.4, hz("E3"), kind="saw", attack=0.05, release=0.3, vibrato=0.01, vib_rate=5), tone(0.8, hz("B3"), kind="saw", attack=0.03, release=0.6, vibrato=0.01, vib_rate=5)), 0.4)
    write("Coin.wav", cat(tone(0.06, hz("B5"), kind="square", release=0.4), tone(0.35, hz("E6"), kind="square", release=0.8)), 0.35)
    write("Turn.wav", mix(tone(1.0, hz("E5"), kind="sine", release=1.5), tone(1.0, hz("B5"), kind="sine", release=2.5, gain=0.4), tone(1.0, hz("E6") * 1.003, kind="sine", release=2.0, gain=0.25)), 0.3)
    write("Menu.wav", tone(0.03, 1300, kind="square", release=1.0), 0.22)
    write("Pick.wav", cat(tone(0.04, 990, kind="square", release=0.6), tone(0.06, 1480, kind="square", release=0.8)), 0.3)
    write("Bump.wav", tone(0.14, 150, 90, "square", release=0.6), 0.35)
    write("Discovery.wav", cat(*[tone(0.08, hz(n), kind="tri", release=0.3) for n in ["E5", "G#5", "B5", "E6", "G#6"]], chord(0.8, ["E6", "B5", "G#5"], kind="tri", gain=0.4, release=1.2)), 0.45)
    write("Council.wav", cat(seq(108, [("C4", 1), ("G4", 1), ("C5", 1.5), ("D5", 0.5), ("E5", 2), ("D5", 1), ("C5", 1), ("G4", 2)], kind="saw", gain=0.45, release=0.4),
                             chord(1.6, ["C4", "G4", "C5", "E5"], kind="saw", gain=0.3, release=1.0)), 0.45)

    # The title: a broad, rising theme in E minor (new). Loops.
    lead = [("E4", 2), ("B4", 2), ("A4", 1), ("G4", 1), ("A4", 2), ("B4", 4), ("-", 2),
            ("E5", 2), ("D5", 1), ("B4", 1), ("C5", 2), ("A4", 2), ("B4", 6), ("-", 2),
            ("G4", 2), ("A4", 1), ("B4", 1), ("D5", 2), ("E5", 2), ("F#5", 2), ("G5", 4), ("-", 2),
            ("F#5", 2), ("E5", 2), ("D5", 2), ("B4", 2), ("E5", 8)]
    bass = [("E2", 8), ("C2", 8), ("A1", 8), ("B1", 8), ("G1", 8), ("D2", 8), ("C2", 8), ("B1", 4), ("E2", 4)]
    write("TitleTune.wav", tune(92, lead, bass, kind="tri"), 0.5)

    # The quiet between the stars: slow pads and a far-off melody (new). Loops.
    pads = []
    for root in [("E3", "B3", "G4"), ("C3", "G3", "E4"), ("A2", "E3", "C4"), ("B2", "F#3", "D#4")]:
        pads.append(chord(4.0, list(root), kind="sine", gain=0.35, release=0.2))
    melody = seq(60, [("-", 2), ("B4", 2), ("G4", 1), ("A4", 1), ("B4", 4), ("-", 2), ("E5", 2), ("D5", 2), ("B4", 2)], kind="tri", gain=0.3, release=0.5)
    write("SpaceMusic.wav", mix(cat(*pads), melody), 0.35)

    # War: a driving pulse in D minor (new). Loops.
    arp = []
    for root in [("D3", "F3", "A3"), ("A#2", "D3", "F3"), ("C3", "E3", "G3"), ("A2", "C#3", "E3")]:
        arp += [(n, 0.5) for n in root + root[::-1][1:] + (root[1],)] * 2
    write("WarMusic.wav", mix(seq(126, arp, kind="pulse", gain=0.3, release=0.8), seq(126, [("D2", 8), ("A#1", 8), ("C2", 8), ("A1", 8)], kind="tri", gain=0.5, release=0.2),
                              seq(126, [("-", 8), ("A4", 2), ("G4", 1), ("F4", 1), ("E4", 4), ("-", 4), ("D5", 2), ("C#5", 2), ("A4", 4)], kind="saw", gain=0.3, release=0.5)), 0.4)

    # Battle: fast and urgent (new). Loops.
    hits = []
    for root in ["E3", "E3", "G3", "A3", "E3", "E3", "D3", "B2"]:
        hits += [(root, 0.25), ("-", 0.25), (root, 0.25), (root, 0.25)]
    write("BattleMusic.wav", mix(seq(150, hits, kind="square", gain=0.35, release=0.9), seq(150, [("E5", 1), ("G5", 1), ("A5", 2), ("B5", 1), ("A5", 1), ("G5", 2)] * 2, kind="saw", gain=0.25, release=0.5)), 0.4)

    write("Victory.wav", tune(104, [("E4", 1), ("B4", 1), ("E5", 2), ("D#5", 1), ("E5", 1), ("F#5", 2), ("G#5", 4), ("B5", 2), ("E6", 6)],
                          [("E2", 4), ("B1", 4), ("E2", 4), ("A1", 4), ("E2", 6)], kind="pulse"), 0.5)
    write("Defeat.wav", tune(66, [("B4", 2), ("A4", 1), ("G4", 1), ("F#4", 2), ("E4", 2), ("D#4", 2), ("E4", 4)],
                         [("E3", 4), ("B2", 4), ("E2", 6)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
