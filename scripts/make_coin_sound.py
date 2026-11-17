"""Synthesize the clock-out sound: a handful of gold coins scattered onto a table.

Each coin is a few short, bright partials (small thin discs ring high and briefly), and it
bounces two or three times: every bounce comes sooner and softer than the last. Coins land
front-loaded over about a second, each panned to its own spot, and a short synthetic room
reverb blends them into one shower. Everything is generated here, so there is nothing to license.

usage: python scripts/make_coin_sound.py [out.wav] [--style shower|sparkle|pour]
  shower   light scatter of coins (default, embedded in the exe)
  sparkle  the same scatter opened by a soft two-note chime
  pour     more and slightly bigger coins, a fuller "treasure" pour
Writes 44.1 kHz 16-bit stereo.
"""
import sys
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt, fftconvolve

RATE = 44100
LENGTH = 1.5
RATIOS = [1.0, 1.52, 2.27]            # few partials: a clean "ting" instead of a clang
LEVELS = [1.0, 0.42, 0.18]

STYLES = {
    #          coins  pitch range (Hz)  big-coin share  ring (s)      spread (s)
    'shower':  dict(n=26, lo=3600, hi=6800, big=0.15, ring=(0.05, 0.12), spread=1.0),
    'sparkle': dict(n=22, lo=3800, hi=7000, big=0.10, ring=(0.05, 0.11), spread=0.9, chime=True),
    'pour':    dict(n=48, lo=3000, hi=6200, big=0.30, ring=(0.06, 0.14), spread=1.15),
}


def tinkle(f, ring, rng):
    """One contact: a short bright ring with a 1 ms attack so there is no click."""
    n = int(min(0.6, ring * 6) * RATE)
    t = np.arange(n) / RATE
    x = np.zeros(n)
    for k, (ratio, level) in enumerate(zip(RATIOS, LEVELS)):
        fk = f * ratio * (1 + rng.uniform(-0.004, 0.004))
        if fk > 15000:
            break
        x += level * np.sin(2 * np.pi * fk * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / (ring / (1 + 0.8 * k)))
    return x * np.minimum(1, t / 0.001)


def chime(rng):
    """Soft two-note bell (a rising fourth), the "ding~" before the coins land."""
    out = np.zeros(int(0.9 * RATE))
    for at, f, g in [(0.0, 1568.0, 0.55), (0.075, 2093.0, 0.7)]:
        n = out.size - int(at * RATE)
        t = np.arange(n) / RATE
        x = (np.sin(2 * np.pi * f * t) + 0.25 * np.sin(2 * np.pi * 2 * f * t) * np.exp(-t / 0.08)) * np.exp(-t / 0.28)
        out[int(at * RATE):] += g * x * np.minimum(1, t / 0.003)
    return out


def room(n, rng, seconds=0.45, decay=0.11):
    """Decaying filtered noise as a reverb tail; different noise per channel gives width."""
    t = np.arange(int(seconds * RATE)) / RATE
    ir = rng.normal(0, 1, t.size) * np.exp(-t / decay)
    ir = sosfilt(butter(2, 7000, 'low', fs=RATE, output='sos'), ir)
    return ir / np.sqrt(np.sum(ir ** 2))


def make(style):
    s = STYLES[style]
    rng = np.random.default_rng(25)                      # fixed seed: the same sound every build
    left, right = np.zeros(int(LENGTH * RATE)), np.zeros(int(LENGTH * RATE))
    start = 0.0
    if s.get('chime'):
        c = chime(rng)
        left[:c.size] += 0.8 * c
        right[:c.size] += 0.8 * c
        start = 0.16

    def add(at, x, pan, gain):
        i = int(at * RATE)
        if i >= left.size:
            return
        m = min(x.size, left.size - i)
        a = (pan + 1) * np.pi / 4                        # equal-power pan
        left[i:i + m] += gain * np.cos(a) * x[:m]
        right[i:i + m] += gain * np.sin(a) * x[:m]

    lands = np.sort(s["spread"] * rng.beta(1.5, 3.2, s["n"]))          # most coins land early
    lands += start + 0.01 - lands[0]                                   # first coin right away
    for k, at in enumerate(lands):
        big = rng.random() < s['big']
        f = rng.uniform(2400, 3400) if big else rng.uniform(s['lo'], s['hi'])
        ring = rng.uniform(*s['ring']) * (1.4 if big else 1.0)
        pan = rng.uniform(-0.75, 0.75)
        gain = rng.uniform(0.55, 1.0) * (0.75 if big else 1.0) * (1 - 0.35 * k / len(lands))
        gap = rng.uniform(0.045, 0.085)
        for b in range(rng.integers(2, 4)):               # the coin bounces: sooner and softer each time
            add(at, tinkle(f * (1 + 0.002 * b), ring * (0.8 ** b), rng), pan, gain)
            at += gap
            gap *= 0.62
            gain *= 0.42

    dry = np.stack([left, right])
    band = butter(2, [1200, 11000], 'band', fs=RATE, output='sos')    # no mud below, no hiss above
    dry = sosfilt(band, dry, axis=1)
    wet = np.stack([fftconvolve(ch, room(ch.size, rng))[:ch.size] for ch in dry])
    out = dry + 0.35 * wet
    fade = np.ones(out.shape[1])
    fade[-int(0.35 * RATE):] = np.linspace(1, 0, int(0.35 * RATE)) ** 2
    out *= fade
    return out * (0.6 / np.max(np.abs(out)))             # peak about -4.4 dBFS


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    style = sys.argv[sys.argv.index('--style') + 1] if '--style' in sys.argv else 'shower'
    args = [a for a in args if a not in STYLES]
    out = args[0] if args else 'assets/coin.wav'
    x = make(style)
    wavfile.write(out, RATE, (x.T * 32767).astype(np.int16))
    print(f"{out}: {style}, {x.shape[1] / RATE:.2f} s stereo, peak {np.max(np.abs(x)):.2f}, rms {np.sqrt(np.mean(x ** 2)):.3f}")


if __name__ == '__main__':
    main()
