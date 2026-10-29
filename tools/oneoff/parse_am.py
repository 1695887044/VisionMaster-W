# -*- coding: utf-8 -*-
"""Parse AutoShop VarAddrMap.am: name -> address map (uppercase symbol names)."""
import io
import re
import struct
import collections

SRC = r"C:\Users\16958\Desktop\歌尔现场\小车\PLC\VarAddrMap.am"
OUT = r"D:\C#\VM\artifacts\logs\20261029\varaddrmap.txt"

raw = open(SRC, "rb").read()
log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


w("size: %d" % len(raw))
w("head: %s" % raw[:64].hex(" "))

# show bytes around the first ALARM1[0]
i = raw.find(b"ALARM1[0]")
w("\nfirst ALARM1[0] at %d" % i)
w("context: %s" % raw[max(0, i - 48):i + 96].hex(" "))
w("ascii  : %r" % raw[max(0, i - 48):i + 96])

# try to find a name/address interleave: look at the next few names
positions = [m.start() for m in re.finditer(rb"ALARM1\[\d+\]", raw)]
w("\nALARM1 occurrences: %d, first positions %s" % (len(positions), positions[:6]))
for p in positions[:4]:
    seg = raw[p:p + 64]
    w("  at %d: %r" % (p, seg))

# guess: name is NUL-terminated or length-prefixed, followed by 4-byte address
# examine deltas between consecutive names
deltas = collections.Counter()
for a, b in zip(positions, positions[1:]):
    deltas[b - a] += 1
w("\nname-to-name deltas: %s" % deltas.most_common(8))

log.close()
print("done")
