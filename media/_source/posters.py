"""Per-effect posters, still and animated, one palette each.

Usage:
  python posters.py stills <shots.json> <raw dir> <out dir> [names]       # 1600x2000 JPEGs
  python posters.py videos <shots.json> <sequence dir> <out dir> [names]  # 1080x1350 10 s loops

<shots.json> is a poster shot list (posters.json, posters-2.json). <raw dir> holds
its renders from `--art <shots.json>`; <sequence dir> holds the frame folders from
`--art` with the list posters_video_spec.py derives from it.

Each shot's "poster" block carries what the art does not: title, description,
colours, title font and offsets. The catalog number comes from Catalog.json.

Two layouts:
  plate     (default) The art fills the sheet. The top right gives the exposure;
            the bar under the caption is the run, lit where the exposure was
            taken. Animated, the exposure moves with the effect and the lit
            window slides along the bar, wrapping with the loop.
  specimen  The art sits in a plate on a sheet beside the effect's own knobs,
            each default marked in its range ("knobs": names, or [name, mark]).

The type layer is HTML (posters_overlay.js renders it with Playwright); the art
is the effect's own output with the same finishing as the art pieces. Fonts are
SIL OFL faces from Fontsource, fetched from npm on first use into
media/_source/.fonts: IBM Plex for the small type, Instrument Serif as the
default title face, and whatever a poster's "font" names.
"""
import json, os, subprocess, sys, tarfile, tempfile, glob
from multiprocessing import Pool
from PIL import Image
from finish import bloom, vignette, grain

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(HERE, '.fonts')
FONTSOURCE = '5.3.0'
CATALOG = os.path.join(HERE, '..', '..', 'samples', 'Gallery', 'Uno.Effects.Gallery', 'Catalog', 'Catalog.json')
NUMBERS = {e['id']: i + 1 for i, e in enumerate(json.load(open(CATALOG))['effects'])}

# A poster's "font" overrides any of these. caption: 'italic' or 'normal' in the title
# face, or 'mono' for faces that only work large; stat: the exposure figure's size.
DEFAULT_TITLE = {'package': 'instrument-serif', 'weight': 400, 'style': 'normal', 'tracking': -2, 'caption': 'italic', 'stat': 96}
# Knob marks are each default's position in its declared range; None is automatic.
COMMON = {'Count': None, 'Speed': 0.25, 'Size': None, 'Opacity': 1.0, 'Seed': 0.0}

STILL, VIDEO = (1600, 2000), (1080, 1350)
FPS, LOOP, CROSSFADE = 30, 300, 30  # 10 s loops; the extra second fades into the start


class Poster:
    def __init__(self, shot):
        p = shot['poster']
        self.name, self.shot = shot['name'], shot
        self.number = NUMBERS[shot['layers'][0]['id']]
        self.layout = p.get('layout', 'plate')
        self.title, self.description = p['title'], p.get('description', '')
        self.ink, self.accent, self.sheet = p['ink'], p.get('accent'), p.get('sheet')
        self.surface = shot['surface']
        font = p.get('font', {})
        self.font = {**DEFAULT_TITLE, 'caption': font.get('style', 'normal'), 'stat': 84, **font} if font else dict(DEFAULT_TITLE)
        self.shift = p.get('shift', [0, 0])
        self.zoom = p.get('zoom', 1)  # <1 shrinks art that runs to the edges inside the plain surface
        self.knobs = p.get('knobs', [])
        self.frames, self.exposure = shot['frames'], shot.get('exposure', 0)

    @property
    def slug(self):
        return f'{self.number:02d}-{self.name}'


def load(path):
    return [Poster(s) for s in json.load(open(path))]


# ---------------------------------------------------------------- fonts

def package_dir(package):
    return os.path.join(FONTS, f'fontsource-{package}-{FONTSOURCE}')


def ensure_fonts(posters):
    packages = {'ibm-plex-mono', 'ibm-plex-sans-condensed'} | {p.font['package'] for p in posters}
    os.makedirs(FONTS, exist_ok=True)
    for package in sorted(packages):
        if os.path.isdir(package_dir(package)):
            continue
        tgz = subprocess.check_output(['npm', 'pack', f'@fontsource/{package}@{FONTSOURCE}', '--silent'],
                                      cwd=FONTS, text=True).strip().splitlines()[-1]
        with tarfile.open(os.path.join(FONTS, tgz)) as t:
            t.extractall(package_dir(package), filter='data')
        os.remove(os.path.join(FONTS, tgz))


