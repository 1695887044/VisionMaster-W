# -*- coding: utf-8 -*-
"""Parse AutoShop VarAddrMap.am (u16 len + name + NUL + u32 LE address) and
compare with the old variable table / rebuild it."""
import io
import struct
import collections

import openpyxl

AM = r"C:\Users\16958\Desktop\歌尔现场\小车\PLC\VarAddrMap.am"
XLSX = r"C:\Users\16958\Desktop\Plc_变量表.xlsx"
OUT = r"D:\C#\VM\artifacts\logs\20261029\am_map.txt"

raw = open(AM, "rb").read()
log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


# header: first entry starts where the u16 length makes sense; find the first
# plausible entry (len 3..200 followed by printable name)
start = 0
for cand in range(0, 64):
    n = struct.unpack_from("<H", raw, cand)[0]
    if 3 <= n <= 200:
        body = raw[cand + 2:cand + 2 + n - 1]
        if body and all(32 <= b < 127 for b in body):
            start = cand
            break

w("entries start at offset %d (header %s)" % (start, raw[:start].hex(" ")))

entries = []
pos = start
while pos + 2 <= len(raw):
    n = struct.unpack_from("<H", raw, pos)[0]
    if n < 2 or pos + 2 + n + 4 > len(raw):
        break
    name_bytes = raw[pos + 2:pos + 2 + n - 1]
    term = raw[pos + 2 + n - 1]
    if term != 0:
        break
    addr = struct.unpack_from("<I", raw, pos + 2 + n)[0]
    try:
        name = name_bytes.decode("utf-8")
    except UnicodeDecodeError:
        name = name_bytes.decode("gb18030", "replace")
    entries.append((name, addr))
    pos += 2 + n + 4

w("parsed entries: %d, consumed %d / %d bytes" % (len(entries), pos, len(raw)))
w("first 5: %s" % entries[:5])
w("last 5: %s" % entries[-5:])

am = {}
for n, a in entries:
    am.setdefault(n.upper(), a)   # first wins

w("unique uppercase names: %d" % len(am))

# --- compare with the old xlsx
wb = openpyxl.load_workbook(XLSX, data_only=True)
ws = wb["变量表"]
rows = list(ws.iter_rows(values_only=True))[1:]
old = [(str(r[0]), str(r[4])) for r in rows if r[0]]
w("xlsx rows: %d" % len(old))

hit = miss = same = diff = 0
miss_samples = []
diff_samples = []
for name, off in old:
    key = name.upper()
    if key in am:
        hit += 1
        try:
            o = int(off, 16)
        except ValueError:
            o = -1
        if o == am[key]:
            same += 1
        else:
            diff += 1
            if len(diff_samples) < 25:
                diff_samples.append((name, off, am[key]))
    else:
        miss += 1
        if len(miss_samples) < 30:
            miss_samples.append((name, off))

w("\nmatched in VarAddrMap: %d / %d" % (hit, len(old)))
w("  address equal   : %d" % same)
w("  address differs : %d" % diff)
w("\n-- differs (first 25) --")
for n, o, a in diff_samples:
    w("  %-30s xlsx=0x%-9s am=0x%08X" % (n, o, a))
w("\n-- missing (first 30) --")
for n, o in miss_samples:
    w("  %-30s xlsx=0x%s" % (n, o))

# --- alarm check
w("\n-- Alarm3 entries in VarAddrMap --")
for n, a in entries:
    if n.upper().startswith("ALARM3[") and a:
        pass
al3 = [(n, a) for n, a in entries if n.upper().startswith("ALARM3[")]
w("  count=%d  first=%s  idx79=%s" % (len(al3), al3[:2], [x for x in al3 if x[0].upper() == "ALARM3[79]"]))

log.close()
print("done")
