"""App screen mockups: each series-1 effect as the hero of a phone screen.

Usage:
  python mockups.py stills <raw dir> <out dir> [names]          # 1080x2340 JPEGs
  python mockups.py videos <sequence dir> <out dir> [names]     # 1080x2340 10 s loops
  python mockups.py overview <mockups dir>                      # 00-overview.jpg and .mp4

Same inputs as posters.py for posters.json: the 1600x2000 renders for stills,
the 1080x1350 loop sequences for videos. The screen is 360x780 pt at 3x. The
effect fills the top 450 pt (1080x1350 px, the loop frames' own size) and fades
into the app's surface; the UI is HTML in each effect's palette, headings in
its poster face, text in Inter.
"""
import glob, json, os, random, subprocess, sys, tempfile
from multiprocessing import Pool
from PIL import Image
import posters as P

W, H, SCALE = 360, 780, 3
SHOTS = os.path.join(P.HERE, 'posters.json')

# ---------------------------------------------------------------- icons (24px, stroke)

def icon(name, size=24, stroke=2, fill='none'):
    paths = {
        'play': '<path d="M8 5v14l11-7z" fill="currentColor" stroke="none"/>',
        'pause': '<path d="M7 5h3v14H7zM14 5h3v14h-3z" fill="currentColor" stroke="none"/>',
        'prev': '<path d="M18 6v12l-9-6zM6 6v12" />',
        'next': '<path d="M6 6v12l9-6zM18 6v12" />',
        'back': '<path d="M15 5l-7 7 7 7" />',
        'more': '<circle cx="5" cy="12" r="1.4" fill="currentColor"/><circle cx="12" cy="12" r="1.4" fill="currentColor"/><circle cx="19" cy="12" r="1.4" fill="currentColor"/>',
        'heart': '<path d="M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10z" />',
        'mic': '<rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5 11a7 7 0 0 0 14 0M12 18v3" />',
        'share': '<path d="M12 3v12M7 8l5-5 5 5M5 14v6h14v-6" />',
        'bookmark': '<path d="M6 3h12v18l-6-4-6 4z" />',
        'stop': '<rect x="6" y="6" width="12" height="12" rx="2" fill="currentColor" stroke="none"/>',
        'x': '<path d="M6 6l12 12M18 6L6 18" />',
        'bolt': '<path d="M13 2L4 14h7l-1 8 9-12h-7z" />',
        'shield': '<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z" />',
        'power': '<path d="M12 3v9M6.3 7.3a8 8 0 1 0 11.4 0" />',
        'drop': '<path d="M12 3s6 7 6 11a6 6 0 0 1-12 0c0-4 6-11 6-11z" />',
        'sun': '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />',
        'leaf': '<path d="M5 19c0-9 6-14 15-14 0 9-5 15-14 15M5 19l7-7" />',
        'file': '<path d="M6 3h8l4 4v14H6zM14 3v4h4" />',
        'moon': '<path d="M20 14A8 8 0 0 1 10 4a8 8 0 1 0 10 10z" />',
        'cloud': '<path d="M7 18h10a4 4 0 0 0 .5-8A6 6 0 0 0 6 11a3.5 3.5 0 0 0 1 7z" />',
        'eye': '<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
        'mountain': '<path d="M3 20l6-10 4 6 3-4 5 8z" />',
        'clock': '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2" />',
        'skip': '<path d="M5 6v12l9-6zM17 6v12" />',
        'plus': '<path d="M12 5v14M5 12h14" />',
        'home': '<path d="M4 11l8-7 8 7v9h-5v-6h-6v6H4z" />',
        'search': '<circle cx="11" cy="11" r="6"/><path d="M20 20l-4.5-4.5" />',
        'user': '<circle cx="12" cy="8" r="4"/><path d="M4 21c1-4 4.5-6 8-6s7 2 8 6" />',
        'grid': '<rect x="4" y="4" width="7" height="7" rx="1.5"/><rect x="13" y="4" width="7" height="7" rx="1.5"/><rect x="4" y="13" width="7" height="7" rx="1.5"/><rect x="13" y="13" width="7" height="7" rx="1.5"/>',
        'wave': '<path d="M3 12h2l2-6 3 12 3-9 2 6 2-3h4" />',
    }
    return (f'<svg width="{size}" height="{size}" viewBox="0 0 24 24" fill="{fill}" stroke="currentColor" '
            f'stroke-width="{stroke}" stroke-linecap="round" stroke-linejoin="round">{paths[name]}</svg>')