def face(family, package, weight, style='normal'):
    path = os.path.join(package_dir(package), 'package', 'files', f'{package}-latin-{weight}-{style}.woff2')
    if not os.path.isfile(path):
        sys.exit(f'{package} has no latin {weight} {style} face')
    return f'@font-face {{ font-family: "{family}"; font-weight: {weight}; font-style: {style}; src: url("file://{path}"); }}'


def font_css(poster):
    f = poster.font
    caption = [] if f['caption'] in ('mono', f['style']) else [face('Title', f['package'], f['weight'], f['caption'])]
    return '\n'.join(caption + [
        face('Title', f['package'], f['weight'], f['style']),
        face('Plex Mono', 'ibm-plex-mono', 400),
        face('Plex Mono', 'ibm-plex-mono', 500),
        face('Plex Cond', 'ibm-plex-sans-condensed', 500),
        face('Plex Cond', 'ibm-plex-sans-condensed', 600),
    ])


# ---------------------------------------------------------------- layouts

BASE = '* { margin: 0; padding: 0; box-sizing: border-box; } html, body { width: 1600px; height: 2000px; overflow: hidden; background: transparent; } body { position: relative; }'


def plate_html(p, animated):
    if p.exposure:
        stat, label = f'{p.exposure / 60:.2f} s', 'Exposure'
        start = (p.frames - p.exposure) / p.frames
    else:
        stat, label = '1/60 s', 'Per frame'
        start = (p.frames - 1) / p.frames
    if animated:
        start = 0  # the loop draws its own window from the whole gradient
    f = p.font
    caption = ('400 26px/1.2 "Plex Mono"' if f['caption'] == 'mono'
               else f'{f["caption"]} {f["weight"]} 44px/1 "Title"')
    return f'''<!doctype html><html><head><meta charset="utf-8"><style>{font_css(p)}
{BASE}
:root {{ --ink: {p.ink}; --accent: {p.accent}; --surface: {p.surface}; }}
#art {{ position: absolute; left: {p.shift[0] + 800 * (1 - p.zoom):.0f}px; top: {p.shift[1] + 1000 * (1 - p.zoom):.0f}px; width: {1600 * p.zoom:.0f}px; height: {2000 * p.zoom:.0f}px; }}
.scrim {{ position: absolute; inset: 0; background:
  linear-gradient(90deg, color-mix(in srgb, var(--surface) 82%, transparent) 0, transparent 26%),
  linear-gradient(0deg, color-mix(in srgb, var(--surface) 92%, transparent) 0, transparent 24%),
  linear-gradient(180deg, color-mix(in srgb, var(--surface) 60%, transparent) 0, transparent 12%); }}
.brand {{ position: absolute; left: 80px; top: 72px; font: 400 18px "Plex Mono"; letter-spacing: .16em; text-transform: uppercase; color: var(--ink); opacity: .55; }}
.stat {{ position: absolute; right: 80px; top: 64px; text-align: right; color: var(--ink); }}
.stat .n {{ font: {f['style']} {f['weight']} {f['stat']}px/1 "Title"; }}
.stat .k {{ font: 400 16px "Plex Mono"; letter-spacing: .14em; text-transform: uppercase; opacity: .55; margin-top: 10px; }}
.title {{ position: absolute; left: 62px; bottom: 360px; transform-origin: left bottom; transform: rotate(-90deg) translateY(100%);
  font: {f['style']} {f['weight']} 238px/0.8 "Title"; letter-spacing: {f['tracking']}px; white-space: nowrap; color: var(--ink); }}
.title i {{ color: var(--accent); font-style: inherit; }}
.foot {{ position: absolute; left: 80px; right: 80px; bottom: 96px; }}
.desc {{ font: {caption}; margin-bottom: 26px; color: var(--ink); }}
#track {{ position: relative; height: 10px; background: color-mix(in srgb, var(--ink) 22%, transparent); }}
#fill {{ position: absolute; top: 0; bottom: 0; left: {start * 100:.4f}%; right: 0; min-width: 4px;
  background: linear-gradient(90deg, color-mix(in srgb, var(--accent) 25%, transparent), var(--accent)); }}
</style>
<script>window.fit = () => {{
  // The title takes the column's height at most: 1440px of a 2000px sheet.
  const t = document.querySelector('.title'); let s = 238;
  while (t.scrollWidth > 1440 && s > 100) {{ s -= 4; t.style.fontSize = s + 'px'; }}
  // Display faces vary widely in width; keep the caption on one line.
  const d = document.querySelector('.desc'); let ds = parseFloat(getComputedStyle(d).fontSize);
  while (d.scrollWidth > d.clientWidth && ds > 20) {{ ds -= 1; d.style.fontSize = ds + 'px'; }}
}};</script>
</head><body>
<div id="art"></div><div class="scrim"></div>
<div class="brand">Uno.Effects &nbsp;{p.number:02d}</div>
<div class="stat"><div class="n">{stat}</div><div class="k">{label}</div></div>
<div class="title">{p.title}<i>.</i></div>
<div class="foot"><div class="desc" style="white-space:nowrap;overflow:hidden">{p.description}</div><div id="track"><div id="fill"></div></div></div>
</body></html>'''


