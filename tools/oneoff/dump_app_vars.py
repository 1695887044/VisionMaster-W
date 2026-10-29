# -*- coding: utf-8 -*-
"""Dump EasyNet variables from the app's operations.db (read-only copy)."""
import io
import os
import shutil
import sqlite3
import tempfile

SRC = r"D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows\Data\operations.db"
OUT = r"D:\C#\VM\artifacts\logs\20261029\app_vars.txt"

tmp = os.path.join(tempfile.gettempdir(), "vm_ops_copy.db")
shutil.copy2(SRC, tmp)

con = sqlite3.connect(tmp)
con.row_factory = sqlite3.Row
cur = con.cursor()
tables = [r[0] for r in cur.execute("select name from sqlite_master where type='table'")]
log = io.open(OUT, "w", encoding="utf-8")


def w(s):
    log.write(s + "\n")


w("tables: %s" % tables)
for t in tables:
    cols = [r[1] for r in cur.execute("PRAGMA table_info(%s)" % t)]
    n = cur.execute("select count(*) from %s" % t).fetchone()[0]
    w("\n== %s (%d rows) cols=%s" % (t, n, cols))

for t in tables:
    cols = [r[1] for r in cur.execute("PRAGMA table_info(%s)" % t)]
    if not any("alarm" in str(c).lower() for c in cols):
        continue
    w("\n== full rows of %s ==" % t)
    for r in cur.execute("select * from %s" % t):
        w(str(dict(r)))

# search any table for 'Alarm3[79]'
for t in tables:
    cols = [r[1] for r in cur.execute("PRAGMA table_info(%s)" % t)]
    text_cols = [c for c in cols]
    where = " OR ".join("CAST(%s AS TEXT) LIKE '%%Alarm3[[]79]%%'" % c for c in text_cols)
    try:
        rows = cur.execute("select * from %s where %s" % (t, where)).fetchall()
    except Exception:
        continue
    if rows:
        w("\n== %s rows matching Alarm3[79] ==" % t)
        for r in rows:
            w(str(dict(r)))

con.close()
log.close()
print("done")
