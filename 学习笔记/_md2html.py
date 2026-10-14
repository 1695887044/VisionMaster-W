# -*- coding: utf-8 -*-
"""Convert the three handbooks (Markdown) into single-file HTML
with all figures embedded as base64 PNG. Mermaid diagrams are
pre-rendered PNGs (m01..m12) produced by Edge headless."""
import re, base64, html, shutil, pathlib

ROOT = pathlib.Path(r'D:\C#\VM\学习笔记')
TMP = pathlib.Path(r'D:\C#\VM\_mmd_tmp')

ROI = ROOT / '区域创建ROI插件手册'
CAL = ROOT / '卡尺测量插件手册'
MAT = ROOT / '模板匹配插件手册'
COL = ROOT / '颜色检测插件手册'
BLOB = ROOT / 'Blob缺陷检测插件手册'
CALXB = ROOT / '标定与坐标变换插件手册'
MAP = ROOT / '00-总选型地图'

for d in [ROI / 'images', CAL / 'images', MAT / 'images', COL / 'images', BLOB / 'images', CALXB / 'images', MAP / 'images']:
    d.mkdir(parents=True, exist_ok=True)

# 1. distribute pre-rendered mermaid pngs into each manual's images dir
#    (source may already have been cleaned up after the first run — skip then)
for i in range(1, 12):
    src = TMP / ('m%02d.png' % i)
    if src.exists():
        shutil.copy(src, ROI / 'images' / ('mermaid-%02d.png' % i))
if (TMP / 'm12.png').exists():
    shutil.copy(TMP / 'm12.png', CAL / 'images' / 'mermaid-01.png')
if (TMP / 'mmatch.png').exists():
    shutil.copy(TMP / 'mmatch.png', MAT / 'images' / 'mermaid-01.png')
if (TMP / 'map.png').exists():
    shutil.copy(TMP / 'map.png', MAP / 'images' / 'mermaid-01.png')

def data_uri(p):
    return 'data:image/png;base64,' + base64.b64encode(pathlib.Path(p).read_bytes()).decode()

def inline(s, images_dir):
    s = html.escape(s, quote=False)
    def img_rep(m):
        alt, path = m.group(1), m.group(2)
        p = pathlib.Path(images_dir) / pathlib.Path(path).name
        return '<img class="fig" src="%s" alt="%s">' % (data_uri(p), alt)
    s = re.sub(r'!\[([^\]]*)\]\(([^)]+?)\)', img_rep, s)
    s = re.sub(r'`([^`]+)`', r'<code>\1</code>', s)
    s = re.sub(r'\*\*([^*]+?)\*\*', r'<strong>\1</strong>', s)
    s = re.sub(r'(?<!\*)\*([^*\n]+?)\*(?!\*)', r'<em>\1</em>', s)
    s = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', r'<a href="\2">\1</a>', s)
    return s

def build_table(rows, images_dir):
    parsed = []
    for r in rows:
        t = r.strip()
        if re.match(r'^\|[\s:|\-]+\|$', t):
            continue
        parsed.append([c.strip() for c in t.strip('|').split('|')])
    if not parsed:
        return ''
    out = ['<table>', '<thead><tr>']
    for c in parsed[0]:
        out.append('<th>%s</th>' % inline(c, images_dir))
    out.append('</tr></thead><tbody>')
    for row in parsed[1:]:
        out.append('<tr>')
        for c in row:
            out.append('<td>%s</td>' % inline(c, images_dir))
        out.append('</tr>')
    out.append('</tbody></table>')
    return ''.join(out)

def build_list(items, images_dir):
    ordered = items and items[0][1] != '-'
    tag = 'ol' if ordered else 'ul'
    out = ['<%s>' % tag]
    for ind, mk, text in items:
        mchk = re.match(r'^\[ \]\s*(.*)$', text)
        if mchk:
            text = '☐ ' + mchk.group(1)
        if tag == 'ul' and mk != '-':
            text = mk + ' ' + text
        cls = ' class="sub"' if ind >= 2 else ''
        out.append('<li%s>%s</li>' % (cls, inline(text, images_dir)))
    out.append('</%s>' % tag)
    return ''.join(out)