def status_bar():
    return ('<div class="status"><span>9:41</span><span class="sys">'
            '<svg width="18" height="11" viewBox="0 0 18 11"><rect x="0" y="7" width="3" height="4" rx="1"/><rect x="5" y="5" width="3" height="6" rx="1"/><rect x="10" y="2.5" width="3" height="8.5" rx="1"/><rect x="15" y="0" width="3" height="11" rx="1"/></svg>'
            '<svg width="16" height="11" viewBox="0 0 16 11"><path d="M8 2.2c2.4 0 4.6.9 6.2 2.5l1.1-1.2A10.3 10.3 0 0 0 8 .5 10.3 10.3 0 0 0 .7 3.5l1.1 1.2A8.7 8.7 0 0 1 8 2.2zm0 3.4c1.5 0 2.8.6 3.8 1.5l1.1-1.2A7 7 0 0 0 8 3.9a7 7 0 0 0-4.9 2l1.1 1.2c1-.9 2.3-1.5 3.8-1.5zM8 9l1.9-1.6a2.9 2.9 0 0 0-3.8 0z"/></svg>'
            '<svg width="26" height="12" viewBox="0 0 26 12"><rect x=".5" y=".5" width="22" height="11" rx="3" fill="none" stroke="currentColor" opacity=".4"/><rect x="2" y="2" width="17" height="8" rx="1.8"/><rect x="23.5" y="4" width="1.6" height="4" rx=".8" opacity=".4"/></svg>'
            '</span></div>')


def qr(seed, n=21, size=132):
    rnd = random.Random(seed)
    cell = size / n
    cells = []
    for y in range(n):
        for x in range(n):
            finder = any(x0 <= x < x0 + 7 and y0 <= y < y0 + 7 for x0, y0 in ((0, 0), (n - 7, 0), (0, n - 7)))
            if finder:
                fx, fy = (x if x < 7 else x - (n - 7)), (y if y < 7 else y - (n - 7))
                d = max(abs(fx - 3), abs(fy - 3))
                on = d != 2
            else:
                on = rnd.random() < 0.48
            if on:
                cells.append(f'<rect x="{x * cell:.2f}" y="{y * cell:.2f}" width="{cell + .3:.2f}" height="{cell + .3:.2f}"/>')
    return f'<svg width="{size}" height="{size}" viewBox="0 0 {size} {size}" fill="currentColor">{"".join(cells)}</svg>'


# ---------------------------------------------------------------- screens (360x780)

# Glyph extents of the bar icons in their 24-unit box, so the bar can align ink, not boxes.
INK = {'back': (8, 15), 'x': (6, 18), 'more': (3.6, 20.4), 'share': (5, 19), 'search': (5, 20.5),
       'grid': (4, 20), 'plus': (5, 19), 'bookmark': (6, 18)}


def bar(left, title, right):
    """Top bar: the glyphs' ink sits on the 24 pt content edges, the title centred."""
    # +1: the 2-unit stroke puts ink half a stroke outside the path coordinates.
    l = f'<span class="bi" style="left:{24 - INK[left][0] + 1}px">{icon(left)}</span>' if left else ''
    r = f'<span class="bi" style="right:{INK[right][1] + 1}px">{icon(right)}</span>' if right else ''
    return f'<div class="bar">{l}<span class="bar-title">{title}</span>{r}</div>'


def hd(eyebrow='', title='', lead='', size='', extra=''):
    """Header group: eyebrow, heading, lead, 12 pt apart, measured cap to baseline."""
    parts = [f'<div class="eyebrow trim">{eyebrow}</div>' if eyebrow else '',
             f'<h1 class="trim {size}">{title}</h1>',
             f'<p class="lead trim">{lead}</p>' if lead else '', extra]
    return f'<div class="hd">{"".join(parts)}</div>'


def transport(left, mid, right, big=True):
    return f'<div class="row center gap32">{left}<button class="fab{" big" if big else ""}">{mid}</button>{right}</div>'


