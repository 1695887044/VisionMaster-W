# -*- coding: utf-8 -*-
"""One-off link statistics for docs/ (2026-10-24 reorg). English output only."""
import collections
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath('.')
DOCS = os.path.join(ROOT, 'docs')
pat = re.compile(r'\]\(([^)]+)\)')

allmd = set()
for dirpath, dirs, files in os.walk(DOCS):
    for fn in files:
        if fn.endswith('.md'):
            allmd.add(fn)

stats = collections.Counter()
detail = collections.defaultdict(list)
for dirpath, dirs, files in os.walk(DOCS):
    for fn in files:
        if not fn.endswith('.md'):
            continue
        p = os.path.join(dirpath, fn)
        rel = os.path.relpath(p, ROOT).replace(os.sep, '/')
        txt = open(p, encoding='utf-8', errors='replace').read()
        for m in pat.finditer(txt):
            t = m.group(1).strip()
            if t.startswith(('http://', 'https://', 'mailto:', '#')):
                continue
            if t.startswith('file:///'):
                stats['old-machine-fileurl'] += 1
                continue
            t2 = t.split('#')[0]
            if not t2:
                continue
            if ' ' in t2:
                stats['space-scraped'] += 1
                detail['space'].append((rel, t))
                continue
            if '#' in t2:
                stats['has-hash'] += 1
                detail['hash'].append((rel, t))
                continue
            target = os.path.normpath(os.path.join(dirpath, t2.replace('/', os.sep)))
            if os.path.exists(target):
                stats['ok'] += 1
                continue
            b = os.path.basename(t2)
            if os.path.exists(os.path.join(dirpath, b)):
                stats['relative-same-dir'] += 1
                detail['sameDir'].append((rel, t))
            elif b in allmd:
                stats['elsewhere'] += 1
                detail['elsewhere'].append((rel, t))
            else:
                stats['dead'] += 1
                detail['dead'].append((rel, t))

for k, v in stats.most_common():
    print(k, v)
print('--- dead (file does not exist anywhere) sample ---')
for x in detail['dead'][:30]:
    print(' ', x[0], '->', x[1])
print('--- elsewhere (same name elsewhere) sample ---')
for x in detail['elsewhere'][:20]:
    print(' ', x[0], '->', x[1])
print('--- relative-same-dir sample ---')
for x in detail['sameDir'][:10]:
    print(' ', x[0], '->', x[1])
