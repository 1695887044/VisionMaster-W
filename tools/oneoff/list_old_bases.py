# -*- coding: utf-8 -*-
"""List base names in the old variable table."""
import io
import re
import collections

import openpyxl

wb = openpyxl.load_workbook(r"C:\Users\16958\Desktop\Plc_变量表.xlsx", data_only=True)
ws = wb["变量表"]
rows = list(ws.iter_rows(values_only=True))[1:]

rx = re.compile(r"^(?P<base>[^\[]+)(\[(?P<idx>\d+)\])?(?P<rest>\..*)?$")
count = collections.Counter()
first = {}
for r in rows:
    if not r[0]:
        continue
    m = rx.match(str(r[0]))
    b = m.group("base").strip() if m else str(r[0])
    count[b] += 1
    first.setdefault(b, (r[3], r[4], r[2]))

out = io.open(r"D:\C#\VM\artifacts\logs\20261029\old_bases.txt", "w", encoding="utf-8")
for b, n in sorted(count.items()):
    a, o, t = first[b]
    out.write("%-34s n=%-4d area=%-3s first=%-9s %s\n" % (b, n, a, o, t))
out.close()
print(len(count))
