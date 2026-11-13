"""Derive the animated posters' shot list from posters.json.

Usage: python posters_video_spec.py <posters.json> <out.json>

Same effects, palettes and seeds, rendered at 1080x1350 (4:5). A shot with a
long exposure writes every frame of its exposure window, so the video can show
the exposure forming. A shot without one writes 8 s of live frames at 30 fps.
"""
import json, sys

VIDEO_W, LIVE_SECONDS = 1080, 8


def video_shot(shot):
    v = dict(shot)
    v['scale'] = VIDEO_W / shot['w']
    exposure = shot.get('exposure', 0)
    if exposure:
        v['sequence'] = {'from': shot['frames'] - exposure + 1, 'every': 1}
    else:
        live = LIVE_SECONDS * 60
        v['frames'] = max(shot['frames'], live + 60)
        v['sequence'] = {'from': v['frames'] - live + 2, 'every': 2}
    return v


if __name__ == '__main__':
    shots = [video_shot(s) for s in json.load(open(sys.argv[1]))]
    json.dump(shots, open(sys.argv[2], 'w'), indent=1)
    print(len(shots), 'video shots ->', sys.argv[2])