def screens():
    I = icon
    return {
        'flow-field': bar('back', 'Session', 'more') + f'''
<div class="sheet">
  {hd('Focus · 20 min', 'Deep focus', 'Follow one line at a time.')}
  <div class="scrub"><div class="track"><i style="width:38%"></i></div><div class="times"><span>07:42</span><span>20:00</span></div></div>
  {transport(I('prev', 28), I('pause', 32), I('next', 28))}
  <div class="chips"><span class="chip on">Rain</span><span class="chip">Brown noise</span><span class="chip">Silence</span></div>
</div>''',
        'curl-noise': bar('back', 'Route', 'share') + f'''
<div class="sheet">
  {hd('Loop · Moderate', 'Ridge loop')}
  <div class="stats3"><div><b>11.4 km</b><span>distance</span></div><div><b>412 m</b><span>ascent</span></div><div><b>3 h 20</b><span>estimated</span></div></div>
  <div class="group">
    <svg class="spark" viewBox="0 0 312 64" preserveAspectRatio="none"><path d="M0 56 C30 54 40 36 70 34 S110 10 140 12 190 40 220 36 260 6 312 18" fill="none" stroke="var(--accent)" stroke-width="2"/><path d="M0 56 C30 54 40 36 70 34 S110 10 140 12 190 40 220 36 260 6 312 18 V64 H0z" fill="var(--accent)" opacity=".14"/></svg>
    <div class="row between meta"><span>Trailhead</span><span>Summit 684 m</span><span>Trailhead</span></div>
  </div>
  <button class="btn">Start route</button>
</div>''',
        'plasma': bar('back', 'Now playing', 'more') + f'''
<div class="sheet">
  {hd('', 'Lava Hours', extra=f'<div class="row between"><p class="lead trim">Night Bloom · Slow Glow</p><span class="ic">{I("heart")}</span></div>')}
  <div class="scrub"><div class="track"><i style="width:36%"></i></div><div class="times"><span>1:24</span><span>3:58</span></div></div>
  {transport(I('prev', 28), I('play', 32), I('next', 28))}
  <div class="card row between"><div class="stack"><span class="meta">Up next</span><span class="strong">Sine Garden</span></div><span class="meta">4:12</span></div>
</div>''',
        'metaballs': bar('x', 'Assistant', 'more') + f'''
<div class="sheet">
  {hd('<i class="dot"></i>Listening', 'Move my 3 pm to tomorrow and tell the team.', size='d-s')}
  <div class="card stack"><span class="meta">Design review · Today 15:00</span><span class="strong">Tomorrow 15:00 · 6 people notified</span></div>
  <div class="row between"><div class="chips"><span class="chip">Undo</span><span class="chip">Another time</span></div><button class="fab">{I('mic', 28)}</button></div>
</div>''',
        'fireworks': bar('x', '', 'share') + f'''
<div class="sheet">
  {hd('Savings goal', 'Goal reached', 'You saved $5,000 for the trip, three weeks early.')}
  <div class="card stats2"><div><b>$5,000</b><span>saved</span></div><div><b>38</b><span>weekly transfers</span></div></div>
  <div class="row gap12"><button class="btn ghost">Share</button><button class="btn">Set next goal</button></div>
</div>''',
        'constellation': bar('back', 'Network', 'search') + f'''
<div class="sheet">
  {hd('Your team', '128', 'connections across 9 teams', size='d-xl')}
  <div class="list">
    <div class="li"><span class="av">AL</span><div class="stack grow"><span class="strong">Ana Lima</span><span class="meta">Design · 14 shared</span></div><span class="action">Connect</span></div>
    <div class="li"><span class="av">KO</span><div class="stack grow"><span class="strong">Kenji Ono</span><span class="meta">Platform · 9 shared</span></div><span class="action">Connect</span></div>
    <div class="li"><span class="av">SR</span><div class="stack grow"><span class="strong">Sam Reyes</span><span class="meta">Research · 6 shared</span></div><span class="action">Connect</span></div>
  </div>
  <button class="btn">Find people</button>
</div>''',
        'orbits': bar('back', 'Focus timer', 'more') + f'''
<div class="timer"><h1 class="trim d-xl">24:59</h1><span class="meta trim">Pomodoro 2 of 4</span></div>
<div class="sheet">
  <div class="row between"><div class="stack"><span class="strong">Write the release notes</span><span class="meta">Short break at 10:05</span></div><span class="tag">Deep work</span></div>
  {transport(f'<button class="round">{I("stop", 24)}</button>', I('pause', 32), f'<button class="round">{I("skip", 24)}</button>')}
  <div class="dots"><i class="on"></i><i class="on half"></i><i></i><i></i></div>
</div>''',
        'lissajous': bar('x', '<span class="rec"><i></i>Recording</span>', 'more') + f'''
<div class="sheet">
  {hd('Episode 35', 'Interference')}
  <div class="group"><div class="t2 trim">03:12</div>
  <div class="bars">{''.join(f'<i style="height:{h}px"></i>' for h in (8,14,22,30,18,26,40,28,16,34,44,30,20,12,24,36,22,14,28,38,26,12,8,18,30,22,14,10))}</div></div>
  {transport(f'<button class="round">{I("pause", 24)}</button>', I('stop', 28), f'<button class="round">{I("bookmark", 24)}</button>').replace('class="fab big"', 'class="fab big danger"')}
</div>''',
        'galaxy': bar('back', 'Tonight', 'more') + f'''
<div class="sheet">
  {hd('Clear skies · 21:40', 'Andromeda is up', 'Look north-east, 38° above the horizon.')}
  <div class="stats3 cards"><div class="card">{I('moon', 20)}<b>12%</b><span>moon</span></div><div class="card">{I('eye', 20)}<b>4 of 5</b><span>seeing</span></div><div class="card">{I('cloud', 20)}<b>8%</b><span>cloud</span></div></div>
  <button class="btn">Open sky map</button>
</div>''',
        'dipole': bar('back', 'Charging', 'more') + f'''
<div class="sheet">
  <div class="row between base"><h1 class="trim d-xl">76%</h1><div class="stack right"><span class="strong">11 kW</span><span class="meta">Full by 06:20</span></div></div>
  <div class="battery"><i style="width:76%"></i></div>
  <div class="stats2 card"><div><b>384 km</b><span>range</span></div><div><b>42 kWh</b><span>added</span></div></div>
  <button class="btn ghost">Stop charging</button>
</div>''',
        'attractor': bar('grid', 'Montréal', 'plus') + f'''
<div class="sheet">
  <div class="row between base"><h1 class="trim d-xl">−3°</h1><div class="stack right"><span class="strong">Light snow</span><span class="meta">High −1° · Low −8°</span></div></div>
  <div class="hours card">{''.join(f'<div><span class="meta">{h}</span>{I(ic, 20)}<b>{t}</b></div>' for h, ic, t in (('Now','cloud','−3°'),('14','cloud','−2°'),('15','sun','−1°'),('16','sun','−2°'),('17','moon','−4°'),('18','moon','−5°')))}</div>
  <div class="list">
    <div class="li"><span class="strong grow">Tomorrow</span>{I('sun', 20)}<span class="meta num">−9° / −2°</span></div>
    <div class="li"><span class="strong grow">Saturday</span>{I('cloud', 20)}<span class="meta num">−6° / 0°</span></div>
  </div>
</div>''',
        'vortex': bar('x', 'Nearby share', 'more') + f'''
<div class="sheet">
  {hd("To Maya's laptop", 'Sending 4 files')}
  <div class="group"><div class="battery thin"><i style="width:62%"></i></div><div class="row between meta"><span>62% · 1.2 of 1.9 GB</span><span>About 18 s left</span></div></div>
  <div class="list">
    <div class="li">{I('file', 20)}<div class="stack grow"><span class="strong">launch-cut.mov</span><span class="meta">1.4 GB · sending</span></div><span class="meta num">58%</span></div>
    <div class="li">{I('file', 20)}<div class="stack grow"><span class="strong">stills.zip</span><span class="meta">420 MB · sent</span></div><span class="meta num">Done</span></div>
  </div>
  <button class="btn ghost">Cancel</button>
</div>''',
        'halftone': bar('back', 'Issue 46', 'bookmark') + f'''
<div class="sheet">
  {hd('Print · 8 min read', 'The dot and the wave', 'By R. Okafor · Photographs by Lena Sato')}
  <p class="body">Every printed photograph is a lie told in dots. Seen from arm's length the rings dissolve into tone; up close they swell and shrink as the image passes through them.</p>
  <div class="row between"><span class="action">Next: Moiré</span><span class="row gap16 ic">{I('heart')}{I('share')}</span></div>
</div>''',
        'phyllotaxis': bar('back', 'My plants', 'more') + f'''
<div class="sheet">
  {hd('Helianthus annuus', 'Sunflower')}
  <div class="stats3 cards"><div class="card">{I('drop', 20)}<b>2 days</b><span>to water</span></div><div class="card">{I('sun', 20)}<b>Full</b><span>sun</span></div><div class="card">{I('leaf', 20)}<b>+4 cm</b><span>this week</span></div></div>
  <div class="card row between"><div class="stack"><span class="strong">Bloom expected</span><span class="meta">In about 9 days</span></div><span class="tag">Day 64</span></div>
  <button class="btn">Log watering</button>
</div>''',
        'sparkler': bar('back', 'Tickets', 'share') + f'''
<div class="sheet">
  <div class="ticket">
    {hd('Dec 31 · 21:00', "New Year's Eve", 'Rooftop, 12th floor · Doors 20:30')}
    <div class="perf"></div>
    <div class="row between"><div class="stack gap16"><div class="stack"><span class="meta">Admit</span><span class="strong">1 · General</span></div><div class="stack"><span class="meta">Ticket</span><span class="strong num">NYE-0060</span></div></div><div class="qr">{qr(60)}</div></div>
  </div>
  <button class="btn">Add to wallet</button>
</div>''',
        'arcs': bar('grid', 'Secure', 'more') + f'''
<div class="sheet">
  {hd('<i class="dot"></i>Connected · 00:42:13', 'Protected')}
  <div class="card row between"><div class="row gap12"><span class="ic accent">{I('shield')}</span><div class="stack"><span class="strong">Toronto · CA-07</span><span class="meta">WireGuard · 18 ms</span></div></div><span class="action">Change</span></div>
  <div class="row gap12"><div class="stats2 card grow"><div><b>1.8 GB</b><span>down</span></div><div><b>212 MB</b><span>up</span></div></div><button class="fab big">{I('power', 32)}</button></div>
</div>''',
    }


