# -*- coding: utf-8 -*-
"""Dump the variable table around interesting addresses and show area/type stats."""
import io
import collections

import openpyxl

XLSX = r"C:\Users\16958\Desktop\Plc_变量表.xlsx"
OUT = r"D:\C#\VM\artifacts\logs\20261029\plc_vars_layout.txt"

wb = openpyxl.load_workbook(XLSX, data_only=True)
ws = wb["变量表"]
rows = list(ws.iter_rows(values_only=True))[1:]

log = io.open(OUT, "w", encoding="utf-8")


def w(s):
    log.write(s + "\n")


vars_ = []
for i, r in enumerate(rows, 2):
    if not r[0]:
        continue
    vars_.append(dict(row=i, name=r[0], desc=r[1], dtype=r[2], area=r[3],
                      offset=r[4], bit=r[5], station=r[6], db=r[7],
                      length=r[8], elembytes=r[9], group=r[13]))

w("total vars: %d" % len(vars_))
w("areas: %s" % dict(collections.Counter(v["area"] for v in vars_)))
w("types: %s" % dict(collections.Counter(v["dtype"] for v in vars_)))
w("groups: %s" % dict(collections.Counter(v["group"] for v in vars_)))
w("lengths != 1: %s" % [(v["name"], v["length"]) for v in vars_ if str(v["length"]) != "1"][:40])

u = []
for v in vars_:
    if v["area"] == "U":
        try:
            v["start"] = int(str(v["offset"]), 16)
        except ValueError:
            continue
        u.append(v)
u.sort(key=lambda v: v["start"])
w("\nU vars: %d" % len(u))
w("\n-- all U vars sorted by address --")
for v in u:
    w("0x%08X  %-22s %-14s len=%s %s" % (v["start"], v["name"], v["dtype"], v["length"], v["desc"] or ""))

w("\n-- non-U vars --")
for v in vars_:
    if v["area"] != "U":
        w("%-4s %-8s %-22s %-14s len=%s" % (v["area"], v["offset"], v["name"], v["dtype"], v["length"]))

log.close()
print("done")
