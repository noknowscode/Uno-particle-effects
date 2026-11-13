"""Per-effect posters, still and animated, one palette each.

Usage:
  python posters.py stills <raw dir> <out dir>          # 1600x2000 JPEGs
  python posters.py videos <sequence dir> <out dir>     # 1080x1350 MP4s

<raw dir> holds the renders from `--art posters.json`; <sequence dir> holds the
frame folders from `--art` with the shot list posters_video_spec.py writes.

Two layouts, split by surface:
  plate     dark pieces. The art fills the sheet; the bar under the caption is
            the run, lit where the exposure was taken (or, animated, as it forms).
  specimen  paper pieces. The art sits in a plate on a sheet in the accent
            colour, beside the effect's own knobs with their defaults marked.

The type layer is HTML (posters_overlay.js renders it with Playwright); the art
is the effect's own output with the same finishing as the art pieces.
Fonts: Instrument Serif and IBM Plex (SIL OFL), fetched from npm on first run
into media/_source/.fonts.
"""
import json, os, subprocess, sys, tarfile, tempfile, glob
from multiprocessing import Pool
from PIL import Image
from finish import bloom, vignette, grain

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(HERE, '.fonts')
FONT_PACKAGES = ['@fontsource/instrument-serif@5.3.0', '@fontsource/ibm-plex-mono@5.3.0',
                 '@fontsource/ibm-plex-sans-condensed@5.3.0']
SHOTS = {s['name']: s for s in json.load(open(os.path.join(HERE, 'posters.json')))}

# name: number, title, description, layout, colours
PLATES = {
    'flow-field':    (12, 'Flow field', 'Particles carried by a slowly evolving noise field.', '#D8F3EC', '#2FB59A'),
    'curl-noise':    (13, 'Curl noise', 'Paths following the curl of a noise field.', '#EAF7F4', '#E08A4F'),
    'plasma':        (20, 'Plasma', 'Four interfering sine fields, drawn by a shader.', '#FFE3EC', '#FF3D7F'),
    'fireworks':     (26, 'Fireworks', 'Shells that burst into a fading shower.', '#FFF4E0', '#FF4D6D'),
    'constellation': (27, 'Constellation', 'Drifting nodes linked whenever they come close.', '#CFE3FF', '#4D8DFF'),
    'orbits':        (28, 'Orbits', 'Nested rings with one body on each.', '#F2F5E8', '#C8F03C'),
    'galaxy':        (36, 'Galaxy', 'A two-armed spiral turning faster towards the centre.', '#FFE3D6', '#FF7A59'),
    'dipole':        (39, 'Dipole', 'Field lines traced from one pole to the other.', '#D6FBFF', '#22D3EE'),
    'attractor':     (40, 'Attractor', 'The Lorenz attractor, plotted in x and z.', '#FFE0E2', '#FF3347'),
    'vortex':        (44, 'Vortex', 'A drain: particles spiral inwards, faster as they go.', '#DDE6FF', '#3D5AFE'),
    'phyllotaxis':   (54, 'Phyllotaxis', 'The sunflower spiral, with a pulse running outwards.', '#FFF1C9', '#FF9F1C'),
    'sparkler':      (60, 'Sparkler', 'A hot core throwing short-lived streaks.', '#FFF6E5', '#FF5A1F'),
    'arcs':          (61, 'Arcs', 'Ragged discharges arcing between two terminals.', '#E9E6FF', '#8A7CFF'),
}

# Knob marks are each default's position in its declared range; None is automatic.
COMMON = {'Count': None, 'Speed': 0.25, 'Size': None, 'Opacity': 1.0, 'Seed': 0.0}
SPECIMENS = {
    'metaballs': (23, 'Metaballs', '#FF5A36', '#1A2466', ['Count', 'Speed', 'Size', 'Opacity', 'Seed', ('Threshold', (1 - 0.4) / 2.1)]),
    'lissajous': (35, 'Lissajous', '#D9365E', '#3A0F2A', ['Count', 'Speed', 'Size', 'Opacity']),
    'halftone':  (46, 'Halftone', '#C8341F', '#161410', ['Count', 'Speed', 'Opacity']),
}

# Centred subjects move right to clear the title column; their edges are plain surface.
SHIFT = {'galaxy': 170, 'fireworks': 120, 'phyllotaxis': 120, 'orbits': 80, 'attractor': 60}

STILL, VIDEO = (1600, 2000), (1080, 1350)
FPS, HOLD = 30, 45