# The scale: 4 pt grid; 8 / 12 / 16 / 24 / 32 between things, 24 pt content edges.
# Type: three heading steps (d-xl 88, h1 48, d-s 32) in the effect's face; Inter for the rest
# at 16 (lead, body), 15 (strong), 13 (meta), 12 (eyebrow). Ink at 100 / 84 / 60 %.
# Icons: 20 inline, 24 tools, 28 transport, 32 in the primary button.
CSS = '''
* { margin: 0; padding: 0; box-sizing: border-box; }
html, body { width: 360px; height: 780px; overflow: hidden; background: transparent; }
body { position: relative; color: var(--ink); font: 400 15px/1.35 "UI"; -webkit-font-smoothing: antialiased; }
#art { position: absolute; left: 0; top: 0; width: 360px; height: 450px; }
.fade { position: absolute; inset: 0; background:
  linear-gradient(180deg, color-mix(in srgb, var(--surface) 70%, transparent) 0, transparent 110px),
  linear-gradient(180deg, transparent 230px, color-mix(in srgb, var(--surface) 70%, transparent) 340px, var(--surface) 450px, var(--surface)); }
.trim { text-box: trim-both cap alphabetic; }
.status { position: absolute; left: 0; right: 0; top: 0; height: 44px; padding: 14px 26px 0 30px; display: flex; justify-content: space-between; font: 600 15px "UI"; letter-spacing: -.01em; }
.status .sys { display: flex; gap: 6px; align-items: center; } .status svg { fill: currentColor; }
.bar { position: absolute; left: 0; right: 0; top: 52px; height: 40px; display: flex; align-items: center; justify-content: center; }
.bi { position: absolute; top: 8px; line-height: 0; }
.bar-title { font: 600 15px "UI"; }
.rec { display: flex; align-items: center; gap: 8px; } .rec i { width: 8px; height: 8px; border-radius: 50%; background: #FF4D4D; }
.sheet { position: absolute; left: 24px; right: 24px; bottom: 42px; display: flex; flex-direction: column; gap: 24px; }
.hd { display: flex; flex-direction: column; gap: 12px; }
.hd h1 + .lead { margin-top: 4px; }
h1 { font-family: "Title"; font-weight: var(--tw); font-style: var(--ts); font-size: 48px; line-height: 1.04; letter-spacing: var(--tt); text-wrap: balance; }
h1.d-xl { font-size: 88px; line-height: 1; } h1.d-s { font-size: 32px; line-height: 1.12; }
.eyebrow { font: 600 12px "UI"; letter-spacing: .12em; text-transform: uppercase; color: var(--accent); display: flex; align-items: center; gap: 8px; }
.dot { width: 8px; height: 8px; border-radius: 50%; background: var(--accent); }
.lead { font-size: 16px; line-height: 1.4; color: color-mix(in srgb, var(--ink) 72%, transparent); text-wrap: pretty; }
.body { font-size: 16px; line-height: 1.5; color: color-mix(in srgb, var(--ink) 84%, transparent); text-wrap: pretty; }
.meta { font-size: 13px; color: color-mix(in srgb, var(--ink) 60%, transparent); }
.strong { font-weight: 600; } .right { text-align: right; align-items: flex-end; } .grow { flex: 1; min-width: 0; }
.num { font-variant-numeric: tabular-nums; }
.row { display: flex; align-items: center; } .row.top { align-items: flex-start; } .row.base { align-items: last baseline; }
.between { justify-content: space-between; } .center { justify-content: center; }
.gap12 { gap: 12px; } .gap16 { gap: 16px; } .gap32 { gap: 32px; }
.stack { display: flex; flex-direction: column; gap: 4px; } .stack.gap16 { gap: 16px; }
.group { display: flex; flex-direction: column; gap: 12px; }
.ic { line-height: 0; color: color-mix(in srgb, var(--ink) 84%, transparent); } .ic.accent { color: var(--accent); }
.card { background: color-mix(in srgb, var(--ink) 7%, transparent); border: 1px solid color-mix(in srgb, var(--ink) 12%, transparent); border-radius: 16px; padding: 16px; }
.btn { display: block; width: 100%; height: 52px; border: 0; border-radius: 26px; background: var(--accent); color: var(--on-accent); font: 600 16px "UI"; }
.btn.ghost { background: transparent; color: var(--ink); border: 1.5px solid color-mix(in srgb, var(--ink) 32%, transparent); }
.fab { width: 64px; height: 64px; border-radius: 50%; border: 0; background: var(--accent); color: var(--on-accent); display: grid; place-items: center; flex: none; }
.fab.big { width: 76px; height: 76px; } .fab.danger { background: #FF4D4D; color: #fff; }
.fab svg path[d^="M8 5v14"] { transform: translateX(1.5px); }  /* the play triangle's optical centre */
.round { width: 52px; height: 52px; border-radius: 50%; border: 1.5px solid color-mix(in srgb, var(--ink) 32%, transparent); background: transparent; color: var(--ink); display: grid; place-items: center; }
.chips { display: flex; gap: 8px; flex-wrap: wrap; }
.chip { height: 36px; padding: 0 16px; display: inline-flex; align-items: center; border-radius: 18px; font: 500 13px "UI"; border: 1px solid color-mix(in srgb, var(--ink) 22%, transparent); white-space: nowrap; }
.chip.on { background: var(--ink); color: var(--surface); border-color: var(--ink); }
.action { height: 32px; padding: 0 14px; display: inline-flex; align-items: center; border-radius: 16px; font: 600 13px "UI"; color: var(--accent); border: 1px solid color-mix(in srgb, var(--accent) 50%, transparent); white-space: nowrap; flex: none; }
.tag { height: 24px; padding: 0 10px; display: inline-flex; align-items: center; border-radius: 6px; font: 600 11px "UI"; letter-spacing: .06em; text-transform: uppercase; color: color-mix(in srgb, var(--ink) 84%, transparent); background: color-mix(in srgb, var(--ink) 10%, transparent); white-space: nowrap; flex: none; }
.track { height: 4px; border-radius: 2px; background: color-mix(in srgb, var(--ink) 20%, transparent); position: relative; }
.track i { position: absolute; left: 0; top: 0; bottom: 0; border-radius: 2px; background: var(--ink); }
.track i::after { content: ""; position: absolute; right: -6px; top: -4px; width: 12px; height: 12px; border-radius: 50%; background: var(--ink); }
.times { display: flex; justify-content: space-between; margin-top: 12px; font: 500 12px "UI"; color: color-mix(in srgb, var(--ink) 60%, transparent); font-variant-numeric: tabular-nums; }
.stats3, .stats2 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; }
.stats2 { grid-template-columns: 1fr 1fr; }
.stats3 > div, .stats2 > div { display: flex; flex-direction: column; gap: 4px; }
.stats3 b, .stats2 b { font: 600 20px "UI"; letter-spacing: -.02em; font-variant-numeric: tabular-nums; }
.stats3 span, .stats2 span { font-size: 13px; color: color-mix(in srgb, var(--ink) 60%, transparent); }
.stats3.cards .card { padding: 12px; } .stats3.cards svg { color: var(--accent); margin-bottom: 8px; } .stats3.cards b { font-size: 18px; }
.spark { width: 100%; height: 64px; display: block; }
.list .li { display: flex; align-items: center; gap: 12px; min-height: 56px; border-bottom: 1px solid color-mix(in srgb, var(--ink) 10%, transparent); }
.list .li:last-child { border-bottom: 0; }
.list .li > svg { color: var(--accent); flex: none; }
.av { width: 40px; height: 40px; border-radius: 50%; display: grid; place-items: center; font: 600 13px "UI"; background: color-mix(in srgb, var(--accent) 24%, transparent); flex: none; }
.timer { position: absolute; left: 0; right: 0; top: 225px; transform: translateY(-50%); display: flex; flex-direction: column; align-items: center; gap: 16px; }
.timer::before { content: ""; position: absolute; left: 50%; top: 50%; width: 280px; height: 200px; transform: translate(-50%, -50%); border-radius: 50%;
  background: radial-gradient(closest-side, color-mix(in srgb, var(--surface) 92%, transparent) 55%, transparent); z-index: -1; }
.timer h1 { font-variant-numeric: tabular-nums; }
.t2 { font: 300 44px "UI"; letter-spacing: -.02em; font-variant-numeric: tabular-nums; }
.dots { display: flex; justify-content: center; gap: 12px; } .dots i { width: 8px; height: 8px; border-radius: 50%; border: 1.5px solid color-mix(in srgb, var(--ink) 40%, transparent); }
.dots i.on { background: var(--accent); border-color: var(--accent); } .dots i.half { background: linear-gradient(90deg, var(--accent) 50%, transparent 50%); }
.bars { display: flex; align-items: center; gap: 4px; height: 48px; } .bars i { flex: 1; border-radius: 2px; background: var(--accent); }
.bars i:nth-child(n+19) { background: color-mix(in srgb, var(--ink) 24%, transparent); }
.battery { height: 12px; border-radius: 6px; background: color-mix(in srgb, var(--ink) 14%, transparent); overflow: hidden; } .battery.thin { height: 6px; }
.battery i { display: block; height: 100%; border-radius: inherit; background: linear-gradient(90deg, color-mix(in srgb, var(--accent) 40%, transparent), var(--accent)); }
.hours { display: grid; grid-template-columns: repeat(6, 1fr); text-align: center; padding: 16px 8px; }
.hours div { display: flex; flex-direction: column; align-items: center; gap: 8px; } .hours svg { color: var(--accent); } .hours b { font-weight: 600; font-size: 15px; font-variant-numeric: tabular-nums; }
.ticket { background: color-mix(in srgb, var(--ink) 8%, transparent); border: 1px solid color-mix(in srgb, var(--ink) 14%, transparent); border-radius: 24px; padding: 24px; display: flex; flex-direction: column; gap: 24px; }
.ticket h1 { font-size: 48px; }
.perf { border-top: 2px dashed color-mix(in srgb, var(--ink) 22%, transparent); margin: 0 -24px; }
.qr { background: var(--ink); color: var(--surface); padding: 8px; border-radius: 12px; line-height: 0; }
.home { position: absolute; left: 50%; bottom: 8px; width: 134px; height: 5px; margin-left: -67px; border-radius: 3px; background: var(--ink); opacity: .9; }
'''


