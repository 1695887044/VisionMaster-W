# -*- coding: utf-8 -*-
"""Probe 2: verify U mapping self-consistency (heartbeat toggle, plausible values)
and scan wider ranges for set bits."""
import io
import struct
import time

HOST, PORT = "127.0.0.1", 12939
OUT = r"D:\C#\VM\artifacts\logs\20261029\easynet_scan2.txt"
READ_TEMPLATE = bytes.fromhex("16000103010000000100010000004000600010000000")


def build_read(bit_addr, bits):
    f = bytearray(READ_TEMPLATE)
    f[14:18] = struct.pack("<i", bit_addr)
    f[18] = bits & 0xFF
    f[19] = (bits >> 8) & 0xFF
    f[20] = (bits >> 16) & 0xFF
    return bytes(f)


class Client:
    def __init__(self):
        import socket
        self.s = socket.create_connection((HOST, PORT), timeout=5)
        self.s.settimeout(10)

    def recv_exact(self, n):
        buf = b""
        while len(buf) < n:
            c = self.s.recv(n - len(buf))
            if not c:
                raise IOError("closed")
            buf += c
        return buf

    def raw(self, frame):
        self.s.sendall(struct.pack("<H", len(frame)) + frame[2:])
        head = self.recv_exact(2)
        total = struct.unpack("<H", head)[0]
        return head + self.recv_exact(total - 2)

    def read_data(self, bit_addr, bits):
        resp = self.raw(build_read(bit_addr, bits))
        if len(resp) >= 9 and resp[8] == 0x0F:
            raise IOError("PLC err %d" % struct.unpack("<H", resp[10:12])[0])
        return resp[22:]

    def read_bits(self, bit_addr, count):
        data = self.read_data(bit_addr, count)
        return [(data[i // 8] >> (i % 8)) & 1 for i in range(count)]


def main():
    log = io.open(OUT, "w", encoding="utf-8")

    def w(s):
        log.write(s + "\n")

    c = Client()

    # 1) heartbeat toggle test at U 0x100064 (PLCHeart)
    samples = []
    for _ in range(20):
        samples.append(c.read_bits(0x100064, 1)[0])
        time.sleep(0.1)
    w("PLCHeart @0x100064 over 2s: %s" % "".join(str(x) for x in samples))

    # 2) plausible values at table addresses
    def word(addr, n=1):
        d = c.read_data(addr, n * 16)
        return [struct.unpack_from("<H", d, 2 * i)[0] for i in range(n)]

    def dword(addr):
        d = c.read_data(addr, 32)
        return struct.unpack("<I", d[:4])[0], struct.unpack("<f", d[:4])[0]

    for addr, name in ((0x2100020, "Rail_CyCurrentPage short"),
                       (0x2100000, "raw 0x2100000 (unknown)"),
                       (0x2100280, "Alarm3_All_1 int"),
                       (0x21002A0, "Alarm3_All_2 int"),
                       (0x21002C0, "Alarm3_All_3 int"),
                       (0x21000D6, "Alarm3_Tag bool")):
        if name.endswith("int"):
            i, f = dword(addr)
            w("%-32s @0x%X = u32=0x%08X (%d) float=%g" % (name, addr, i, i, f))
        else:
            w("%-32s @0x%X = word=0x%04X (%d)" % (name, addr, word(addr)[0], word(addr)[0]))

    # 3) wide scan for set bits
    ranges = [(0x100000, 0x110000), (0x4020000, 0x4030000), (0x5010000, 0x5020000),
              (0x2100000, 0x2110000), (0x2000000, 0x2010000), (0x0000000, 0x0010000)]
    for lo, hi in ranges:
        found = []
        start = lo
        while start < hi:
            span = min(8000, hi - start)
            try:
                bits = c.read_bits(start, span)
            except Exception as ex:
                w("scan 0x%X..0x%X: chunk 0x%X FAILED %s" % (lo, hi, start, ex))
                start += span
                continue
            found += [start + i for i, b in enumerate(bits) if b]
            start += span
        w("scan 0x%X..0x%X: %d set bits %s" % (lo, hi, len(found), [hex(x) for x in found][:60]))

    log.close()
    print("done")


if __name__ == "__main__":
    main()