def specimen_html(p, animated):
    rows = []
    for knob in p.knobs:
        label, mark = (knob, COMMON[knob]) if isinstance(knob, str) else knob
        cls, left = ('auto', 0) if mark is None else ('', mark * 100)
        rows.append(f'<div class="knob"><div class="name">{label}</div><div class="range"><i class="{cls}" style="left:{left:.2f}%"></i></div></div>')
    return f'''<!doctype html><html><head><meta charset="utf-8"><style>{font_css(p)}
{BASE}
:root {{ --sheet: {p.sheet}; --ink: {p.ink}; }}
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
<div class="m head"><span>Uno.Effects</span><span>No. {p.number}</span></div>
<div class="m title">{p.title}</div>
<div class="col">{''.join(rows)}</div>
<div class="m foot">&lt;fx:{p.title} /&gt;</div>
</body></html>'''


def overlays(posters, scale, animated, work):
    """Render every poster's type layer once; returns {name: (overlay, fill, geometry)}."""
    jobs = []
    for p in posters:
        html = (specimen_html if p.layout == 'specimen' else plate_html)(p, animated)
        path = os.path.join(work, p.name + '.html')
        open(path, 'w').write(html)
        jobs.append({'html': path, 'out': os.path.join(work, p.name), 'scale': scale})
    open(os.path.join(work, 'jobs.json'), 'w').write(json.dumps(jobs))
    subprocess.check_call(['node', os.path.join(HERE, 'posters_overlay.js'), os.path.join(work, 'jobs.json')])
    out = {}
    for p in posters:
        base = os.path.join(work, p.name)
        geo = json.load(open(base + '.json'))
        fill = Image.open(base + '.fill.png').convert('RGBA') if geo['fill'] else None
        out[p.name] = (Image.open(base + '.overlay.png').convert('RGBA'), fill, geo)
    return out


# ---------------------------------------------------------------- composite

def finished(im, p, seed):
    if p.layout == 'specimen':
        return grain(vignette(im, 0.10), 5.0, seed)
    return grain(vignette(bloom(im), 0.45), 4.0, seed)


def compose(p, art, layer, progress=1.0, window=None):
    """art: finished effect output. The bar shows either `progress`, how much of the
    fill is lit (0..1), or `window`, (head, length) as fractions of the track: the
    exposure sliding along the loop, bright at its head, wrapping at the end."""
    overlay, fill, geo = layer
    s = geo['scale']
    poster = Image.new('RGBA', overlay.size, p.surface)
    a = geo['art']
    box = (round(a['x'] * s), round(a['y'] * s), round((a['x'] + a['w']) * s), round((a['y'] + a['h']) * s))
    bw, bh = box[2] - box[0], box[3] - box[1]
    k = max(bw / art.width, bh / art.height)  # cover
    im = art.resize((round(art.width * k), round(art.height * k)), Image.LANCZOS)
    left, top = (im.width - bw) // 2, (im.height - bh) // 2
    poster.paste(im.crop((left, top, left + bw, top + bh)), box[:2])
    poster.alpha_composite(overlay)
    if fill is None:
        return poster.convert('RGB')
    f = geo['fill']
    x0, x1 = round(f['x'] * s), round((f['x'] + f['w']) * s)
    if window is None:
        if progress > 0:
            poster.alpha_composite(fill.crop((0, 0, x0 + max(1, round((x1 - x0) * progress)), fill.height)), (0, 0))
        return poster.convert('RGB')
    head, length = window
    width = x1 - x0
    seg = max(2, round(width * length))
    y0, y1 = round(f['y'] * s), round((f['y'] + f['h']) * s)
    strip = fill.crop((x0, y0, x1, y1)).resize((seg, y1 - y0), Image.LANCZOS)
    end = round(width * head)
    for offset in (end - seg, end - seg + width):  # the second copy is the wrapped part
        lo, hi = max(0, offset), min(width, offset + seg)
        if lo < hi:
            poster.alpha_composite(strip.crop((lo - offset, 0, hi - offset, y1 - y0)), (x0 + lo, y0))
    return poster.convert('RGB')