def contrast_ink(hex_):
    r, g, b = (int(hex_[i:i + 2], 16) / 255 for i in (1, 3, 5))
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
    return '#0B0B0F' if lum > 0.45 else '#FFFFFF'


EXTRA = {
    'halftone': '.fade { background: linear-gradient(180deg, color-mix(in srgb, var(--surface) 92%, transparent) 0, color-mix(in srgb, var(--surface) 40%, transparent) 110px, transparent 170px), linear-gradient(180deg, transparent 220px, color-mix(in srgb, var(--surface) 80%, transparent) 330px, var(--surface) 430px, var(--surface)); }',
}


def html(p, body):
    f = p.font
    return f'''<!doctype html><html><head><meta charset="utf-8"><style>
{P.face('Title', f['package'], f['weight'], f['style'])}
{P.face('UI', 'inter', 400)}
{P.face('UI', 'inter', 500)}
{P.face('UI', 'inter', 600)}
{P.face('UI', 'inter', 300)}
:root {{ --ink: {p.ink}; --accent: {p.accent}; --surface: {p.surface}; --on-accent: {contrast_ink(p.accent)};
  --tw: {f['weight']}; --ts: {f['style']}; --tt: {f['tracking'] / 5:.1f}px; }}
{CSS}
{EXTRA.get(p.name, '')}
</style></head><body>
<div id="art"></div><div class="fade"></div>
{status_bar()}
{body}
<div class="home"></div>
</body></html>'''


