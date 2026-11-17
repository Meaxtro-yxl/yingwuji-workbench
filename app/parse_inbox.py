# 解析 收件箱.md 的采集结果 → 打印分级结果（JSON 存到 app/_inbox.json）
# 阈值读 配置.js 的 grading；平台名不写死，表头写成「# 平台采集结果 - 关键词」即可
import re, json, sys, os
sys.stdout.reconfigure(encoding="utf-8")

CFG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '配置.js')


def strip_js_comments(s):
    """配置.js 允许写 // 注释，JSON 不认。只剥字符串外的注释，URL 里的 // 不动。"""
    out, i, n = [], 0, len(s)
    while i < n:
        c = s[i]
        if c == '"' or c == "'":
            q = c
            out.append(c); i += 1
            while i < n:
                ch = s[i]
                out.append(ch); i += 1
                if ch == '\\' and i < n:
                    out.append(s[i]); i += 1
                elif ch == q:
                    break
            continue
        if c == '/' and i + 1 < n and s[i + 1] == '/':
            while i < n and s[i] != '\n': i += 1
            continue
        if c == '/' and i + 1 < n and s[i + 1] == '*':
            i += 2
            while i + 1 < n and not (s[i] == '*' and s[i + 1] == '/'): i += 1
            i += 2
            continue
        out.append(c); i += 1
    return ''.join(out)


def load_cfg():
    try:
        raw = strip_js_comments(open(CFG_PATH, encoding='utf-8-sig').read())
        return json.loads(raw[raw.index('{'):raw.rindex('}') + 1])
    except Exception:
        return {}


G = load_cfg().get('grading', {})
A_FANS, A_MULT = G.get('aFans', 500), G.get('aMult', 30)
B_FANS, B_MULT = G.get('bFans', 2000), G.get('bMult', 10)
C_MULT = G.get('cMult', 5)
DROP_FANS = G.get('dropFans', 10000)
MIN_FANS = G.get('minFans', 10)


def num(s):
    s = (s or '').strip().replace(',', '')
    m = re.match(r'([\d.]+)\s*(万|w|W)?', s)
    if not m: return None
    v = float(m.group(1))
    return int(v * 10000) if m.group(2) else int(v)


def grade(fa, mu):
    if fa is None: return '无粉丝数'
    if fa > DROP_FANS: return 'X'
    if fa < MIN_FANS: return '粉丝<10'
    if fa <= A_FANS and mu >= A_MULT: return 'A'
    if fa <= B_FANS and mu >= B_MULT: return 'B'
    if fa <= B_FANS and mu >= C_MULT: return 'C'
    return '参考'


src = open(sys.argv[1], encoding='utf-8').read()
section_pf, section_kw = None, None
items, deep = [], []
for block in re.split(r'\n(?=# |### |#### )', src):
    d = re.match(r'#### 评论区深挖\s*\|\s*(.+?)\s*\|\s*(.+)', block)
    if d:
        deep.append(dict(pf=d.group(1).strip(), account=d.group(2).strip(), raw=block.strip()[:3000])); continue
    h = re.match(r'# (.+?)采集结果\s*-\s*(.+)', block)
    if h:
        section_pf, section_kw = h.group(1).strip(), h.group(2).strip(); continue
    m = re.match(r'### (.+?)\s*\|\s*(.+?)\s*\|\s*(.+)', block)
    if not m: continue
    f = lambda k: (re.search(k + r'：([^\n]*?)(?:\s{2,}|\n|$)', block) or [None, ''])[1].strip()
    asks = re.findall(r'^\d+\.\s*(.+)$', block, re.M)
    it = dict(pf=m.group(1).strip(), account=m.group(2).strip(), fans_raw=m.group(3).strip(),
              kw=f('搜索词') or section_kw, tier=f('档位'), title=f('标题'), url=f('链接'),
              likes=num(f('赞')), comments=num(f('评')), saves=num(f('藏')), plays=num(f('播放')),
              date=f('发布'), cover=f('封面文字'), dur=f('时长'),
              asks=[a for a in asks if a.strip() != '未知'], raw=block[:1500])
    it['fans'] = num(it['fans_raw'])
    inter = sum(x or 0 for x in (it['likes'], it['comments'], it['saves']))
    it['inter'] = inter
    it['mult'] = inter / it['fans'] if it['fans'] else None
    it['grade'] = grade(it['fans'], it['mult'])
    items.append(it)

json.dump(dict(items=items, deep=deep), open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
from collections import Counter
print(Counter(i['grade'] for i in items), '| 平台', Counter(i['pf'] for i in items), '| 评论区深挖', len(deep))
for l in re.findall(r'^源状态：.*$', src, re.M): print(l)
for i in sorted([i for i in items if i['fans'] is not None], key=lambda i: -(i['mult'] or 0)):
    miss = '缺评/藏' if i['comments'] is None or (i['saves'] is None and '无此数据' not in i['raw']) else ''
    print(f"{i['grade']:4} ×{i['mult']:.1f} {i['pf']} {i['account']} 粉{i['fans']} 赞{i['likes']} 评{i['comments']} 藏{i['saves']} {miss} [{i['tier']}] {i['kw']} | {i['title'][:40]} | {i['date']} | {i['url'][:70]}")
