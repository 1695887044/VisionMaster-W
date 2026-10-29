# -*- coding: utf-8 -*-
"""Verify: compiled address of Alarm3[79] vs the xlsx address, plus CSV format."""
import io
import re
import struct
import socket

CSV = r"C:\Users\16958\Desktop\歌尔现场\小车\PLC\VarNameAndAddr.csv"
OUT = r"D:\C#\VM\artifacts\logs\20261029\verify_alarm379.txt"

raw = open(CSV, "rb").read()
text = raw.decode("utf-16") if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else raw.decode("utf-8", "replace")

log = io.open(OUT, "w", encoding="utf-8")


def w(s=""):
    log.write(s + "\n")


entries = []
for line in text.splitlines():
    parts = [p.strip() for p in line.split("\t")]
    if len(parts) < 2 or not parts[1].upper().startswith("0X"):
        continue
    try:
        addr = int(parts[1], 16)
    except ValueError:
        continue
    entries.append((parts[0], addr, parts[2] if len(parts) > 2 else "", parts[3] if len(parts) > 3 else ""))

w("csv lines with address: %d" % len(entries))
w("\n-- entries mentioning Alarm / Car / Hmi / sHmi --")
for n, a, d, c in entries:
    if re.search(r"Alarm|sHmi|Hmi|Car", n):
        w("%-24s 0x%08X  cnt=%-6s %s" % (n, a, c, d))

w("\n-- variables near 0x210118F (compiled Alarm3[79]) --")
for n, a, d, c in sorted(entries, key=lambda e: e[1]):
    if 0x2101000 <= a <= 0x2101400:
        w("%-24s 0x%08X  cnt=%-6s" % (n, a, c))

# --- protocol read ---
READ_TEMPLATE = bytes.fromhex("16000103010000000100010000004000600010000000")


def build_read(bit_addr, bits):
    f = bytearray(READ_TEMPLATE)
    f[14:18] = struct.pack("<i", bit_addr)
    f[18] = bits & 0xFF
    f[19] = (bits >> 8) & 0xFF
    f[20] = (bits >> 16) & 0xFF
    return bytes(f)


s = socket.create_connection(("127.0.0.1", 12939), timeout=5)
s.settimeout(10)


def recv_exact(n):
    buf = b""
    while len(buf) < n:
        c = s.recv(n - len(buf))
        if not c:
            raise IOError("closed")
        buf += c
    return buf


def read_bits(addr, count):
    frame = build_read(addr, count)
    s.sendall(struct.pack("<H", len(frame)) + frame[2:])
    head = recv_exact(2)
    total = struct.unpack("<H", head)[0]
    resp = head + recv_exact(total - 2)
    if resp[8] == 0x0F:
        raise IOError("PLC err %d" % struct.unpack("<H", resp[10:12])[0])
    data = resp[22:]
    return [(data[i // 8] >> (i % 8)) & 1 for i in range(count)], data


w("\n-- live reads --")
for label, addr in (("xlsx Alarm3[79]  U210019F", 0x210019F),
                    ("compiled Alarm3[79] U210118F", 0x210118F)):
    bits, data = read_bits(addr, 1)
    w("%-32s @0x%X = %d" % (label, addr, bits[0]))

bits, data = read_bits(0x2101140, 100)
on = [i for i, b in enumerate(bits) if b]
w("compiled Alarm3[0..99] @0x2101140: set indices = %s" % on)
w("   data = %s" % data.hex(" "))

bits, data = read_bits(0x2100150, 100)
on = [i for i, b in enumerate(bits) if b]
w("xlsx     Alarm3[0..99] @0x2100150: set indices = %s" % on)

log.close()
print("done")