def overlays(posters, work):
    bodies = screens()
    jobs = []
    for p in posters:
        path = os.path.join(work, p.name + '.html')
        open(path, 'w').write(html(p, bodies[p.name]))
        jobs.append({'html': path, 'out': os.path.join(work, p.name), 'scale': SCALE, 'width': W, 'height': H})
    open(os.path.join(work, 'jobs.json'), 'w').write(json.dumps(jobs))
    subprocess.check_call(['node', os.path.join(P.HERE, 'posters_overlay.js'), os.path.join(work, 'jobs.json')])
    return {p.name: (Image.open(os.path.join(work, p.name + '.overlay.png')).convert('RGBA'), None,
                     json.load(open(os.path.join(work, p.name + '.json')))) for p in posters}


def stills(posters, raw, out):
    os.makedirs(out, exist_ok=True)
    with tempfile.TemporaryDirectory() as work:
        layers = overlays(posters, work)
        for i, p in enumerate(posters):
            art = Image.open(os.path.join(raw, p.name + '.png')).convert('RGB').resize(P.VIDEO, Image.LANCZOS)
            path = os.path.join(out, p.slug + '.jpg')
            P.compose(p, P.finished(art, p, 200 + i), layers[p.name]).save(path, quality=92, subsampling=0)
            print('still', path)


