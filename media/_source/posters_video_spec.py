"""Derive the animated posters' shot list from a poster shot list.

Usage: python posters_video_spec.py <posters.json> <out.json>

Same effects, palettes and seeds, rendered at 1080x1350 (4:5) as 10 s loops.
A shot with a long exposure becomes a moving one: the renderer's persistence
fades each frame out over the exposure's length, so the trails look like the
still's while the effect keeps going. Each shot warms up until that fade has
settled, then records 11 s at 30 fps; posters.py crossfades the extra second
into the start so the loop has no seam.
"""
import json, sys

VIDEO_W, FPS = 1080, 30
LOOP_SECONDS, CROSSFADE_SECONDS = 10, 1


def video_shot(shot):
    v = dict(shot)
    v['scale'] = VIDEO_W / shot['w']
    exposure = shot.get('exposure', 0)
    v['exposure'] = 0
    start = max(shot['frames'], round(exposure * 1.5), 120)
    recorded = (LOOP_SECONDS + CROSSFADE_SECONDS) * 60
    v['frames'] = start + recorded - 1
    v['sequence'] = {'from': start, 'every': 60 // FPS, 'persistence': exposure}
    return v


if __name__ == '__main__':
    shots = [video_shot(s) for s in json.load(open(sys.argv[1]))]
    json.dump(shots, open(sys.argv[2], 'w'), indent=1)
    print(len(shots), 'video shots ->', sys.argv[2])
