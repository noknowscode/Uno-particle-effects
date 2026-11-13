"""App screen mockups: each series-1 effect as the hero of a phone screen.

Usage:
  python mockups.py stills <raw dir> <out dir> [names]          # 1080x2340 JPEGs
  python mockups.py videos <sequence dir> <out dir> [names]     # 1080x2340 10 s loops

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

def screens():
    I = icon
    return {
        'flow-field': f'''
<div class="bar">{I('back')}<span class="bar-title">Session</span>{I('more')}</div>
<div class="sheet" style="top:380px">
  <div class="eyebrow">Focus · 20 min</div>
  <h1>Deep focus</h1>
  <p class="lead">Let your attention follow one line at a time.</p>
  <div class="scrub"><div class="track"><i style="width:38%"></i></div><div class="times"><span>07:42</span><span>20:00</span></div></div>
  <div class="row center gap24" style="margin-top:22px">{I('prev', 26)}<button class="fab">{I('pause', 30)}</button>{I('next', 26)}</div>
  <div class="chips" style="margin-top:30px"><span class="chip on">Rain</span><span class="chip">Brown noise</span><span class="chip">Silence</span></div>
</div>''',
        'curl-noise': f'''
<div class="bar">{I('back')}<span class="bar-title">Route</span>{I('share')}</div>
<div class="sheet" style="top:360px">
  <div class="eyebrow">Loop · Moderate</div>
  <h1>Ridge loop</h1>
  <div class="stats3"><div><b>11.4</b><span>km</span></div><div><b>412</b><span>m ascent</span></div><div><b>3h 20</b><span>est. time</span></div></div>
  <svg class="spark" viewBox="0 0 312 70" preserveAspectRatio="none"><path d="M0 60 C30 58 40 40 70 38 S110 12 140 14 190 44 220 40 260 8 312 20" fill="none" stroke="var(--accent)" stroke-width="2.5"/><path d="M0 60 C30 58 40 40 70 38 S110 12 140 14 190 44 220 40 260 8 312 20 V70 H0z" fill="var(--accent)" opacity=".14"/></svg>
  <div class="row between muted small"><span>Trailhead</span><span>Summit 684 m</span><span>Trailhead</span></div>
  <button class="btn" style="margin-top:26px">Start route</button>
</div>''',
        'plasma': f'''
<div class="bar">{I('back')}<span class="bar-title">Now playing</span>{I('more')}</div>
<div class="sheet" style="top:440px">
  <div class="row between"><div><h1 style="font-size:40px">Lava Hours</h1><p class="lead" style="margin-top:2px">Night Bloom · Slow Glow</p></div>{I('heart', 26)}</div>
  <div class="scrub" style="margin-top:20px"><div class="track"><i style="width:36%"></i></div><div class="times"><span>1:24</span><span>3:58</span></div></div>
  <div class="row center gap32" style="margin-top:18px">{I('prev', 28)}<button class="fab big">{I('play', 34)}</button>{I('next', 28)}</div>
  <div class="card row between" style="margin-top:28px"><div><div class="muted small">Up next</div><div class="strong">Sine Garden</div></div><span class="muted small">4:12</span></div>
</div>''',
        'metaballs': f'''
<div class="bar">{I('x')}<span class="bar-title">Assistant</span>{I('more')}</div>
<div class="sheet" style="top:380px">
  <div class="eyebrow pulse">Listening…</div>
  <h1 style="font-size:30px;line-height:1.05">Move my 3 pm to tomorrow and tell the team.</h1>
  <div class="card" style="margin-top:22px"><div class="muted small">Design review · Today 15:00</div><div class="strong" style="margin-top:4px">→ Tomorrow 15:00 · 6 people notified</div></div>
  <div class="row between" style="margin-top:22px"><div class="chips"><span class="chip">Undo</span><span class="chip">Another time</span></div><button class="fab">{I('mic', 26)}</button></div>
</div>''',
        'fireworks': f'''
<div class="bar">{I('x')}<span></span>{I('share')}</div>
<div class="sheet" style="top:400px">
  <div class="eyebrow">Savings goal</div>
  <h1 style="font-size:56px">Goal reached</h1>
  <p class="lead">You saved $5,000 for the trip, three weeks early.</p>
  <div class="card stats2" style="margin-top:22px"><div><b>$5,000</b><span>saved</span></div><div><b>38</b><span>weekly transfers</span></div></div>
  <div class="row gap12" style="margin-top:28px"><button class="btn ghost">Share</button><button class="btn">Set next goal</button></div>
</div>''',
        'constellation': f'''
<div class="bar">{I('back')}<span class="bar-title">Network</span>{I('search')}</div>
<div class="sheet" style="top:330px">
  <div class="eyebrow">Your team</div>
  <h1 style="font-size:58px">128</h1>
  <p class="lead" style="margin-top:-2px">connections across 9 teams</p>
  <div class="list" style="margin-top:20px">
    <div class="li"><span class="av">AL</span><div><div class="strong">Ana Lima</div><div class="muted small">Design systems · 14 shared</div></div><span class="pill">Connect</span></div>
    <div class="li"><span class="av">KO</span><div><div class="strong">Kenji Ono</div><div class="muted small">Platform · 9 shared</div></div><span class="pill">Connect</span></div>
    <div class="li"><span class="av">SR</span><div><div class="strong">Sam Reyes</div><div class="muted small">Research · 6 shared</div></div><span class="pill">Connect</span></div>
  </div>
  <button class="btn" style="margin-top:22px">Find people</button>
</div>''',
        'orbits': f'''
<div class="bar">{I('back')}<span class="bar-title">Focus timer</span>{I('more')}</div>
<div class="timer"><div class="t">24:59</div><div class="muted small">Pomodoro 2 of 4</div></div>
<div class="sheet" style="top:500px">
  <div class="row between"><div><div class="strong">Write the release notes</div><div class="muted small">Short break at 10:05</div></div><span class="pill">Deep</span></div>
  <div class="row center gap24" style="margin-top:30px"><button class="round">{I('stop', 22)}</button><button class="fab big">{I('pause', 30)}</button><button class="round">{I('skip', 22)}</button></div>
  <div class="dots" style="margin-top:30px"><i class="on"></i><i class="on half"></i><i></i><i></i></div>
</div>''',
        'lissajous': f'''
<div class="bar">{I('x')}<span class="bar-title rec"><i></i>Recording</span>{I('more')}</div>
<div class="sheet" style="top:420px">
  <div class="eyebrow">Episode 35</div>
  <h1 style="font-size:52px">Interference</h1>
  <div class="t2">03:12</div>
  <div class="bars">{''.join(f'<i style="height:{h}px"></i>' for h in (8,14,22,30,18,26,40,28,16,34,44,30,20,12,24,36,22,14,28,38,26,12,8,18,30,22,14,10))}</div>
  <div class="row center gap24" style="margin-top:26px"><button class="round">{I('pause', 22)}</button><button class="fab big danger">{I('stop', 26)}</button><button class="round">{I('bookmark', 22)}</button></div>
</div>''',
        'galaxy': f'''
<div class="bar">{I('back')}<span class="bar-title">Tonight</span>{I('more')}</div>
<div class="sheet" style="top:380px">
  <div class="eyebrow">Clear skies · 21:40</div>
  <h1 style="font-size:44px">Andromeda is up</h1>
  <p class="lead">Look north-east after 21:40, 38° above the horizon.</p>
  <div class="stats3 cards" style="margin-top:20px"><div class="card">{I('moon', 20)}<b>12%</b><span>moon</span></div><div class="card">{I('eye', 20)}<b>4/5</b><span>seeing</span></div><div class="card">{I('cloud', 20)}<b>8%</b><span>cloud</span></div></div>
  <button class="btn" style="margin-top:24px">Open sky map</button>
</div>''',
        'dipole': f'''
<div class="bar">{I('back')}<span class="bar-title">Charging</span>{I('more')}</div>
<div class="sheet" style="top:400px">
  <div class="row between" style="align-items:flex-end"><h1 style="font-size:84px;line-height:.9">76%</h1><div class="right"><div class="strong">11 kW</div><div class="muted small">Full by 06:20</div></div></div>
  <div class="battery" style="margin-top:20px"><i style="width:76%"></i></div>
  <div class="stats2 card" style="margin-top:20px"><div><b>384 km</b><span>range</span></div><div><b>42 kWh</b><span>added</span></div></div>
  <button class="btn ghost" style="margin-top:24px">Stop charging</button>
</div>''',
        'attractor': f'''
<div class="bar">{I('grid')}<span class="bar-title">Montréal</span>{I('plus')}</div>
<div class="sheet" style="top:400px">
  <div class="row between" style="align-items:flex-end"><h1 style="font-size:96px;line-height:.85">−3°</h1><div class="right"><div class="strong">Light snow</div><div class="muted small">H −1° · L −8°</div></div></div>
  <div class="hours card" style="margin-top:22px">{''.join(f'<div><span class="muted small">{h}</span>{I(ic, 20)}<b>{t}</b></div>' for h, ic, t in (('Now','cloud','−3°'),('14','cloud','−2°'),('15','sun','−1°'),('16','sun','−2°'),('17','moon','−4°'),('18','moon','−5°')))}</div>
  <div class="list" style="margin-top:14px">
    <div class="li"><span class="strong">Tomorrow</span>{I('sun', 20)}<span class="muted">−9° / −2°</span></div>
    <div class="li"><span class="strong">Saturday</span>{I('cloud', 20)}<span class="muted">−6° / 0°</span></div>
  </div>
</div>''',
        'vortex': f'''
<div class="bar">{I('x')}<span class="bar-title">Nearby share</span>{I('more')}</div>
<div class="sheet" style="top:370px">
  <div class="eyebrow">To Maya's laptop</div>
  <h1 style="font-size:46px">Sending 4 files</h1>
  <div class="battery thin" style="margin-top:14px"><i style="width:62%"></i></div>
  <div class="row between muted small" style="margin-top:8px"><span>62% · 1.2 GB of 1.9 GB</span><span>~ 18 s</span></div>
  <div class="list" style="margin-top:16px">
    <div class="li">{I('file', 20)}<div class="grow"><div class="strong">launch-cut.mov</div><div class="muted small">1.4 GB · sending</div></div><span class="muted small">58%</span></div>
    <div class="li">{I('file', 20)}<div class="grow"><div class="strong">stills.zip</div><div class="muted small">420 MB · done</div></div><span class="muted small">✓</span></div>
  </div>
  <button class="btn ghost" style="margin-top:18px">Cancel</button>
</div>''',
        'halftone': f'''
<div class="bar">{I('back')}<span class="bar-title">Issue 46</span>{I('bookmark')}</div>
<div class="sheet" style="top:410px">
  <div class="eyebrow">Print · 8 min read</div>
  <h1 style="font-size:42px;line-height:1">The dot and the wave</h1>
  <p class="byline muted small">By R. Okafor · Photographs by the press</p>
  <p class="body">Every printed photograph is a lie told in dots. Seen from arm's length the rings dissolve into tone; up close they swell and shrink as the image passes through them, a wave you can only see when you stop reading.</p>
  <div class="row between" style="margin-top:20px"><span class="pill">Next: Moiré</span><span class="row gap16 muted">{I('heart', 22)}{I('share', 22)}</span></div>
</div>''',
        'phyllotaxis': f'''
<div class="bar">{I('back')}<span class="bar-title">My plants</span>{I('more')}</div>
<div class="sheet" style="top:400px">
  <div class="eyebrow">Helianthus annuus</div>
  <h1 style="font-size:56px">Sunflower</h1>
  <div class="stats3 cards" style="margin-top:18px"><div class="card">{I('drop', 20)}<b>2 days</b><span>to water</span></div><div class="card">{I('sun', 20)}<b>Full</b><span>sun</span></div><div class="card">{I('leaf', 20)}<b>+4 cm</b><span>this week</span></div></div>
  <div class="card row between" style="margin-top:12px"><div><div class="strong">Bloom expected</div><div class="muted small">In about 9 days</div></div><span class="pill">89 seeds/cm²</span></div>
  <button class="btn" style="margin-top:22px">Log watering</button>
</div>''',
        'sparkler': f'''
<div class="bar">{I('back')}<span class="bar-title">Tickets</span>{I('share')}</div>
<div class="sheet" style="top:330px">
  <div class="ticket">
    <div class="eyebrow">Dec 31 · 21:00</div>
    <h1 style="font-size:46px;line-height:1">New Year's Eve</h1>
    <p class="muted small" style="margin-top:6px">Rooftop, 12th floor · Doors 20:30</p>
    <div class="perf"></div>
    <div class="row between" style="align-items:center"><div><div class="muted small">Admit</div><div class="strong">1 · General</div><div class="muted small" style="margin-top:10px">Ticket</div><div class="strong">NYE-0060</div></div><div class="qr">{qr(60)}</div></div>
  </div>
  <button class="btn" style="margin-top:20px">Add to wallet</button>
</div>''',
        'arcs': f'''
<div class="bar">{I('grid')}<span class="bar-title">Secure</span>{I('more')}</div>
<div class="sheet" style="top:410px">
  <div class="row between" style="align-items:center"><div><div class="eyebrow on">● Connected · 00:42:13</div><h1 style="font-size:40px">Protected</h1></div><button class="fab">{I('power', 28)}</button></div>
  <div class="card row between" style="margin-top:20px"><div class="row gap12">{I('shield', 22)}<div><div class="strong">Toronto · CA-07</div><div class="muted small">WireGuard · 18 ms</div></div></div><span class="pill">Change</span></div>
  <div class="stats2 card" style="margin-top:12px"><div><b>1.8 GB</b><span>down</span></div><div><b>212 MB</b><span>up</span></div></div>
</div>''',
    }


CSS = '''
* { margin: 0; padding: 0; box-sizing: border-box; }
html, body { width: 360px; height: 780px; overflow: hidden; background: transparent; }
body { position: relative; color: var(--ink); font: 400 15px/1.35 "UI"; -webkit-font-smoothing: antialiased; }
#art { position: absolute; left: 0; top: 0; width: 360px; height: 450px; }
.fade { position: absolute; inset: 0; background:
  linear-gradient(180deg, color-mix(in srgb, var(--surface) 55%, transparent) 0, transparent 90px),
  linear-gradient(180deg, transparent 230px, color-mix(in srgb, var(--surface) 70%, transparent) 340px, var(--surface) 450px, var(--surface)); }
.status { position: absolute; left: 0; right: 0; top: 0; height: 44px; padding: 14px 26px 0 30px; display: flex; justify-content: space-between; font: 600 15px "UI"; letter-spacing: -.01em; }
.status .sys { display: flex; gap: 6px; align-items: center; } .status svg { fill: currentColor; }
.bar { position: absolute; left: 0; right: 0; top: 50px; height: 44px; padding: 0 16px; display: flex; align-items: center; justify-content: space-between; }
.bar-title { font: 600 15px "UI"; opacity: .9; }
.rec { display: flex; align-items: center; gap: 8px; } .rec i { width: 8px; height: 8px; border-radius: 50%; background: #FF4D4D; }
.sheet { position: absolute; left: 24px; right: 24px; }
h1 { font-family: "Title"; font-weight: var(--tw); font-style: var(--ts); font-size: 48px; line-height: 1; letter-spacing: var(--tt); }
.eyebrow { font: 600 12px "UI"; letter-spacing: .12em; text-transform: uppercase; color: var(--accent); margin-bottom: 10px; }
.eyebrow.on { color: var(--accent); }
.lead { margin-top: 8px; font-size: 16px; color: color-mix(in srgb, var(--ink) 70%, transparent); }
.body { margin-top: 14px; font-size: 15px; line-height: 1.5; color: color-mix(in srgb, var(--ink) 82%, transparent); }
.byline { margin-top: 8px; }
.muted { color: color-mix(in srgb, var(--ink) 58%, transparent); } .small { font-size: 13px; } .strong { font-weight: 600; }
.right { text-align: right; } .grow { flex: 1; }
.row { display: flex; align-items: center; } .between { justify-content: space-between; } .center { justify-content: center; }
.gap12 { gap: 12px; } .gap16 { gap: 16px; } .gap24 { gap: 24px; } .gap32 { gap: 32px; }
.card { background: color-mix(in srgb, var(--ink) 7%, transparent); border: 1px solid color-mix(in srgb, var(--ink) 12%, transparent); border-radius: 18px; padding: 14px 16px; backdrop-filter: blur(12px); }
.btn { display: block; width: 100%; height: 52px; border: 0; border-radius: 26px; background: var(--accent); color: var(--on-accent); font: 600 16px "UI"; }
.btn.ghost { background: transparent; color: var(--ink); border: 1.5px solid color-mix(in srgb, var(--ink) 35%, transparent); }
.fab { width: 64px; height: 64px; border-radius: 50%; border: 0; background: var(--accent); color: var(--on-accent); display: grid; place-items: center; }
.fab.big { width: 76px; height: 76px; } .fab.danger { background: #FF4D4D; color: #fff; }
.round { width: 52px; height: 52px; border-radius: 50%; border: 1.5px solid color-mix(in srgb, var(--ink) 30%, transparent); background: transparent; color: var(--ink); display: grid; place-items: center; }
.chips { display: flex; gap: 8px; flex-wrap: wrap; }
.chip, .pill { padding: 8px 14px; border-radius: 999px; font: 500 13px "UI"; border: 1px solid color-mix(in srgb, var(--ink) 22%, transparent); white-space: nowrap; }
.chip.on { background: var(--ink); color: var(--surface); border-color: var(--ink); }
.pill { padding: 6px 12px; background: color-mix(in srgb, var(--accent) 18%, transparent); border-color: color-mix(in srgb, var(--accent) 40%, transparent); }
.scrub { margin-top: 26px; } .track { height: 4px; border-radius: 2px; background: color-mix(in srgb, var(--ink) 20%, transparent); position: relative; }
.track i { position: absolute; left: 0; top: 0; bottom: 0; border-radius: 2px; background: var(--ink); }
.track i::after { content: ""; position: absolute; right: -6px; top: -4px; width: 12px; height: 12px; border-radius: 50%; background: var(--ink); }
.times { display: flex; justify-content: space-between; margin-top: 8px; font: 500 12px "UI"; color: color-mix(in srgb, var(--ink) 58%, transparent); font-variant-numeric: tabular-nums; }
.stats3, .stats2 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-top: 18px; }
.stats2 { grid-template-columns: 1fr 1fr; }
.stats3 b, .stats2 b { display: block; font: 600 22px "UI"; letter-spacing: -.02em; }
.stats3 span, .stats2 span { font-size: 12px; color: color-mix(in srgb, var(--ink) 58%, transparent); }
.stats3.cards .card { padding: 12px; } .stats3.cards svg { color: var(--accent); margin-bottom: 8px; } .stats3.cards b { font-size: 18px; }
.spark { width: 100%; height: 70px; margin-top: 18px; display: block; }
.list .li { display: flex; align-items: center; gap: 12px; padding: 12px 0; border-bottom: 1px solid color-mix(in srgb, var(--ink) 10%, transparent); }
.list .li > div:not(.grow) { flex: 1; } .list .li svg { color: var(--accent); flex: none; }
.av { width: 40px; height: 40px; border-radius: 50%; display: grid; place-items: center; font: 600 13px "UI"; background: color-mix(in srgb, var(--accent) 24%, transparent); color: var(--ink); flex: none; }
.timer { position: absolute; left: 0; right: 0; top: 190px; text-align: center; }
.timer::before { content: ""; position: absolute; left: 50%; top: 36px; width: 210px; height: 150px; transform: translate(-50%, -50%); border-radius: 50%;
  background: radial-gradient(closest-side, color-mix(in srgb, var(--surface) 92%, transparent) 55%, transparent); z-index: -1; }
.timer .t { font-family: "Title"; font-weight: var(--tw); font-size: 72px; line-height: 1; letter-spacing: var(--tt); font-variant-numeric: tabular-nums; }
.t2 { font: 300 44px "UI"; letter-spacing: -.02em; margin-top: 10px; font-variant-numeric: tabular-nums; }
.dots { display: flex; justify-content: center; gap: 10px; } .dots i { width: 10px; height: 10px; border-radius: 50%; border: 1.5px solid color-mix(in srgb, var(--ink) 40%, transparent); }
.dots i.on { background: var(--accent); border-color: var(--accent); } .dots i.half { background: linear-gradient(90deg, var(--accent) 50%, transparent 50%); }
.bars { display: flex; align-items: center; gap: 4px; height: 50px; margin-top: 14px; } .bars i { flex: 1; border-radius: 2px; background: var(--accent); }
.bars i:nth-child(n+19) { background: color-mix(in srgb, var(--ink) 25%, transparent); }
.battery { height: 14px; border-radius: 7px; background: color-mix(in srgb, var(--ink) 14%, transparent); overflow: hidden; } .battery.thin { height: 6px; }
.battery i { display: block; height: 100%; border-radius: inherit; background: linear-gradient(90deg, color-mix(in srgb, var(--accent) 40%, transparent), var(--accent)); }
.hours { display: grid; grid-template-columns: repeat(6, 1fr); text-align: center; padding: 12px 6px; }
.hours div { display: flex; flex-direction: column; align-items: center; gap: 6px; } .hours svg { color: var(--accent); } .hours b { font-weight: 600; font-size: 15px; }
.ticket { background: color-mix(in srgb, var(--ink) 8%, transparent); border: 1px solid color-mix(in srgb, var(--ink) 14%, transparent); border-radius: 22px; padding: 20px; backdrop-filter: blur(14px); }
.perf { border-top: 2px dashed color-mix(in srgb, var(--ink) 22%, transparent); margin: 18px -20px; }
.qr { background: var(--ink); color: var(--surface); padding: 8px; border-radius: 10px; line-height: 0; }
.pulse { display: flex; align-items: center; gap: 8px; } .pulse::before { content: ""; width: 8px; height: 8px; border-radius: 50%; background: var(--accent); }
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


if __name__ == '__main__':
    mode, src, dst = sys.argv[1:4]
    only = set(sys.argv[4:])
    posters = [p for p in P.load(SHOTS) if not only or p.name in only]
    P.ensure_fonts(posters, extra=('inter',))
    (stills if mode == 'stills' else videos)(posters, src, dst)
