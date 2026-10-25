# -*- coding: utf-8 -*-
"""One-off docs link fixer, pass 3 (2026-10-24 docs reorg). Dry-run by default.

Targets only the categories the statistics script reported as still broken:
  * `elsewhere`  - the named file exists somewhere under docs/ but not at the
                   linked path: rewrite relative to the real location.
  * `same-dir`   - target resolves but the file actually sits next to the source.
Skips markdown files by design (old dated records are append-only history and a
few of their links are deliberate placeholders such as `旧名.md`).
A second run must be a no-op: after the rewrite the target resolves, so the
same link is left alone.
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath('.')
DOCS = os.path.join(ROOT, 'docs')
PAT = re.compile(r'\]\(([^)]+)\)')

NAME_INDEX = {}


def build_index():
    for dirpath, dirs, files in os.walk(DOCS):
        for fn in files:
            if fn.endswith('.md'):
                rel = os.path.relpath(os.path.join(dirpath, fn), DOCS).replace(os.sep, '/')
                NAME_INDEX.setdefault(fn, []).append(rel)


def score(cand, src_rel, old_target):
    """Higher is better. The first path segment is the domain (code-changes,
    代码, 方案设计 ...); a candidate in the same domain as the SOURCE is a better
    guess when the old link carried no directory at all, a candidate sharing the
    old link's leading segment is a better guess when it did."""
    c_seg = cand.split('/')[:-1]
    s_seg = src_rel.split('/')[:-1]
    o_seg = [p for p in old_target.split('/')[:-1] if p not in ('', '.', '..')]
    s = 0
    if c_seg and s_seg and c_seg[0] == s_seg[0]:
        s += 4
    common = 0
    for a, b in zip(s_seg, c_seg):
        if a != b:
            break
        common += 1
    s += common * 3
    if o_seg and c_seg:
        tail = c_seg[-len(o_seg):]
        if len(c_seg) >= len(o_seg) and tail == o_seg:
            s += 5
        elif c_seg and o_seg and c_seg[-1] == o_seg[-1]:
            s += 3
    return s


def choose(cands, src_rel, old_target):
    return sorted(cands, key=lambda c: (-score(c, src_rel, old_target), len(c)))[0]


def rel_from(src_rel, dst_rel):
    depth = len([p for p in src_rel.split('/')[:-1] if p])
    return ('../' * depth) + dst_rel


def main():
    build_index()
    apply = '--apply' in sys.argv
    changes = []
    files_touched = set()
    for dirpath, dirs, files in os.walk(DOCS):
        for fn in files:
            if not fn.endswith('.md'):
                continue
            p = os.path.join(dirpath, fn)
            src_rel = os.path.relpath(p, DOCS).replace(os.sep, '/')
            txt = open(p, encoding='utf-8').read()

            def repl(m, src_rel=src_rel, dirpath=dirpath):
                raw = m.group(1)
                if not raw or raw.startswith(('http://', 'https://', 'file:///', 'mailto:', '#')):
                    return m.group(0)
                target, frag = (raw.split('#', 1) + [''])[:2] if '#' in raw else (raw, '')
                suffix = ('#' + frag) if frag else ''
                if not target or ' ' in target:
                    return m.group(0)
                abs_target = os.path.normpath(os.path.join(dirpath, target.replace('/', os.sep)))
                if os.path.exists(abs_target):
                    return m.group(0)
                base = os.path.basename(target)
                cands = NAME_INDEX.get(base, [])
                if not cands:
                    return m.group(0)
                dst = choose(cands, src_rel, target)
                new_target = rel_from(src_rel, dst)
                # prefer the flattest correct form: if the real file is next to
                # the source, the bare basename is enough.
                src_dir = os.path.dirname(src_rel)
                if os.path.dirname(dst) == src_dir:
                    new_target = base
                if new_target == target:
                    return m.group(0)
                changes.append((src_rel, raw, new_target + suffix))
                files_touched.add(src_rel)
                return '](' + new_target + suffix + ')'

            new_txt = PAT.sub(repl, txt)
            if new_txt != txt and apply:
                with open(p, 'w', encoding='utf-8', newline='') as f:
                    f.write(new_txt)

    out = os.path.join(ROOT, 'artifacts', 'logs', '20261024', 'docs-linkfix3.tsv')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('file\told\tnew\n')
        for r in changes:
            f.write('\t'.join(r) + '\n')
    print('apply=%s files=%d links=%d' % (apply, len(files_touched), len(changes)))
    for r in changes[:50]:
        print('  ', r[0], '|', r[1], '=>', r[2])


if __name__ == '__main__':
    main()