# ---------------------------------------------------------------- fonts

def ensure_fonts():
    if os.path.isdir(FONTS):
        return
    os.makedirs(FONTS)
    for package in FONT_PACKAGES:
        tgz = subprocess.check_output(['npm', 'pack', package, '--silent'], cwd=FONTS, text=True).strip().splitlines()[-1]
        with tarfile.open(os.path.join(FONTS, tgz)) as t:
            t.extractall(os.path.join(FONTS, tgz[:-4]), filter='data')
        os.remove(os.path.join(FONTS, tgz))


def font_css():
    def f(family, pattern, weight, style='normal'):
        path = glob.glob(os.path.join(FONTS, '*', 'package', 'files', pattern))[0]
        return f'@font-face {{ font-family: "{family}"; font-weight: {weight}; font-style: {style}; src: url("file://{path}"); }}'
    return '\n'.join([
        f('Instrument Serif', 'instrument-serif-latin-400-normal.woff2', 400),
        f('Instrument Serif', 'instrument-serif-latin-400-italic.woff2', 400, 'italic'),
        f('Plex Mono', 'ibm-plex-mono-latin-400-normal.woff2', 400),
        f('Plex Mono', 'ibm-plex-mono-latin-500-normal.woff2', 500),
        f('Plex Cond', 'ibm-plex-sans-condensed-latin-500-normal.woff2', 500),
        f('Plex Cond', 'ibm-plex-sans-condensed-latin-600-normal.woff2', 600),
    ])


# ---------------------------------------------------------------- layouts

BASE = '* { margin: 0; padding: 0; box-sizing: border-box; } html, body { width: 1600px; height: 2000px; overflow: hidden; background: transparent; } body { position: relative; }'


def exposure(name):
    shot = SHOTS[name]
    frames, exposed = shot['frames'], shot.get('exposure', 0)
    return frames, exposed


def plate_html(name, animated):
    number, title, desc, ink, accent = PLATES[name]
    surface = SHOTS[name]['surface']
    frames, exposed = exposure(name)
    if exposed:
        stat, label = f'{exposed / 60:.2f} s', 'Exposure'
        start = (frames - exposed) / frames
    else:
        stat, label = '1/60 s', 'Per frame'
        start = 0 if animated else (frames - 1) / frames
    return f'''<!doctype html><html><head><meta charset="utf-8"><style>{font_css()}
{BASE}
:root {{ --ink: {ink}; --accent: {accent}; --surface: {surface}; }}
#art {{ position: absolute; left: {SHIFT.get(name, 0)}px; top: 0; width: 1600px; height: 2000px; }}
.scrim {{ position: absolute; inset: 0; background:
  linear-gradient(90deg, color-mix(in srgb, var(--surface) 82%, transparent) 0, transparent 26%),
  linear-gradient(0deg, color-mix(in srgb, var(--surface) 92%, transparent) 0, transparent 24%),
  linear-gradient(180deg, color-mix(in srgb, var(--surface) 60%, transparent) 0, transparent 12%); }}
.brand {{ position: absolute; left: 80px; top: 72px; font: 400 18px "Plex Mono"; letter-spacing: .16em; text-transform: uppercase; color: var(--ink); opacity: .55; }}
.stat {{ position: absolute; right: 80px; top: 64px; text-align: right; color: var(--ink); }}
.stat .n {{ font: 400 96px/1 "Instrument Serif"; }}
.stat .k {{ font: 400 16px "Plex Mono"; letter-spacing: .14em; text-transform: uppercase; opacity: .55; margin-top: 10px; }}
.title {{ position: absolute; left: 62px; bottom: 360px; transform-origin: left bottom; transform: rotate(-90deg) translateY(100%);
  font: 400 238px/0.8 "Instrument Serif"; letter-spacing: -2px; white-space: nowrap; color: var(--ink); }}
.title i {{ color: var(--accent); font-style: normal; }}
.foot {{ position: absolute; left: 80px; right: 80px; bottom: 96px; }}
.desc {{ font: italic 400 44px/1 "Instrument Serif"; margin-bottom: 26px; color: var(--ink); }}
#track {{ position: relative; height: 10px; background: color-mix(in srgb, var(--ink) 22%, transparent); }}
#fill {{ position: absolute; top: 0; bottom: 0; left: {start * 100:.4f}%; right: 0; min-width: 4px;
  background: linear-gradient(90deg, color-mix(in srgb, var(--accent) 25%, transparent), var(--accent)); }}
</style>
<script>window.fit = () => {{ const t = document.querySelector('.title'); let s = 238;
  while (t.scrollWidth > 1440 && s > 120) {{ s -= 4; t.style.fontSize = s + 'px'; }} }};</script>
</head><body>
<div id="art"></div><div class="scrim"></div>
<div class="brand">Uno.Effects &nbsp;{number:02d}</div>
<div class="stat"><div class="n">{stat}</div><div class="k">{label}</div></div>
<div class="title">{title}<i>.</i></div>
<div class="foot"><div class="desc">{desc}</div><div id="track"><div id="fill"></div></div></div>
</body></html>'''


