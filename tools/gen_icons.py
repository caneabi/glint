import math, os, re, sys
import xml.etree.ElementTree as ET
src, out = sys.argv[1], sys.argv[2]

def arc(x1, y1, rx, ry, phi, fa, fs, x2, y2):
    if rx == 0 or ry == 0 or (x1 == x2 and y1 == y2):
        return [('L', x2, y2)]
    rx, ry = abs(rx), abs(ry)
    p = math.radians(phi); cp, sp = math.cos(p), math.sin(p)
    dx, dy = (x1 - x2) / 2, (y1 - y2) / 2
    x_, y_ = cp * dx + sp * dy, -sp * dx + cp * dy
    l = x_ * x_ / (rx * rx) + y_ * y_ / (ry * ry)
    if l > 1:
        s = math.sqrt(l); rx *= s; ry *= s
    n = rx * rx * ry * ry - rx * rx * y_ * y_ - ry * ry * x_ * x_
    d = rx * rx * y_ * y_ + ry * ry * x_ * x_
    c = math.sqrt(max(0, n / d)) * (-1 if fa == fs else 1)
    cx_, cy_ = c * rx * y_ / ry, -c * ry * x_ / rx
    cx, cy = cp * cx_ - sp * cy_ + (x1 + x2) / 2, sp * cx_ + cp * cy_ + (y1 + y2) / 2
    a1 = math.atan2((y_ - cy_) / ry, (x_ - cx_) / rx)
    a2 = math.atan2((-y_ - cy_) / ry, (-x_ - cx_) / rx)
    da = a2 - a1
    if fs and da < 0: da += 2 * math.pi
    if not fs and da > 0: da -= 2 * math.pi
    segs = max(1, math.ceil(abs(da) / (math.pi / 2) - 1e-9))
    res = []
    t = 4 / 3 * math.tan(da / segs / 4)
    for i in range(segs):
        s, e = a1 + i * da / segs, a1 + (i + 1) * da / segs
        pts = [(math.cos(s) - t * math.sin(s), math.sin(s) + t * math.cos(s)),
               (math.cos(e) + t * math.sin(e), math.sin(e) - t * math.cos(e)),
               (math.cos(e), math.sin(e))]
        flat = []
        for ux, uy in pts:
            xx, yy = ux * rx, uy * ry
            flat += [cp * xx - sp * yy + cx, sp * xx + cp * yy + cy]
        res.append(('C', *flat))
    return res

def path(d):
    toks = re.findall(r'[MmLlHhVvCcSsQqTtAaZz]|-?(?:\d*\.\d+|\d+)(?:e-?\d+)?', d)
    i, out, x, y, sx, sy, cmd, lc = 0, [], 0.0, 0.0, 0.0, 0.0, None, None
    def isnum(j): return j < len(toks) and not toks[j].isalpha()
    def num():
        nonlocal i
        v = float(toks[i]); i += 1; return v
    def flag():
        nonlocal i
        t = toks[i]
        if len(t) > 1 and t[0] in '01' and t != '0' and t != '1' and not t.startswith(('0.', '1.')) or (len(t) > 1 and t[0] in '01' and t[1] == '.' ):
            toks[i] = t[1:]
            return float(t[0])
        i += 1; return float(t)
    while i < len(toks):
        if toks[i].isalpha(): cmd = toks[i]; i += 1
        c, rel = cmd.upper(), cmd.islower()
        if c == 'Z':
            out.append(('Z',)); x, y = sx, sy; lc = None; continue
        ox, oy = (x, y) if rel else (0.0, 0.0)
        if c == 'M':
            x, y = num() + ox, num() + oy; sx, sy = x, y; out.append(('M', x, y)); cmd = 'l' if rel else 'L'
        elif c == 'L': x, y = num() + ox, num() + oy; out.append(('L', x, y))
        elif c == 'H': x = num() + ox; out.append(('L', x, y))
        elif c == 'V': y = num() + oy; out.append(('L', x, y))
        elif c == 'C':
            a, b, e, f, g, h = num()+ox, num()+oy, num()+ox, num()+oy, num()+ox, num()+oy
            out.append(('C', a, b, e, f, g, h)); lc = (e, f); x, y = g, h; continue
        elif c == 'S':
            a, b = (2*x-lc[0], 2*y-lc[1]) if lc else (x, y)
            e, f, g, h = num()+ox, num()+oy, num()+ox, num()+oy
            out.append(('C', a, b, e, f, g, h)); lc = (e, f); x, y = g, h; continue
        elif c == 'Q':
            a, b, g, h = num()+ox, num()+oy, num()+ox, num()+oy
            out.append(('C', x+2/3*(a-x), y+2/3*(b-y), g+2/3*(a-g), h+2/3*(b-h), g, h)); x, y = g, h
        elif c == 'A':
            rx, ry, phi = num(), num(), num(); fa = flag(); fs = flag()
            g, h = num()+ox, num()+oy
            out += arc(x, y, rx, ry, phi, fa, fs, g, h); x, y = g, h
        else: raise ValueError(cmd)
        lc = None
    return out

