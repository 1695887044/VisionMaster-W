# -*- coding: utf-8 -*-
"""Inspect the AutoShop-exported variable CSV (GB18030)."""
import io

SRC = r"C:\Users\16958\Desktop\12.csv"
OUT = r"D:\C#\VM\artifacts\logs\20261029\src_csv.txt"

raw = open(SRC, "rb").read()
text = raw.decode("gb18030", "replace")
lines = text.splitlines()

log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


w("lines: %d" % len(lines))
for i, line in enumerate(lines[:20]):
    w("%3d| %s" % (i, line))

w("\n-- header row candidates --")
for i, line in enumerate(lines[:40]):
    if "变量名" in line or "变量名称" in line or "偏移" in line:
        w("%3d| %s" % (i, line))

w("\n-- Alarm3 rows --")
for i, line in enumerate(lines):
    if "Alarm3" in line:
        w("%3d| %s" % (i, line))

w("\n-- rows mentioning 210019F or 210118F --")
for i, line in enumerate(lines):
    if "210019F" in line.upper() or "210118F" in line.upper():
        w("%3d| %s" % (i, line))

log.close()
print("done")
