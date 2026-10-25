# -*- coding: utf-8 -*-
"""One-off docs link fixer (2026-10-24 docs reorg).

English output only; paths are handled as str and printed via UTF-8 stdout.
Rules (ordered, first match wins). Every rule is a *literal* rewrite so a
second run is a no-op.
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath('.')
DOCS = os.path.join(ROOT, 'docs')


def code_dir(domain, plugin):
    return '代码/' + domain + '/' + plugin


DOMAIN_OF_PLUGIN = {
    '图像采集': '采图', '图像预处理': '采图', '图像赋值': '采图', '图像脚本': '采图',
    '创建ROI': '视觉算子', '卡尺测量': '视觉算子', '模板匹配': '视觉算子',
    '颜色检查': '视觉算子', '缺陷检测': '视觉算子', '胶路检测': '视觉算子',
    '码读取': '视觉算子', 'OCR': '视觉算子', '目标检测': '视觉算子',
    '深度学习': '视觉算子', '几何工具': '视觉算子',
    '标定插件': '标定变换', '坐标变换插件': '标定变换',
    '运动控制': '运动通讯',
    '结果显示': '结果与报表', '结果上报': '结果与报表', 'Excel报表': '结果与报表', 'C#脚本': '结果与报表',
    '产线仿真3D': '产线仿真',
}

CODE_FILES = {}  # (domain, plugin, filename) -> True


def index_code_files():
    base = os.path.join(DOCS, '代码')
    for dom in os.listdir(base):
        d = os.path.join(base, dom)
        if not os.path.isdir(d):
            continue
        for plug in os.listdir(d):
            pd = os.path.join(d, plug)
            if not os.path.isdir(pd):
                continue
            for fn in os.listdir(pd):
                if fn.endswith('.md'):
                    CODE_FILES[(dom, plug, fn)] = True


def find_code_file(name):
    """Locate a filename that used to live in a flat plugin dir."""
    hits = [k for k in CODE_FILES if k[2] == name]
    if len(hits) == 1:
        return hits[0]
    return None


LINK = re.compile(r'\]\(([^)]+)\)')


def fix_source(source_abs, text):
    """Return (new_text, changes[])."""
    changes = []
    src_rel = os.path.relpath(source_abs, DOCS).replace(os.sep, '/')
    src_dir = os.path.dirname(src_rel)          # relative to docs/
    depth = len([p for p in src_dir.split('/') if p])  # 0 for docs root

    def repl(m):
        raw = m.group(1)
        target = raw
        suffix = ''
        if '#' in target:
            target, suffix = target.split('#', 1)
            suffix = '#' + suffix
        if not target or target.startswith(('http://', 'https://', 'file:///', 'mailto:')):
            return m.group(0)

        # --- rule 1: a bare file name that now sits in code/<domain>/<plugin>/ ---
        new = target
        if '/' not in target:
            hit = find_code_file(target)
            if hit:
                want = '../../' + code_dir(hit[0], hit[1]) + '/' + target
                if want != target:
                    new = want
            else:
                new = target

        # --- rule 2: ../code-changes/... -> ../../code-changes/... (2nd/3rd level) ---
        if new.startswith('../code-changes/') and depth >= 2:
            new = '../' + new

        # --- rule 3: ../<known-plugin>/rest -> sibling path inside code/ ---
        if new.startswith('../') and not new.startswith('../../'):
            rest = new[3:]
            head = rest.split('/')[0]
            dom = DOMAIN_OF_PLUGIN.get(head)
            if dom:
                if depth >= 3:
                    new = rest
                else:
                    new = code_dir(dom, head) + '/' + rest.split('/', 1)[1] if '/' in rest else code_dir(dom, head)
        # --- rule 4: code/<domain>/README.md -> ../README.md for domain READMEs ---
        if src_dir.count('/') == 1 and src_dir.startswith('代码/') and '/' in new and new.endswith('/README.md'):
            new = '../README.md'

        if new != target:
            changes.append((src_rel, raw, new + suffix if not suffix else new + suffix))
            return '](' + new + suffix + ')'
        return m.group(0)

    new_text = LINK.sub(repl, text)
    return new_text, changes


def main():
    index_code_files()
    apply = '--apply' in sys.argv
    total = 0
    files = 0
    report = []
    for dirpath, dirs, names in os.walk(DOCS):
        for fn in names:
            if not fn.endswith('.md'):
                continue
            p = os.path.join(dirpath, fn)
            txt = open(p, encoding='utf-8').read()
            new, ch = fix_source(p, txt)
            if ch:
                files += 1
                total += len(ch)
                report.extend(ch)
                if apply:
                    with open(p, 'w', encoding='utf-8', newline='') as f:
                        f.write(new)
    out = os.path.join(ROOT, 'artifacts', 'logs', '20261024', 'docs-linkfix.tsv')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('file\told\tnew\n')
        for r in report:
            f.write('\t'.join(r) + '\n')
    print('apply=%s files=%d links=%d report=%s' % (apply, files, total, out))
    for r in report[:40]:
        print('  ', r[0], '|', r[1], '=>', r[2])


if __name__ == '__main__':
    main()