def specimen_html(name, animated):
    number, title, sheet, ink, knobs = SPECIMENS[name]
    rows = []
    for knob in knobs:
        label, mark = (knob, COMMON[knob]) if isinstance(knob, str) else knob
        cls, left = ('auto', 0) if mark is None else ('', mark * 100)
        rows.append(f'<div class="knob"><div class="name">{label}</div><div class="range"><i class="{cls}" style="left:{left:.2f}%"></i></div></div>')
    return f'''<!doctype html><html><head><meta charset="utf-8"><style>{font_css()}
{BASE}
:root {{ --sheet: {sheet}; --ink: {ink}; }}
body {{ color: var(--ink); }}
#art {{ position: absolute; left: 80px; top: 440px; width: 1020px; height: 1340px; box-shadow: 0 0 0 3000px var(--sheet); }}
#art::before, #art::after {{ content: ""; position: absolute; width: 36px; height: 36px; border: 2px solid var(--ink); opacity: .6; }}
#art::before {{ left: 22px; top: 22px; border-right: 0; border-bottom: 0; }}
#art::after {{ right: 22px; bottom: 22px; border-left: 0; border-top: 0; }}
.m {{ position: absolute; left: 80px; right: 80px; }}
.head {{ top: 72px; display: flex; justify-content: space-between; font: 500 18px "Plex Mono"; letter-spacing: .14em; text-transform: uppercase; padding-bottom: 16px; border-bottom: 2px solid var(--ink); }}
.title {{ top: 122px; font: 600 262px/1 "Plex Cond"; letter-spacing: -9px; }}
.col {{ position: absolute; left: 1150px; right: 80px; top: 440px; }}
.knob {{ padding: 0 0 46px; }}
.knob .name {{ font: 500 34px/1 "Plex Cond"; }}
.knob .range {{ position: relative; height: 14px; margin-top: 16px; border-left: 2px solid var(--ink); border-right: 2px solid var(--ink); }}
.knob .range::before {{ content: ""; position: absolute; left: 0; right: 0; top: 6px; height: 2px; background: var(--ink); }}
.knob .range i {{ position: absolute; top: -3px; width: 14px; height: 20px; margin-left: -7px; background: var(--ink); }}
.knob .range i.auto {{ background: var(--sheet); border: 2px solid var(--ink); }}
.foot {{ bottom: 70px; left: 1150px; font: 400 20px "Plex Mono"; }}
</style></head><body>
<div id="art"></div>
<div class="m head"><span>Uno.Effects</span><span>No. {number}</span></div>
<div class="m title">{title}</div>
<div class="col">{''.join(rows)}</div>
<div class="m foot">&lt;fx:{title} /&gt;</div>
</body></html>'''


def overlays(names, scale, animated, work):
    """Render every poster's type layer once; returns {name: (overlay, fill, geometry)}."""
    jobs = []
    for name in names:
        html = (plate_html if name in PLATES else specimen_html)(name, animated)
        path = os.path.join(work, name + '.html')
        open(path, 'w').write(html)
        jobs.append({'html': path, 'out': os.path.join(work, name), 'scale': scale})
    open(os.path.join(work, 'jobs.json'), 'w').write(json.dumps(jobs))
    subprocess.check_call(['node', os.path.join(HERE, 'posters_overlay.js'), os.path.join(work, 'jobs.json')])
    out = {}
    for name in names:
        p = os.path.join(work, name)
        geo = json.load(open(p + '.json'))
        fill = Image.open(p + '.fill.png').convert('RGBA') if geo['fill'] else None
        out[name] = (Image.open(p + '.overlay.png').convert('RGBA'), fill, geo)
    return out


# ---------------------------------------------------------------- composite

