# -*- coding: utf-8 -*-
"""List symbol roots inside VarAddrMap.am."""
import io
import struct
import collections

raw = open(r"C:\Users\16958\Desktop\歌尔现场\小车\PLC\VarAddrMap.am", "rb").read()
pos = 30
names = []
while pos + 2 <= len(raw):
    n = struct.unpack_from("<H", raw, pos)[0]
    if n < 2 or pos + 2 + n + 4 > len(raw):
        break
    nm = raw[pos + 2:pos + 2 + n - 1]
    if raw[pos + 2 + n - 1] != 0:
        break
    addr = struct.unpack_from("<I", raw, pos + 2 + n)[0]
    names.append((nm.decode("utf-8", "replace"), addr))
    pos += 2 + n + 4

out = io.open(r"D:\C#\VM\artifacts\logs\20261029\am_roots.txt", "w", encoding="utf-8")
for k in ("SHMI", "RECIPE", "SV_CMD", "RAIL", "STATICE", "VIRBTN", "SHOWSTR", "CLT_", "ALARM"):
    sel = [x for x in names if x[0].upper().startswith(k)]
    out.write("%s: %d  e.g. %s\n" % (k, len(sel), sel[:3]))

out.write("\n-- distinct roots --\n")
roots = collections.Counter()
for nm, a in names:
    r = nm.split("[")[0].split(".")[0].upper()
    roots[r] += 1
for r, c in roots.most_common(80):
    out.write("%-30s %d\n" % (r, c))
out.close()
print("ok")