def videos(posters, seq, out):
    os.makedirs(out, exist_ok=True)
    with tempfile.TemporaryDirectory() as work:
        layers = overlays(posters, work)
        state = ({p.name: p for p in posters}, layers)
        with Pool(4, initializer=P._init, initargs=(state,)) as pool:
            for p in posters:
                plan = P.timeline(p, sorted(glob.glob(os.path.join(seq, p.name, '*.jpg'))))
                path = os.path.join(out, p.slug + '.mp4')
                ff = subprocess.Popen(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                                       '-s', f'{W * SCALE}x{H * SCALE}', '-r', str(P.FPS), '-i', '-',
                                       '-c:v', 'libx264', '-preset', 'slow', '-crf', '22', '-pix_fmt', 'yuv420p',
                                       '-movflags', '+faststart', path], stdin=subprocess.PIPE)
                jobs = [(p.name, f, tail, w, None, k) for k, (f, tail, w, _) in enumerate(plan)]
                for data in pool.imap(P._frame, jobs, chunksize=4):
                    ff.stdin.write(data)
                ff.stdin.close()
                ff.wait()
                print('video', path, f'{len(plan) / P.FPS:.1f} s')


# Overview: the 16 screens in two rows of eight, rounded like the device, on near-black.
# 3840 px wide for the video (UHD), every tile on even pixels so chroma does not smear edges.
OV = dict(cols=8, tile=(434, 940), gap=32, margin=72, radius=56, ground='#0B0B0F')