def finished(im, name, seed):
    if name in SPECIMENS:
        return grain(vignette(im, 0.10), 5.0, seed)
    return grain(vignette(bloom(im), 0.45), 4.0, seed)


def compose(name, art, layer, progress):
    """art: finished effect output; progress: how much of the bar's fill is lit, 0..1."""
    overlay, fill, geo = layer
    s = geo['scale']
    poster = Image.new('RGBA', overlay.size, SHOTS[name]['surface'])
    a = geo['art']
    box = (round(a['x'] * s), round(a['y'] * s), round((a['x'] + a['w']) * s), round((a['y'] + a['h']) * s))
    bw, bh = box[2] - box[0], box[3] - box[1]
    k = max(bw / art.width, bh / art.height)  # cover
    im = art.resize((round(art.width * k), round(art.height * k)), Image.LANCZOS)
    left, top = (im.width - bw) // 2, (im.height - bh) // 2
    poster.paste(im.crop((left, top, left + bw, top + bh)), box[:2])
    poster.alpha_composite(overlay)
    if fill is not None and progress > 0:
        f = geo['fill']
        x0, x1 = round(f['x'] * s), round((f['x'] + f['w']) * s)
        lit = fill.crop((0, 0, x0 + max(1, round((x1 - x0) * progress)), fill.height))
        poster.alpha_composite(lit, (0, 0))
    return poster.convert('RGB')


def stills(raw, out):
    os.makedirs(out, exist_ok=True)
    names = list(SHOTS)
    with tempfile.TemporaryDirectory() as work:
        layers = overlays(names, 1, False, work)
        for i, name in enumerate(names):
            art = Image.open(os.path.join(raw, name + '.png')).convert('RGB').resize(STILL, Image.LANCZOS)
            number = (PLATES.get(name) or SPECIMENS[name])[0]
            path = os.path.join(out, f'{number:02d}-{name}.jpg')
            compose(name, finished(art, name, 100 + i), layers[name], 1.0).save(path, quality=93, subsampling=0)
            print('still', path)


def timeline(name, frames):
    """Output frames as (sequence file, bar progress)."""
    if SHOTS[name].get('exposure', 0):
        # The exposure forming: 4-8 s whatever its length, then a hold on the result.
        n = round(min(8, max(4, len(frames) / 30)) * FPS)
        picks = [min(len(frames) - 1, (k + 1) * len(frames) // n) for k in range(n)]
        return [(frames[i], (i + 1) / len(frames)) for i in picks] + [(frames[-1], 1.0)] * HOLD
    return [(f, (k + 1) / len(frames)) for k, f in enumerate(frames)]


_LAYERS = None


def _init(layers):
    global _LAYERS
    _LAYERS = layers


def _frame(args):
    name, path, progress, seed = args
    art = Image.open(path).convert('RGB')
    return compose(name, finished(art, name, seed), _LAYERS[name], progress).tobytes()


def videos(seq, out, only):
    os.makedirs(out, exist_ok=True)
    names = [n for n in SHOTS if not only or n in only]
    with tempfile.TemporaryDirectory() as work:
        layers = overlays(names, VIDEO[0] / STILL[0], True, work)
        with Pool(4, initializer=_init, initargs=(layers,)) as pool:
            for name in names:
                frames = sorted(glob.glob(os.path.join(seq, name, '*.jpg')))
                plan = timeline(name, frames)
                number = (PLATES.get(name) or SPECIMENS[name])[0]
                path = os.path.join(out, f'{number:02d}-{name}.mp4')
                ff = subprocess.Popen(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                                       '-s', f'{VIDEO[0]}x{VIDEO[1]}', '-r', str(FPS), '-i', '-',
                                       '-c:v', 'libx264', '-preset', 'slow', '-crf', '21', '-pix_fmt', 'yuv420p',
                                       '-movflags', '+faststart', path], stdin=subprocess.PIPE)
                jobs = [(name, f, p, k) for k, (f, p) in enumerate(plan)]
                for data in pool.imap(_frame, jobs, chunksize=4):
                    ff.stdin.write(data)
                ff.stdin.close()
                ff.wait()
                print('video', path, f'{len(plan) / FPS:.1f} s')


if __name__ == '__main__':
    ensure_fonts()
    mode, src, dst = sys.argv[1:4]
    if mode == 'stills':
        stills(src, dst)
    else:
        videos(src, dst, set(sys.argv[4:]))
