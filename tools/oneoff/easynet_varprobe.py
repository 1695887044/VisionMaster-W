# -*- coding: utf-8 -*-
"""One-off probe: compare EasyNet segment reads with single-bit reads.

Reads the PLC variable table (xlsx), replays the app's U|B segment plan
(merge by Start, cap = 8000 bits), then compares each variable's bit against
a direct single-bit read of the same address.
"""
import io
import struct
import sys
import collections

import openpyxl

XLSX = r"C:\Users\16958\Desktop\Plc_变量表.xlsx"
HOST = "127.0.0.1"
PORT = 12939
OUT = r"D:\C#\VM\artifacts\logs\20261029\easynet_bitprobe.txt"

READ_TEMPLATE = bytes.fromhex("16000103010000000100010000004000600010000000")


def build_read(bit_addr, bits):
    f = bytearray(READ_TEMPLATE)
    f[14:18] = struct.pack("<i", bit_addr)
    f[18] = bits & 0xFF
    f[19] = (bits >> 8) & 0xFF
    f[20] = (bits >> 16) & 0xFF
    return bytes(f)


class Client:
    def __init__(self, host, port):
        import socket
        self.s = socket.create_connection((host, port), timeout=5)
        self.s.settimeout(5)

    def recv_exact(self, n):
        buf = b""
        while len(buf) < n:
            chunk = self.s.recv(n - len(buf))
            if not chunk:
                raise IOError("remote closed")
            buf += chunk
        return buf

    def xfer(self, frame):
        self.s.sendall(struct.pack("<H", len(frame)) + frame[2:])
        head = self.recv_exact(2)
        total = struct.unpack("<H", head)[0]
        rest = self.recv_exact(total - 2)
        return head + rest

    def read_bits(self, bit_addr, count):
        req = build_read(bit_addr, count)
        resp = self.xfer(req)
        if len(resp) >= 9 and resp[8] == 0x0F:
            code = struct.unpack("<H", resp[10:12])[0] if len(resp) >= 12 else -1
            raise IOError("PLC error %d (addr=0x%X bits=%d)" % (code, bit_addr, count))
        data = resp[22:]
        need = (count + 7) // 8
        if len(data) < need:
            raise IOError("short data: %d < %d (addr=0x%X bits=%d)" % (len(data), need, bit_addr, count))
        return [(data[i // 8] >> (i % 8)) & 1 for i in range(count)]


def load_vars():
    wb = openpyxl.load_workbook(XLSX, data_only=True)
    ws = wb["变量表"]
    rows = list(ws.iter_rows(values_only=True))[1:]
    out = []
    for r in rows:
        if not r[0]:
            continue
        name, desc, dtype, area, offset = r[0], r[1], r[2], r[3], r[4]
        length = r[8] if r[8] not in (None, "") else "1"
        out.append(dict(name=name, desc=desc, dtype=dtype, area=area,
                        offset=str(offset), length=str(length)))
    return out


def main():
    variables = load_vars()
    log = io.open(OUT, "w", encoding="utf-8")

    def w(s):
        log.write(s + "\n")

    u_bool = []
    for v in variables:
        if v["area"] == "U" and "布尔" in (v["dtype"] or ""):
            try:
                v["start"] = int(v["offset"], 16)
                v["count"] = int(float(v["length"]))
            except ValueError:
                continue
            u_bool.append(v)

    u_bool.sort(key=lambda v: v["start"])
    w("U bool vars: %d, range 0x%X .. 0x%X" % (len(u_bool), u_bool[0]["start"], u_bool[-1]["start"]))

    # Replay the app's merge: greedy, gap-tolerant (ethernet => unlimited), cap 8000 bits
    segments = []
    cur = None
    for v in u_bool:
        end = v["start"] + v["count"]
        if cur is not None and max(cur["end"], end) - cur["start"] > 8000:
            segments.append(cur)
            cur = None
        if cur is None:
            cur = dict(start=v["start"], end=end, members=[])
        cur["end"] = max(cur["end"], end)
        cur["members"].append(v)
    if cur:
        segments.append(cur)
    w("segments: %d -> %s" % (len(segments), [(hex(s["start"]), s["end"] - s["start"]) for s in segments]))

    c = Client(HOST, PORT)

    # 1) baseline: single-bit read of the alarm bit
    target = [v for v in u_bool if v["name"] == "Alarm3[79]"][0]
    single = c.read_bits(target["start"], 1)[0]
    w("single read %s @0x%X = %d" % (target["name"], target["start"], single))

    # 2) neighborhood: single-bit reads around it
    w("\n-- single-bit reads 0x%X..0x%X --" % (target["start"] - 8, target["start"] + 8))
    neigh = []
    for a in range(target["start"] - 8, target["start"] + 9):
        try:
            b = c.read_bits(a, 1)[0]
        except Exception as ex:
            b = "ERR:%s" % ex
        neigh.append((a, b))
    w(" ".join("%X=%s" % (a, b) for a, b in neigh))

    # 3) byte-aligned 16-bit read around it, decoded LSB-first
    base = target["start"] & ~0xF
    bits = c.read_bits(base, 16)
    w("\nread16 @0x%X LSB-first = %s" % (base, "".join(str(x) for x in bits)))
    w("  bit %d (Alarm3[79]) in that read = %d" % (target["start"] - base, bits[target["start"] - base]))

    # 4) full segment replay: read each segment and decode every member
    w("\n-- segment replay --")
    mismatches = 0
    for seg in segments:
        span = seg["end"] - seg["start"]
        try:
            data = c.read_bits(seg["start"], span)
        except Exception as ex:
            w("segment 0x%X span %d FAILED: %s" % (seg["start"], span, ex))
            continue
        bad = []
        for v in seg["members"]:
            off = v["start"] - seg["start"]
            seg_val = data[off] if 0 <= off < len(data) else "OOR"
            try:
                ref = c.read_bits(v["start"], v["count"])
            except Exception as ex:
                ref = "ERR:%s" % ex
            if isinstance(ref, list):
                ref_cmp = ref[0] if v["count"] == 1 else tuple(ref)
            else:
                ref_cmp = ref
            seg_cmp = seg_val if v["count"] == 1 else tuple(data[off:off + v["count"]])
            if seg_cmp != ref_cmp:
                bad.append((v["name"], hex(v["start"]), seg_cmp, ref_cmp))
        if bad:
            mismatches += len(bad)
            w("segment 0x%X span %d: %d/%d mismatched" % (seg["start"], span, len(bad), len(seg["members"])))
            for name, addr, got, ref in bad[:40]:
                w("   %-14s %s seg=%s single=%s" % (name, addr, got, ref))
        else:
            w("segment 0x%X span %d: all %d members match" % (seg["start"], span, len(seg["members"])))

    w("\ntotal mismatches: %d" % mismatches)
    log.close()
    print("done, see", OUT)


if __name__ == "__main__":
    main()
