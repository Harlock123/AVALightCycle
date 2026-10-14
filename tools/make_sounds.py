#!/usr/bin/env python3
"""Synthesises AVALightCycle's sound effects and tunes into ../src/AVALightCycle/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVALightCycle", "Sounds")
RNG = random.Random(1982)


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


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Discs and shells.
    write("Shot.wav", tone(0.12, 1800, 600, "square", release=0.8, gain=0.6), 0.25)
    write("EnemyShot.wav", tone(0.16, 700, 250, "saw", release=0.7), 0.3)
    write("Hit.wav", mix(tone(0.08, 1200, 400, "square", release=1.0), tone(0.06, 3000, 800, "noise", release=1.0, gain=0.4)), 0.35)
    write("Ricochet.wav", tone(0.18, 2600, 3600, "tri", release=0.7, vibrato=0.05, vib_rate=40), 0.2)
    write("Boom.wav", mix(tone(0.8, 400, 40, "noise", release=0.5), tone(0.7, 160, 30, "square", release=0.6, gain=0.6)), 0.55)
    write("Squash.wav", cat(tone(0.04, 900, 300, "square", release=0.6), tone(0.08, 300, 120, "noise", release=0.8)), 0.3)
    write("Chip.wav", mix(tone(0.07, 1500, 900, "tri", release=1.0), tone(0.05, 3000, 1200, "noise", release=1.0, gain=0.3)), 0.25)
    write("Spark.wav", cat(*[tone(0.03, 4000 - k * 300, 2000, "noise", release=0.5) for k in range(6)]), 0.25)
    # The grid itself.
    write("Derez.wav", cat(*[mix(tone(0.05, 1600 - k * 110, 200, "square", release=0.7, gain=0.6), tone(0.05, 2400, 600, "noise", release=0.8, gain=0.3)) for k in range(14)]), 0.45)
    write("Teleport.wav", cat(tone(0.25, 300, 2400, "sine", release=0.2), tone(0.2, 2400, 1200, "tri", release=0.6)), 0.35)
    write("Gate.wav", cat(*[tone(0.08, hz(n), kind="sine", release=0.4) for n in ["C5", "G5", "C6", "E6", "G6"]], tone(0.6, hz("C7"), kind="sine", release=0.7, vibrato=0.01)), 0.4)
    write("Move.wav", tone(0.05, 900, 1100, "pulse", release=0.6), 0.25)
    write("Choose.wav", cat(tone(0.08, hz("E5"), kind="pulse", release=0.4), tone(0.18, hz("B5"), kind="pulse", release=0.6)), 0.35)
    write("Cleared.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.3) for n in ["A4", "C#5", "E5", "A5", "C#6", "E6"]]), 0.4)
    write("LevelUp.wav", tune(150, [("A4", 0.5), ("E5", 0.5), ("A5", 0.5), ("C#6", 1), ("B5", 0.5), ("E6", 2)], [("A2", 2), ("E2", 1), ("A2", 2)], kind="pulse"), 0.45)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["E6", "B5", "E6", "B5", "E6", "G#6", "B6"]]), 0.4)

    # The grid's pulse: a driving synth arpeggio in A minor over a throbbing bass (new). Loops.
    arp = []
    for chord in [["A3", "C4", "E4", "A4"], ["F3", "A3", "C4", "F4"], ["C3", "E3", "G3", "C4"], ["G3", "B3", "D4", "G4"]] * 4:
        arp += [(n, 0.25) for n in chord + chord[::-1]]
    bassline = []
    for root in ["A1", "F1", "C2", "G1"] * 4:
        bassline += [(root, 0.25), ("-", 0.25)] * 4
    bass = seq(132, bassline, kind="saw", gain=0.4, release=0.3)
    top = seq(132, arp, kind="pulse", gain=0.18, release=0.5)
    beat = 60.0 / 132
    kicks = cat(*[mix(tone(0.12, 150, 45, "sine", release=0.8, gain=0.9), silence(beat)) for _ in range(int(len(bass) / RATE / beat))])
    write("Music.wav", mix(bass, top, kicks), 0.42)

    write("TitleTune.wav", tune(112,
                                [("A4", 1), ("C5", 0.5), ("E5", 1.5), ("D5", 0.5), ("C5", 0.5), ("B4", 1), ("G4", 1), ("A4", 2),
                                 ("E5", 1), ("G5", 0.5), ("A5", 1.5), ("G5", 0.5), ("E5", 0.5), ("D5", 1), ("B4", 1), ("A4", 2)],
                                [("A2", 2), ("F2", 2), ("C3", 2), ("G2", 2), ("A2", 2), ("F2", 2), ("G2", 2), ("A2", 2)], kind="saw"), 0.42)
    write("StartTune.wav", tune(150, [("E4", 0.5), ("A4", 0.5), ("C5", 0.5), ("E5", 1.5), ("D5", 0.5), ("E5", 2)], [("A2", 2), ("E2", 1), ("A2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("E5", 1), ("D5", 1), ("C5", 1), ("B4", 1), ("A4", 3)], [("A3", 2), ("F3", 2), ("E3", 1), ("A2", 3)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
