# -*- coding: utf-8 -*-
"""Scan the EasyNet U area for set bits and dump raw frames."""
import io
import struct

HOST, PORT = "127.0.0.1", 12939
OUT = r"D:\C#\VM\artifacts\logs\20261029\easynet_scan.txt"
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

    def xfer(self, frame):
        self.s.sendall(struct.pack("<H", len(frame)) + frame[2:])
        total = struct.unpack("<H", self.recv_exact(2))[0]
        return self.recv_exact(2)[:0] + self.recv_exact(total - 2) if False else None

    def raw(self, frame):
        self.s.sendall(struct.pack("<H", len(frame)) + frame[2:])
        head = self.recv_exact(2)
        total = struct.unpack("<H", head)[0]
        return head + self.recv_exact(total - 2)

    def read_bits(self, bit_addr, count):
        resp = self.raw(build_read(bit_addr, count))
        if len(resp) >= 9 and resp[8] == 0x0F:
            raise IOError("PLC err %d @0x%X" % (struct.unpack("<H", resp[10:12])[0], bit_addr))
        data = resp[22:]
        return [(data[i // 8] >> (i % 8)) & 1 for i in range(count)], data


def main():
    log = io.open(OUT, "w", encoding="utf-8")

    def w(s):
        log.write(s + "\n")

    c = Client()
    for start, span in ((0x2100000, 4096), (0x2101000, 4096), (0x2102000, 4096),
                        (0x2103000, 4096), (0x2104000, 4096)):
        try:
            bits, data = c.read_bits(start, span)
        except Exception as ex:
            w("scan 0x%X span %d FAILED: %s" % (start, span, ex))
            continue
        on = [start + i for i, b in enumerate(bits) if b]
        w("scan 0x%X span %d: %d bits set -> %s" % (start, span, len(on), [hex(x) for x in on][:80]))
        w("   first 32 data bytes: %s" % data[:32].hex(" "))

    # Also: what does a word read at 0x2100190 (aligned) return?
    for addr, bits in ((0x2100150, 16), (0x2100190, 16), (0x2100198, 16)):
        resp = c.raw(build_read(addr, bits))
        w("\nreq U%X bits=%d" % (addr, bits))
        w("  resp[0..] = %s" % resp.hex(" "))
        w("  data = %s" % resp[22:].hex(" "))

    log.close()
    print("done")


if __name__ == "__main__":
    main()
