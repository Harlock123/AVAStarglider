#!/usr/bin/env python3
"""Synthesises AVAStarglider's sound effects and tunes into ../src/AVAStarglider/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the tunes
are new compositions: a soaring theme for the title and a driving pulse for the waves. The AGAV's computer speaks
through the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAStarglider", "Sounds")
RNG = random.Random(1986)


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


def computer(words):
    voice = say(words, rate=RATE, pitch=104, speed=0.95, size=1.05)
    hum = say(words, rate=RATE, pitch=156, speed=0.95, size=1.05, seed=11)
    return mix(voice, [s * 0.3 for s in hum])


def computer(words):
    voice = say(words, rate=RATE, pitch=104, speed=0.95, size=1.05)
    hum = say(words, rate=RATE, pitch=156, speed=0.95, size=1.05, seed=11)
    return mix(voice, [s * 0.3 for s in hum])


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Laser.wav", tone(0.12, 2600, 700, "square", attack=0.001, release=0.9, gain=0.5), 0.13)
    write("Hit.wav", mix(tone(0.08, 2600, 1200, "square", attack=0.001, release=1.4, gain=0.4), tone(0.07, 3000, 800, "noise", release=1.4, gain=0.4)), 0.18)
    write("Boom.wav", mix(tone(0.6, 1800, 100, "noise", attack=0.001, release=0.9), tone(0.5, 140, 40, "sine", release=0.8, gain=0.8)), 0.38)
    write("Deflect.wav", tone(0.14, 3200, 1600, "tri", attack=0.001, release=0.8, gain=0.5), 0.16)
    write("Launch.wav", mix(tone(0.6, 300, 1200, "saw", attack=0.02, release=0.5, gain=0.4), tone(0.6, 3000, 1500, "noise", release=0.6, gain=0.3)), 0.28)
    write("StargliderHit.wav", cat(mix(tone(0.3, 1500, 200, "noise", attack=0.001, release=0.8), tone(0.3, 400, 120, "saw", release=0.8, gain=0.5)), tone(0.6, 900, 300, "sine", vibrato=0.08, vib_rate=9, release=0.6, gain=0.5)), 0.36)
    write("StargliderDown.wav", cat(mix(tone(1.6, 2200, 40, "noise", attack=0.001, release=0.5), tone(1.4, 160, 25, "sine", release=0.6)), tone(1.0, 1200, 90, "saw", vibrato=0.15, vib_rate=6, release=0.6, gain=0.4)), 0.55)
    write("EgronFire.wav", tone(0.14, 700, 250, "square", attack=0.001, release=1.0, gain=0.5), 0.15)
    write("Hurt.wav", mix(tone(0.15, 600, 180, "square", attack=0.001, release=1.0, gain=0.6), tone(0.12, 2500, 500, "noise", release=1.2, gain=0.6)), 0.3)
    write("Explode.wav", cat(mix(tone(1.3, 2200, 50, "noise", attack=0.001, release=0.5), tone(1.1, 140, 25, "sine", release=0.6, gain=0.9)), tone(0.6, 500, 70, "saw", vibrato=0.2, vib_rate=10, release=0.6, gain=0.3)), 0.55)
    write("Bump.wav", mix(tone(0.2, 160, 60, "square", attack=0.001, release=1.0, gain=0.7), tone(0.12, 1200, 300, "noise", release=1.2, gain=0.5)), 0.32)
    write("Dock.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "G5", "C6"]]), 0.3)
    write("Charge.wav", tone(0.6, 200, 600, "saw", attack=0.05, vibrato=0.08, vib_rate=40, release=0.5, gain=0.4), 0.18)
    write("LowEnergy.wav", cat(*[tone(0.12, 1000, kind="square", release=0.2, gain=0.5) for _ in range(3)]), 0.22)
    # "It's coming!" - the Starglider on its way.
    write("SayStarglider.wav", computer("IH1 T S , K AH1 M IH NG !"), 0.5)

    # The waves (an original composition): a driving pulse.
    e = 0.5
    lead = [("E4", e), ("E4", e), ("G4", e), ("A4", e), ("B4", 1), ("A4", e), ("G4", e), ("E4", e), ("D4", e), ("E4", 2),
            ("E4", e), ("G4", e), ("A4", e), ("C5", e), ("B4", 1), ("A4", e), ("B4", e), ("G4", e), ("F#4", e), ("E4", 2)]
    bass = [("E2", e), ("E3", e)] * 8 + [("C2", e), ("C3", e)] * 4 + [("D2", e), ("D3", e)] * 3 + [("E2", e), ("E3", e)]
    write("Music.wav", tune(140, lead * 2, bass * 2, kind="pulse"), 0.24)
    # The title (an original composition): soaring.
    title = [("E5", 1), ("B4", e), ("E5", e), ("G5", 1.5), ("F#5", e), ("E5", 1), ("D5", 1), ("B4", 2),
             ("C5", 1), ("G5", e), ("C6", e), ("B5", 1.5), ("A5", e), ("G5", 1), ("F#5", 1), ("E5", 3)]
    tbass = [("E3", 2), ("G3", 2), ("D3", 2), ("B2", 2), ("C3", 2), ("A2", 2), ("B2", 2), ("E3", 3)]
    write("TitleTune.wav", cat(tune(116, title * 2, tbass * 2, kind="square"), silence(0.8)), 0.34)
    write("Victory.wav", tune(150, [("E5", 0.5), ("G#5", 0.5), ("B5", 0.5), ("E6", 1.5), ("D#6", 0.5), ("E6", 0.5), ("F#6", 0.5), ("G#6", 3)],
                              [("E3", 1), ("B2", 1), ("C#3", 1), ("A2", 1), ("B2", 1), ("E3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("B4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)], [("E3", 2), ("C3", 2), ("B2", 2), ("E2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
