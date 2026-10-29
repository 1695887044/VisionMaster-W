# -*- coding: utf-8 -*-
"""Check what address the app's solution file has for Alarm3[79]."""
import io
import json
import re

PATHS = [
    r"D:\C#\VM\解决方案\视觉样例对拍.vms",
    r"D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows\Autosave\recovered\视觉样例对拍-20261029-180724.vms",
]
OUT = r"D:\C#\VM\artifacts\logs\20261029\vms_check.txt"

log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


for p in PATHS:
    try:
        raw = open(p, "rb").read()
    except OSError as ex:
        w("%s: %s" % (p, ex))
        continue
    w("=== %s (%d bytes) head=%s" % (p, len(raw), raw[:12].hex(" ")))
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        w("  not utf-8 (skip)")
        continue

    for key in ("Alarm3[79]", "210019F", "210118F", "EasyNet", "Conn_172334", "127.0.0.1"):
        w("  %-14s at %d" % (key, text.find(key)))

    # dump the neighbourhood of the first EasyNet variable entry
    m = re.search(r'"VariableName"\s*:\s*"Alarm3\[79\]"', text)
    if not m:
        m = re.search(r'Alarm3\[79\]', text)
    if m:
        s = max(0, m.start() - 400)
        w("  --- context ---")
        w("  " + text[s:m.start() + 400].replace("\n", " ")[:900])

    # count variables mentioning 210019F
    w("  occurrences of 210019F: %d" % text.count("210019F"))
    w("  occurrences of U2100: %d" % text.count("U2100"))

log.close()
print("done")