def process(lines, images_dir, mermaid_pngs, mcounter, toc):
    out = []
    i, n = 0, len(lines)
    while i < n:
        line = lines[i]
        if not line.strip():
            i += 1
            continue
        m = re.match(r'^```(\w*)\s*$', line)
        if m:
            lang = m.group(1)
            buf = []
            i += 1
            while i < n and not re.match(r'^```\s*$', lines[i]):
                buf.append(lines[i])
                i += 1
            i += 1
            if lang == 'mermaid':
                png = mermaid_pngs[mcounter[0]]
                mcounter[0] += 1
                out.append('<img class="fig" src="%s" alt="流程图">' % data_uri(png))
            else:
                out.append('<pre class="code"><code>%s</code></pre>' % html.escape('\n'.join(buf), quote=False))
            continue
        if line.startswith('>'):
            q = []
            while i < n and lines[i].startswith('>'):
                q.append(re.sub(r'^> ?', '', lines[i]))
                i += 1
            out.append('<blockquote>' + process(q, images_dir, mermaid_pngs, mcounter, toc) + '</blockquote>')
            continue
        hm = re.match(r'^(#{1,4})\s+(.*)$', line)
        if hm:
            lvl = len(hm.group(1))
            hid = 'sec-%d' % (len(toc) + 1)
            toc.append((lvl, inline(hm.group(2), images_dir), hid))
            out.append('<h%d id="%s">%s</h%d>' % (lvl, hid, inline(hm.group(2), images_dir), lvl))
            i += 1
            continue
        if line.startswith('|'):
            rows = []
            while i < n and lines[i].startswith('|'):
                rows.append(lines[i])
                i += 1
            out.append(build_table(rows, images_dir))
            continue
        lm = re.match(r'^(\s*)(-|\d+\.)\s+(.*)$', line)
        if lm:
            items = []
            while i < n:
                lm2 = re.match(r'^(\s*)(-|\d+\.)\s+(.*)$', lines[i])
                if not lm2:
                    break
                items.append((len(lm2.group(1)), lm2.group(2), lm2.group(3)))
                i += 1
            out.append(build_list(items, images_dir))
            continue
        if re.match(r'^---+\s*$', line):
            out.append('<hr>')
            i += 1
            continue
        para = [line]
        i += 1
        while i < n and lines[i].strip() and not re.match(r'^(#|```|\||>|-|\d+\.|---)', lines[i]):
            para.append(lines[i])
            i += 1
        out.append('<p>%s</p>' % '<br>'.join(inline(x, images_dir) for x in para))
    return '\n'.join(out)

CSS = '''
*{box-sizing:border-box}
body{font-family:"Microsoft YaHei","PingFang SC",sans-serif;margin:0;color:#24292f;background:#FBFBF9;padding-left:292px}
.doc{max-width:960px;margin:0 auto;padding:30px 34px 70px;line-height:1.75}
h1{font-size:25px;border-bottom:2px solid #2b6cb0;padding-bottom:8px;color:#1a365d}
h2{font-size:20px;color:#1a365d;border-bottom:1px solid #dbe4ee;padding-bottom:6px;margin-top:34px}
h3{font-size:16.5px;color:#2c5282;margin-top:26px}
h4{font-size:15px;color:#2d3748}
table{border-collapse:collapse;width:100%;margin:10px 0;font-size:13.5px}
th,td{border:1px solid #d7dee6;padding:6px 10px;text-align:left;vertical-align:top}
th{background:#eef2f6}
pre.code{background:#f6f8fa;border:1px solid #e2e8f0;border-radius:4px;padding:10px 12px;overflow-x:auto;font-family:Consolas,monospace;font-size:12.5px;line-height:1.5}
code{background:#f2f4f7;border-radius:3px;padding:1px 4px;font-family:Consolas,monospace;font-size:12.5px}
pre.code code{background:none;padding:0}
blockquote{border-left:4px solid #3b82f6;background:#f0f7ff;margin:10px 0;padding:8px 14px;color:#374151;border-radius:0 4px 4px 0}
blockquote p{margin:4px 0}
img.fig{max-width:100%;border:1px solid #e2e8f0;border-radius:4px;margin:8px 0;display:block;cursor:zoom-in}
li{margin:3px 0}
li.sub{margin-left:26px;list-style:none}
hr{border:none;border-top:1px solid #dbe4ee;margin:22px 0}
p{margin:8px 0}
a{color:#2b6cb0}
nav#toc{position:fixed;left:0;top:0;bottom:0;width:264px;overflow-y:auto;background:#0F1B2D;padding:18px 12px 46px;font-size:12.5px;z-index:10}
nav#toc .tt{color:#7FB3E8;font-weight:bold;font-size:13px;letter-spacing:2px;margin:0 8px 10px}
nav#toc a{display:block;color:#C9D6E4;text-decoration:none;padding:3px 8px;border-radius:3px;line-height:1.45}
nav#toc a.t1{color:#8FB8E8;font-weight:bold;margin-top:10px}
nav#toc a.t2{margin-left:12px;color:#AFC3D6}
nav#toc a:hover{background:#1B2A40;color:#fff}
#progress{position:fixed;top:0;left:0;height:3px;background:#F59E0B;width:0;z-index:99}
#toTop{position:fixed;right:24px;bottom:28px;width:42px;height:42px;border-radius:50%;background:#1A365D;color:#fff;border:none;cursor:pointer;display:none;font-size:17px;box-shadow:0 2px 8px rgba(15,27,45,.35)}
#lightbox{position:fixed;inset:0;background:rgba(10,15,25,.86);display:none;align-items:center;justify-content:center;z-index:98;cursor:zoom-out}
#lightbox img{max-width:92vw;max-height:92vh;border-radius:4px;box-shadow:0 8px 40px rgba(0,0,0,.5)}
@media (max-width:1150px){nav#toc{display:none}body{padding-left:0}}
@media print{nav#toc,#toTop,#progress,#lightbox{display:none}body{padding-left:0}}
'''

