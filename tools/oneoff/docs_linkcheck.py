# -*- coding: utf-8 -*-
import os, re, sys, json
sys.stdout.reconfigure(encoding='utf-8')
root = os.path.abspath('.')
docs = os.path.join(root, 'docs')
bad = []
total = 0
fileurl = 0
pat = re.compile(r'\[[^\]]*\]\(([^)]+)\)')
for dirpath, dirs, files in os.walk(docs):
    for fn in files:
        if not fn.endswith('.md'):
            continue
        p = os.path.join(dirpath, fn)
        txt = open(p, encoding='utf-8', errors='replace').read()
        for m in pat.finditer(txt):
            t = m.group(1).strip()
            if t.startswith(('http://', 'https://', 'mailto:', '#')):
                continue
            if t.startswith('file:///'):
                fileurl += 1
                continue
            t = t.split('#')[0]
            if not t:
                continue
            if ' ' in t and not t.startswith('<'):
                t = t.split(' ')[0]
            total += 1
            target = os.path.normpath(os.path.join(dirpath, t.replace('/', os.sep)))
            if not os.path.exists(target):
                rel = os.path.relpath(p, root).replace(os.sep, '/')
                bad.append([rel, t])
print('relative:', total, 'broken:', len(bad), 'fileurl:', fileurl)
out = os.path.join(root, 'artifacts', 'logs', '20261024', 'docs-linkcheck.tsv')
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('source\ttarget\n')
    for b in bad:
        f.write(b[0] + '\t' + b[1] + '\n')
print('report:', out)
for b in bad:
    if not b[0].startswith('docs/code-changes'):
        print('BROKEN', b[0], '->', b[1])