def shape(el):
    t = el.tag.split('}')[-1]; a = el.attrib; f = lambda k, d=0: float(a.get(k, d))
    if t == 'path': return path(a['d'])
    if t == 'line': return [('M', f('x1'), f('y1')), ('L', f('x2'), f('y2'))]
    if t in ('polyline', 'polygon'):
        v = list(map(float, re.findall(r'-?[\d.]+', a['points'])))
        r = [('M', v[0], v[1])] + [('L', v[i], v[i+1]) for i in range(2, len(v), 2)]
        return r + ([('Z',)] if t == 'polygon' else [])
    if t in ('circle', 'ellipse'):
        rx, ry = (f('r'), f('r')) if t == 'circle' else (f('rx'), f('ry'))
        cx, cy = f('cx'), f('cy')
        return path(f'M{cx-rx} {cy}A{rx} {ry} 0 1 1 {cx+rx} {cy}A{rx} {ry} 0 1 1 {cx-rx} {cy}Z')
    if t == 'rect':
        x, y, w, h, r = f('x'), f('y'), f('width'), f('height'), f('rx')
        if r == 0: return [('M',x,y),('L',x+w,y),('L',x+w,y+h),('L',x,y+h),('Z',)]
        return path(f'M{x+r} {y}H{x+w-r}A{r} {r} 0 0 1 {x+w} {y+r}V{y+h-r}A{r} {r} 0 0 1 {x+w-r} {y+h}H{x+r}A{r} {r} 0 0 1 {x} {y+h-r}V{y+r}A{r} {r} 0 0 1 {x+r} {y}Z')
    return []

rows = []
for fn in sorted(os.listdir(src)):
    if not fn.endswith('.svg'): continue
    root = ET.parse(os.path.join(src, fn)).getroot()
    cmds = []
    for el in root: cmds += shape(el)
    s = ' '.join(c[0] + ' ' + ' '.join(f'{v:.3f}'.rstrip('0').rstrip('.') for v in c[1:]) for c in cmds)
    rows.append(f'            {{"{fn[:-4]}", "{s}"}},')
with open(out, 'w', encoding='utf-8') as fh:
    fh.write('// Erzeugt aus Lucide-Icons (lucide-static 0.468.0, https://lucide.dev), ISC-Lizenz,\n// Copyright (c) Lucide Contributors; Teile (c) Cole Bemis (Feather, MIT).\nusing System.Collections.Generic;\n\nnamespace Glint\n{\n    static class IconData\n    {\n        public static readonly Dictionary<string, string> Paths = new Dictionary<string, string> {\n')
    fh.write('\n'.join(rows))
    fh.write('\n        };\n    }\n}\n')
print(len(rows), 'Icons')