def stills(posters, raw, out):
    os.makedirs(out, exist_ok=True)
    with tempfile.TemporaryDirectory() as work:
        layers = overlays(posters, 1, False, work)
        for i, p in enumerate(posters):
            art = Image.open(os.path.join(raw, p.name + '.png')).convert('RGB').resize(STILL, Image.LANCZOS)
            path = os.path.join(out, p.slug + '.jpg')
            compose(p, finished(art, p, 100 + i), layers[p.name], 1.0).save(path, quality=93, subsampling=0)
            print('still', path)


def timeline(p, frames):
    """The loop: (frame, frame faded in over it, its weight, bar window) per output frame.

    The sequence runs one crossfade longer than the loop. Its tail fades into the
    first frames, so the last frame leads straight back into the first. The bar's
    window is the exposure (or one frame) at 60 fps against the loop's 600 frames.
    """
    if len(frames) < LOOP + CROSSFADE:
        sys.exit(f'{p.name}: {len(frames)} frames, need {LOOP + CROSSFADE}')
    length = min(1.0, max(p.exposure, 6) / (LOOP * 60 // FPS))
    plan = []
    for k in range(LOOP):
        tail = frames[LOOP + k] if k < CROSSFADE else None
        plan.append((frames[k], tail, 1 - (k + 0.5) / CROSSFADE if tail else 0, ((k + 1) / LOOP, length)))
    return plan


_STATE = None


def _init(state):
    global _STATE
    _STATE = state


def _frame(args):
    name, path, tail, weight, window, seed = args
    posters, layers = _STATE
    p = posters[name]
    art = Image.open(path).convert('RGB')
    if tail:
        art = Image.blend(art, Image.open(tail).convert('RGB'), weight)
    return compose(p, finished(art, p, seed), layers[name], window=window).tobytes()


def videos(posters, seq, out):
    os.makedirs(out, exist_ok=True)
    with tempfile.TemporaryDirectory() as work:
        layers = overlays(posters, VIDEO[0] / STILL[0], True, work)
        state = ({p.name: p for p in posters}, layers)
        with Pool(4, initializer=_init, initargs=(state,)) as pool:
            for p in posters:
                frames = sorted(glob.glob(os.path.join(seq, p.name, '*.jpg')))
                plan = timeline(p, frames)
                path = os.path.join(out, p.slug + '.mp4')
                ff = subprocess.Popen(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                                       '-s', f'{VIDEO[0]}x{VIDEO[1]}', '-r', str(FPS), '-i', '-',
                                       '-c:v', 'libx264', '-preset', 'slow', '-crf', '21', '-pix_fmt', 'yuv420p',
                                       '-movflags', '+faststart', path], stdin=subprocess.PIPE)
                jobs = [(p.name, f, tail, w, win, k) for k, (f, tail, w, win) in enumerate(plan)]
                for data in pool.imap(_frame, jobs, chunksize=4):
                    ff.stdin.write(data)
                ff.stdin.close()
                ff.wait()
                print('video', path, f'{len(plan) / FPS:.1f} s')


if __name__ == '__main__':
    mode, shots, src, dst = sys.argv[1:5]
    only = set(sys.argv[5:])
    posters = [p for p in load(shots) if not only or p.name in only]
    ensure_fonts(posters)
    (stills if mode == 'stills' else videos)(posters, src, dst)
