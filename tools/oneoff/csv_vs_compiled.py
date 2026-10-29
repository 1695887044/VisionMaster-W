# -*- coding: utf-8 -*-
"""Analyse the source CSV (12.csv): connections, name bases, and how well the
compiled address map (VarNameAndAddr.csv) can cover them."""
import io
import re
import collections

CSV = r"C:\Users\16958\Desktop\12.csv"
MAP = r"C:\Users\16958\Desktop\歌尔现场\小车\PLC\VarNameAndAddr.csv"
OUT = r"D:\C#\VM\artifacts\logs\20261029\csv_vs_compiled.txt"

text = open(CSV, "rb").read().decode("gb18030", "replace")
lines = [l for l in text.splitlines() if l.strip() and not l.lstrip().startswith("#")]

log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


rows = []
for l in lines:
    f = l.split(",")
    if len(f) < 6:
        continue
    name, conn, dtype, length, arr, addr = f[0].strip(), f[1].strip(), f[2].strip(), f[3].strip(), f[4].strip(), f[5].strip()
    if not name or name == "名称":
        continue
    rows.append(dict(name=name, conn=conn, dtype=dtype, length=length, arr=arr, addr=addr))

w("csv rows: %d" % len(rows))
w("connections: %s" % dict(collections.Counter(r["conn"] for r in rows)))
w("types: %s" % dict(collections.Counter(r["dtype"] for r in rows)))

# compiled map
craw = open(MAP, "rb").read()
ctext = craw.decode("utf-16") if craw[:2] in (b"\xff\xfe", b"\xfe\xff") else craw.decode("utf-8", "replace")
compiled = {}
for line in ctext.splitlines():
    p = [x.strip() for x in line.split("\t")]
    if len(p) < 2 or not p[1].upper().startswith("0X"):
        continue
    compiled[p[0]] = int(p[1], 16)
w("compiled bases: %d" % len(compiled))

rx = re.compile(r"^(?P<base>[^\[]+)(\[(?P<idx>\d+)\])?(?P<rest>\..*)?$")

per_conn = collections.defaultdict(lambda: collections.Counter())
unmatched = collections.defaultdict(list)
for r in rows:
    m = rx.match(r["name"])
    base = m.group("base").strip() if m else r["name"]
    if base in compiled:
        per_conn[r["conn"]]["matched"] += 1
    else:
        per_conn[r["conn"]]["unmatched"] += 1
        if len(unmatched[r["conn"]]) < 25:
            unmatched[r["conn"]].append((r["name"], r["addr"]))

w("\n-- per connection coverage by compiled map --")
for c, cnt in per_conn.items():
    w("  %-10s matched=%-5d unmatched=%-5d" % (c, cnt["matched"], cnt["unmatched"]))

for c, items in unmatched.items():
    w("\n-- %s unmatched samples --" % c)
    for n, a in items:
        w("   %-30s %s" % (n, a))

# bases of the Plc connection
bases = collections.Counter()
for r in rows:
    if r["conn"] != "Plc":
        continue
    m = rx.match(r["name"])
    b = m.group("base").strip() if m else r["name"]
    bases[b] += 1
w("\n-- Plc-connection bases (%d) --" % len(bases))
for b, n in sorted(bases.items()):
    mark = "OK " if b in compiled else "?? "
    w("  %s%-30s n=%d" % (mark, b, n))

log.close()
print("done")
