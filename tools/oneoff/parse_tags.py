# -*- coding: utf-8 -*-
"""Parse the HMI project's Tags.dat and compare tag addresses with:
   - the old xlsx variable table (stale)
   - the compiled VarNameAndAddr.csv (truth)
"""
import io
import re
import collections

import openpyxl

TAGS = r"C:\Users\16958\Desktop\歌尔现场\现场触摸屏\obj\Tags.dat"
XLSX = r"C:\Users\16958\Desktop\Plc_变量表.xlsx"
CSV = r"C:\Users\16958\Desktop\歌尔现场\小车\PLC\VarNameAndAddr.csv"
OUT = r"D:\C#\VM\artifacts\logs\20261029\tags_vs_table.txt"

raw = open(TAGS, "rb").read()
log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


# --- tag extraction: "<name>" ... "39#<addr>#-1"
tag_re = re.compile(rb"39#(\d+)#-1")
tags = {}
for m in tag_re.finditer(raw):
    addr = int(m.group(1))
    # walk backwards: skip control/binary bytes, collect the name
    i = m.start()
    # skip non-printable tail bytes
    j = i
    while j > 0 and raw[j - 1] < 0x20:
        j -= 1
    k = j
    while k > 0 and raw[k - 1] >= 0x20:
        k -= 1
    name_bytes = raw[k:j]
    if not name_bytes:
        continue
    try:
        name = name_bytes.decode("utf-8")
    except UnicodeDecodeError:
        name = name_bytes.decode("gb18030", "replace")
    name = name.strip()
    if not name:
        continue
    tags[name] = addr

w("tags parsed: %d" % len(tags))
w("sample: %s" % list(tags.items())[:8])

# --- compiled map
craw = open(CSV, "rb").read()
ctext = craw.decode("utf-16") if craw[:2] in (b"\xff\xfe", b"\xfe\xff") else craw.decode("utf-8", "replace")
compiled = {}
for line in ctext.splitlines():
    p = [x.strip() for x in line.split("\t")]
    if len(p) < 2 or not p[1].upper().startswith("0X"):
        continue
    compiled[p[0]] = int(p[1], 16)

# --- xlsx
wb = openpyxl.load_workbook(XLSX, data_only=True)
ws = wb["变量表"]
rows = list(ws.iter_rows(values_only=True))[1:]
old = {}
for r in rows:
    if not r[0]:
        continue
    try:
        old[str(r[0])] = int(str(r[4]), 16)
    except ValueError:
        pass

w("xlsx vars: %d | compiled bases: %d" % (len(old), len(compiled)))

# --- compare
name_re = re.compile(r"^(?P<base>[^\[]+)(\[(?P<idx>\d+)\])?(?P<rest>\..*)?$")


def compiled_addr(name):
    m = name_re.match(name)
    if m:
        base = m.group("base").strip()
        idx = int(m.group("idx")) if m.group("idx") else None
        rest = m.group("rest") or ""
        if base in compiled:
            if idx is None and not rest:
                return compiled[base], "scalar"
            return None, "array-base-needs-size"
    return None, "no-base"

same_as_old = 0
diff_from_old = 0
tag_missing = []
samples = []
tag_vs_compiled_mismatch = []

for name, oldaddr in old.items():
    if name not in tags:
        tag_missing.append(name)
        continue
    t = tags[name]
    if t == oldaddr:
        same_as_old += 1
    else:
        diff_from_old += 1
        if len(samples) < 30:
            samples.append((name, oldaddr, t))

w("\nxlsx vars present in HMI tags: %d (missing %d)" % (len(old) - len(tag_missing), len(tag_missing)))
w("  tag address == old xlsx address : %d" % same_as_old)
w("  tag address != old xlsx address : %d" % diff_from_old)
w("\n-- first 30 differing --")
for n, o, t in samples:
    w("  %-26s xlsx=0x%-9X tag=0x%08X" % (n, o, t))

w("\n-- missing from HMI tags (first 20) --")
for n in tag_missing[:20]:
    w("  %s" % n)

# check tags against compiled for base arrays
w("\n-- Alarm array check --")
for base in ("Alarm1", "Alarm2", "Alarm3"):
    base_addr = compiled.get(base)
    if base_addr is None:
        continue
    mism = 0
    tot = 0
    for i in range(100):
        nm = "%s[%d]" % (base, i)
        if nm in tags:
            tot += 1
            if tags[nm] != base_addr + i:
                mism += 1
    w("  %-8s compiled base=0x%08X  tags=%d  mismatched=%d" % (base, base_addr, tot, mism))

# scalar spot check against compiled
w("\n-- scalar tags vs compiled --")
for n in ("CyCurrentPage", "Rail_CyCurrentPage", "PLCHeart", "Alarm3_Tag", "CarDir"):
    if n in tags:
        w("  %-20s tag=0x%08X  compiled=%s" % (n, tags[n], ("0x%08X" % compiled[n]) if n in compiled else "-"))

log.close()
print("done")
