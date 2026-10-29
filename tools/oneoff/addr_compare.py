# -*- coding: utf-8 -*-
"""Compare the xlsx variable table against AutoShop's compiled VarNameAndAddr.csv."""
import io
import re
import collections

import openpyxl

XLSX = r"C:\Users\16958\Desktop\Plc_变量表.xlsx"
CSV = r"C:\Users\16958\Desktop\歌尔现场\小车\PLC\VarNameAndAddr.csv"
OUT = r"D:\C#\VM\artifacts\logs\20261029\addr_compare.txt"

log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


# ---- CSV ----
raw = open(CSV, "rb").read()
if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
    text = raw.decode("utf-16")
elif raw[:3] == b"\xef\xbb\xbf":
    text = raw.decode("utf-8-sig")
else:
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        text = raw.decode("gbk")

csv_map = {}
for line in text.splitlines():
    line = line.rstrip("\r\n")
    if not line.strip():
        continue
    parts = line.split("\t")
    if len(parts) < 2:
        parts = line.split(",")
    if len(parts) < 2:
        continue
    name = parts[0].strip()
    addr = parts[1].strip()
    desc = parts[2].strip() if len(parts) > 2 else ""
    cnt = parts[3].strip() if len(parts) > 3 else ""
    if not addr.upper().startswith("0X"):
        continue
    try:
        a = int(addr, 16)
    except ValueError:
        continue
    csv_map[name] = dict(addr=a, desc=desc, count=cnt)

w("csv entries: %d" % len(csv_map))
w("first 10: %s" % list(csv_map.items())[:10])

# ---- XLSX ----
wb = openpyxl.load_workbook(XLSX, data_only=True)
ws = wb["变量表"]
rows = list(ws.iter_rows(values_only=True))[1:]
x = []
for r in rows:
    if not r[0]:
        continue
    x.append(dict(name=str(r[0]), desc=r[1], dtype=r[2], area=r[3], off=str(r[4]),
                  length=str(r[8]) if r[8] is not None else "1"))
w("xlsx entries: %d" % len(x))

name_re = re.compile(r"^(?P<base>.+?)\[(?P<idx>\d+)\]$")

# element size in bits by xlsx dtype
def bits_of(dtype):
    d = dtype or ""
    if "布尔" in d:
        return 1
    if "短整数数组" in d:
        return 16
    if "短整数" in d:
        return 16
    if "整数" in d:
        return 32
    if "单精度" in d:
        return 32
    return None

matched = 0
deltas = collections.Counter()
samples = []
unmatched = []
for v in x:
    try:
        xaddr = int(v["off"], 16)
    except ValueError:
        unmatched.append((v["name"], v["off"], "bad hex"))
        continue
    m = name_re.match(v["name"])
    if m:
        base, idx = m.group("base"), int(m.group("idx"))
        entry = csv_map.get(base)
        if entry:
            bw = bits_of(v["dtype"])
            if bw is None:
                unmatched.append((v["name"], v["off"], "dtype " + str(v["dtype"])))
                continue
            csv_addr = entry["addr"] + idx * bw
            matched += 1
            d = xaddr - csv_addr
            deltas[d] += 1
            if len(samples) < 25:
                samples.append((v["name"], v["off"], "0x%X" % csv_addr, "0x%X" % d))
            continue
    # scalar
    entry = csv_map.get(v["name"])
    if entry:
        matched += 1
        d = xaddr - entry["addr"]
        deltas[d] += 1
        if len(samples) < 25:
            samples.append((v["name"], v["off"], "0x%X" % entry["addr"], "0x%X" % d))
    else:
        unmatched.append((v["name"], v["off"], "no csv entry"))

w("\nmatched: %d / %d   unmatched: %d" % (matched, len(x), len(unmatched)))
w("\ndelta histogram (xlsx - csv):")
for d, c in deltas.most_common(30):
    w("  delta 0x%X (%d): %d vars" % (d if d >= 0 else -d, d, c))
w("\nsamples:")
for s in samples:
    w("  %-24s xlsx=0x%-8s csv=%-10s delta=%s" % s)
w("\nunmatched samples:")
for u in unmatched[:30]:
    w("  %s" % (u,))

# per-base comparison for arrays
w("\n-- array bases present in both --")
bases = set()
for v in x:
    m = name_re.match(v["name"])
    if m and m.group("base") in csv_map:
        bases.add(m.group("base"))
for b in sorted(bases):
    elems = [v for v in x if name_re.match(v["name"]) and name_re.match(v["name"]).group("base") == b]
    elems.sort(key=lambda v: int(name_re.match(v["name"]).group("idx")))
    first = elems[0]
    last = elems[-1]
    w("%-14s csv=0x%-8X count=%-4s | xlsx %s..%s (0x%s..0x%s) n=%d  dtype=%s" % (
        b, csv_map[b]["addr"], csv_map[b]["count"], first["name"], last["name"],
        first["off"], last["off"], len(elems), first["dtype"]))

# tag vars
w("\n-- *_Tag / scalar vars in both --")
for v in x:
    if v["name"] in csv_map and not name_re.match(v["name"]):
        w("%-18s xlsx=0x%-8s csv=0x%-8X dtype=%s" % (v["name"], v["off"], csv_map[v["name"]]["addr"], v["dtype"]))

log.close()
print("done")