def wrap(title, body, toc):
    nav = ['<nav id="toc"><div class="tt">目 录</div>']
    for lvl, txt, hid in toc:
        if lvl <= 2:
            nav.append('<a class="t%d" href="#%s">%s</a>' % (lvl, hid, txt))
    nav.append('</nav>')
    js = ('<script>'
          'var pb=document.getElementById("progress"),tt=document.getElementById("toTop");'
          'window.addEventListener("scroll",function(){var h=document.documentElement;'
          'var sc=h.scrollTop||document.body.scrollTop;var hh=h.scrollHeight-h.clientHeight;'
          'pb.style.width=(hh>0?(sc/hh*100):0)+"%";tt.style.display=sc>600?"block":"none";});'
          'tt.addEventListener("click",function(){window.scrollTo({top:0,behavior:"smooth"});});'
          'var lb=document.getElementById("lightbox"),lbi=lb.querySelector("img");'
          'document.querySelectorAll("img.fig").forEach(function(im){'
          'im.addEventListener("click",function(){lbi.src=im.src;lb.style.display="flex";});});'
          'lb.addEventListener("click",function(){lb.style.display="none";});'
          '</script>')
    return ('<!DOCTYPE html>\n<html lang="zh-CN"><head><meta charset="utf-8">'
            '<meta name="viewport" content="width=device-width, initial-scale=1">'
            '<title>%s</title><style>%s</style></head><body>'
            '<div id="progress"></div>%s<main class="doc">%s</main>'
            '<button id="toTop">↑</button><div id="lightbox"><img alt=""></div>%s'
            '</body></html>' % (html.escape(title), CSS, ''.join(nav), body, js))

jobs = [
    (ROI / '区域创建ROI插件手册.md', ROI / 'images', 11),
    (CAL / '卡尺测量插件手册.md', CAL / 'images', 1),
    (MAT / '模板匹配插件手册.md', MAT / 'images', 1),
    (COL / '颜色检测插件手册.md', COL / 'images', 1),
    (BLOB / 'Blob缺陷检测插件手册.md', BLOB / 'images', 2),
    (CALXB / '标定与坐标变换插件手册.md', CALXB / 'images', 2),
    (MAP / '00-总选型地图.md', MAP / 'images', 1),
]

for md, imgdir, mcount in jobs:
    text = md.read_text(encoding='utf-8')
    mermaid_pngs = [imgdir / ('mermaid-%02d.png' % (k + 1)) for k in range(mcount)]
    mcounter = [0]
    toc = []
    body = process(text.split('\n'), imgdir, mermaid_pngs, mcounter, toc)
    assert mcounter[0] == mcount, ('mermaid count mismatch', str(md), mcounter[0], mcount)
    tm = re.match(r'^#\s+(.+)$', text, re.M)
    title = tm.group(1) if tm else md.stem
    outp = md.with_suffix('.html')
    outp.write_text(wrap(title, body, toc), encoding='utf-8')
    print('OK %s | %d bytes | %d imgs | %d toc' % (outp.name, outp.stat().st_size, body.count('<img class="fig"'), len(toc)))
print('DONE')