def overview(folder):
    from PIL import ImageDraw
    cols, (tw, th), gap, m, r = OV['cols'], OV['tile'], OV['gap'], OV['margin'], OV['radius']
    names = sorted(f for f in os.listdir(folder) if f.endswith('.jpg') and not f.startswith('00-'))
    rows = -(-len(names) // cols)
    size = (2 * m + cols * tw + (cols - 1) * gap, 2 * m + rows * th + (rows - 1) * gap)
    spots = [(m + (k % cols) * (tw + gap), m + (k // cols) * (th + gap)) for k in range(len(names))]
    mask = Image.new('L', (tw, th), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, tw - 1, th - 1), radius=r, fill=255)

    sheet = Image.new('RGB', size, OV['ground'])
    for name, spot in zip(names, spots):
        sheet.paste(Image.open(os.path.join(folder, name)).convert('RGB').resize((tw, th), Image.LANCZOS), spot, mask)
    sheet.save(os.path.join(folder, '00-overview.jpg'), quality=90)
    print('overview', size)

    with tempfile.TemporaryDirectory() as work:
        mask_path = os.path.join(work, 'mask.png')
        mask.save(mask_path)
        args = ['ffmpeg', '-y', '-loglevel', 'error']
        for name in names:
            args += ['-i', os.path.join(folder, 'video', name[:-4] + '.mp4')]
        args += ['-loop', '1', '-i', mask_path]
        n = len(names)
        graph = [f'[{n}]format=gray,split={n}' + ''.join(f'[m{k}]' for k in range(n))]
        graph += [f'[{k}]scale={tw}:{th}:flags=lanczos,format=rgba[s{k}];[s{k}][m{k}]alphamerge[t{k}]' for k in range(n)]
        graph.append(f'color=c={OV["ground"]}:s={size[0]}x{size[1]}:r={P.FPS}:d={P.LOOP / P.FPS}[b0]')
        for k, (x, y) in enumerate(spots):
            graph.append(f'[b{k}][t{k}]overlay={x}:{y}:shortest=1[b{k + 1}]')
        args += ['-filter_complex', ';'.join(graph), '-map', f'[b{n}]', '-frames:v', str(P.LOOP),
                 '-c:v', 'libx264', '-preset', 'slow', '-crf', '20', '-pix_fmt', 'yuv420p', '-movflags', '+faststart',
                 os.path.join(folder, 'video', '00-overview.mp4')]
        subprocess.check_call(args)
    print('overview video', size, f'{P.LOOP / P.FPS:.1f} s')


if __name__ == '__main__':
    mode = sys.argv[1]
    if mode == 'overview':
        overview(sys.argv[2])
        sys.exit()
    src, dst = sys.argv[2:4]
    only = set(sys.argv[4:])
    posters = [p for p in P.load(SHOTS) if not only or p.name in only]
    P.ensure_fonts(posters, extra=('inter',))
    (stills if mode == 'stills' else videos)(posters, src, dst)
