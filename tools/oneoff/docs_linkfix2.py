# -*- coding: utf-8 -*-
"""One-off docs link fixer (2026-10-24 docs reorg). Pass 2, dry-run by default.

English output only. A second run must be a no-op: every rewrite is literal and
idempotent (the fixed target resolves through the same index again).
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath('.')
DOCS = os.path.join(ROOT, 'docs')

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

CODE_DOMAINS = sorted(set(DOMAIN_OF_PLUGIN.values()))
CODE_FILES = set()
CODE_FILE_BY_NAME = {}

# top-level docs dirs that must never be treated as plugin dir names
DOC_TOP = {
    '代码', 'code-changes', 'meetings', 'performance', '方案设计', '问题汇总', '数据记录',
    '通信协议矩阵', '实施计划', '架构文档', '工程纪律', '归档', '基础设施', '工具集',
    'UI设计基线', 'UI样式参考', 'SCADA运行态Web客户端', 'AI协作', 'AI操作计划',
}


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
                    CODE_FILES.add((dom, plug, fn))
                    CODE_FILE_BY_NAME.setdefault(fn, []).append((dom, plug))


LINK = re.compile(r'\]\(([^)]+)\)')


def normalize(target, src_depth):
    """Resolve a link target to a repo-relative-ish path under docs/."""
    t = target.replace('\\', '/')
    parts = [p for p in t.split('/') if p not in ('', '.')]
    out = []
    for p in parts:
        if p == '..':
            if out:
                out.pop()
            continue
        out.append(p)
    # parts are relative to the source directory; walk up src_depth times
    out = out[src_depth:] if src_depth <= len(out) else []
    return '/'.join(out)


def resolve_docs_path(normalized):
    """Return the true docs-relative path if the file exists, else None."""
    p = os.path.join(DOCS, normalized.replace('/', os.sep))
    return normalized if os.path.exists(p) else None


def fix_target(src_rel, target):
    src_dir = os.path.dirname(src_rel)
    src_depth = len([p for p in src_dir.split('/') if p])
    orig = target
    norm = normalize(target, src_depth)

    if resolve_docs_path(norm):
        return orig  # already correct

    new = None
    base = norm.rsplit('/', 1)[-1]

    # 1) a bare file name that now lives in 代码/<domain>/<plugin>/
    hits = CODE_FILE_BY_NAME.get(base, [])
    if len(hits) == 1:
        new = '代码/' + hits[0][0] + '/' + hits[0][1] + '/' + base

    # 2) <domain>/README.md -> docs/README.md (domain READMEs were folded up)
    if new is None and norm in [d + '/README.md' for d in CODE_DOMAINS]:
        new = 'README.md'

    # 3) old plugin-relative: <plugin>/<file> -> 代码/<domain>/<plugin>/<file>
    if new is None:
        head = norm.split('/', 1)[0]
        dom = DOMAIN_OF_PLUGIN.get(head)
        if dom and head not in DOC_TOP:
            new = '代码/' + dom + '/' + norm

    # 4) wrong plugin segment under 代码/<domain>/<plugin>/<file>
    if new is None and norm.startswith('代码/'):
        parts = norm.split('/')
        rest = parts[-1]
        hits2 = CODE_FILE_BY_NAME.get(rest, [])
        if len(hits2) == 1:
            new = '代码/' + hits2[0][0] + '/' + hits2[0][1] + '/' + rest

    if new is None:
        return orig
    return ('../' * src_depth) + new


def main():
    index_code_files()
    apply = '--apply' in sys.argv
    changes = []
    files = 0
    for dirpath, dirs, names in os.walk(DOCS):
        for fn in names:
            if not fn.endswith('.md'):
                continue
            p = os.path.join(dirpath, fn)
            src_rel = os.path.relpath(p, DOCS).replace(os.sep, '/')
            txt = open(p, encoding='utf-8').read()

            def repl(m, src_rel=src_rel):
                raw = m.group(1)
                if not raw or raw.startswith(('http://', 'https://', 'file:///', 'mailto:', '#')):
                    return m.group(0)
                if '#' in raw:
                    target, frag = raw.split('#', 1)
                    suffix = '#' + frag
                else:
                    target, suffix = raw, ''
                if not target:
                    return m.group(0)
                newt = fix_target(src_rel, target)
                if newt != target:
                    changes.append((src_rel, raw, newt + suffix))
                    return '](' + newt + suffix + ')'
                return m.group(0)

            new_txt = LINK.sub(repl, txt)
            if new_txt != txt:
                files += 1
                if apply:
                    with open(p, 'w', encoding='utf-8', newline='') as f:
                        f.write(new_txt)

    out = os.path.join(ROOT, 'artifacts', 'logs', '20261024', 'docs-linkfix2.tsv')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('file\told\tnew\n')
        for r in changes:
            f.write('\t'.join(r) + '\n')
    print('apply=%s files=%d links=%d' % (apply, files, len(changes)))
    for r in changes[:60]:
        print('  ', r[0], '|', r[1], '=>', r[2])


if __name__ == '__main__':
    main()
