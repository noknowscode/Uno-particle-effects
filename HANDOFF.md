# HANDOFF — Uno.Effects (particles library + gallery)
Updated: 2026-11-13

## Where we are

Implemented the Claude Design recommendation
(`Uno Effects Recommendation.dc.html`, project `7b2677f1-35c8-4329-ac51-392436f2222a`)
as a working Uno Platform solution. Phase 1 is done and overshot: all **64**
studies from `effects.js` are ported to C#/SkiaSharp behind a single
`ParticleView` control, with the gallery app (8×8 board, detail view, property
panel, generated XAML/C#/SKSL), a deterministic render-test harness, and a
packable `Uno.Effects.Particles` 0.1.0 NuGet carrying both heads. Since then:
a measured performance pass (worst frame 226 ms → 9.7 ms) and a fix for a
device-tier density the docs claimed but the code never implemented. In October:
a review pass (allocation, sizing, reversed gravity, frame-rate independence),
security pins, CI, and the merge into the public `Uno-particle-effects` repo.
In November: Uno.Sdk 7.0.5 (Uno Platform 7.0, SkiaSharp 4.151.1).

Full detail is in `README.md`; the reasoning, measurements and findings that
should go back to the recommendation are in `docs/session-log.md`.

## Last verified state

Re-verified 2026-11-13 in a Linux cloud session, after the move from Uno.Sdk
6.7.22 to 7.0.5.

- **Uno.Sdk 7.0.5:** the library and the test library list the `Skia` feature
  (Uno 7 ignores `SkiaRenderer` in a library, UNOB0030, and the build failed
  without SkiaSharp); the gallery head lists `Skia` too, which is what Uno 7
  maps `SkiaRenderer` to. SkiaSharp 4 paths are immutable, so the three effects
  that traced outlines (Flock, Sine path, Blob) build them in a reused
  `SKPathBuilder` and draw the detached path; Flock traces the whole flock as
  one path a frame (126 B/frame; one path per boid was 3.2 KB). The diagnostics
  text uses the `DrawText` overload with an explicit alignment.
- **Build:** pass. Whole solution, both TFMs, **zero warnings** with
  `-warnaserror`. The two security pins are gone: Uno.Sdk 7.0.5 no longer
  brings `Microsoft.Windows.Compatibility`, so neither package is in the graph.
- **Tests:** `--render-tests` exits 0 on Skia Desktop (Linux, Xvfb); all 64
  effects are deterministic and draw. **The hashes moved** with SkiaSharp 4's
  rasterizer: compared with 6.7.22 frame by frame, 49 of 192 frames are
  identical, a median 0.6% of pixels differ, and at most 0.08% of a frame
  (Halftone) differs by more than 32 levels in a channel: anti-aliasing at
  shape edges, no visible change. Hashes from before November are no baseline
  for cross-head comparison any more.
- **CI:** `.github/workflows/ci.yml` builds both heads, runs the render tests
  under Xvfb, packs the NuGet. Validated step by step locally; its first run on
  GitHub is still to come.
- **Runtime:** the gallery board and detail page were not re-run interactively
  this session; wide-frame and reversed-gravity fixes were checked by rendering
  before/after frames.
- **Packaging:** `dotnet pack` produces `Uno.Effects.Particles.0.1.0.nupkg` with
  `lib/net10.0-desktop` and `lib/net10.0-browserwasm`. Still no licence.

## Next actions (in order)

1. **LICENSE file.** The repo is now public; without a licence nobody may use
   the code or the package.
2. **Run `--render-tests` on the WebAssembly head and diff the hashes against
   desktop.** Every hash so far came from Skia Desktop, so "the same pixels on
   every target" is asserted, not demonstrated. Then add it to CI.
3. **Benchmark head + `matrix.json` + the platform strip on each detail page**
   (recommendation §1 and §5). The measurement plumbing already exists in
   `RenderTestRunner`; CI can publish the matrix once it exists.
4. **Amend §5's CI budget in the document.** 8 ms p95 on desktop does not hold
   for Metaballs on the CPU raster backend (measured 5–18 ms at 256×256).
5. **Add the remaining heads** (WinAppSDK, Android, iOS, Skia Desktop on
   macOS). This also settles the open decision about the Windows head.
6. **Copy-as-source (Tier 2)** — the zero-dependency tier in §2, unbuilt.
7. **Review findings not yet fixed** (all verified, none affects default
   settings on a square tile):
   - Grid effects (Wave, Halftone, Shimmer, PulseGrid, Static, Lattice,
     Filings, Repel) treat `Count` as columns, so Count 400 means 160k cells.
     Clamp the columns or give them their own range.
   - Metaballs silently caps `Count` at 8; the slider goes to 400.
   - Lightning bolts stop at ~860 px on tall views (`MaxSegments = 96`).
   - Size does not scale some outlines: Bokeh rim, Sparkle cross, Bubbles,
     Pollen. Recycle margins in Fog, Bubbles, Rain, Confetti and Leaves ignore
     Size, so large particles pop at the edge.
   - Gravity runs backwards in Dust and Embers (it speeds the rise).
   - Nebula draws one cloud in four in the primary ink (`(i % 4) + 1` wraps).
   - Frame 0 is wrong for Trail (head at 0,0) and Grain (solid black). Only
     visible with `WarmUpFrames="0"`; fixing it moves those two hashes.
   - Arcs, Grain and Static re-randomise every frame whatever `Speed` is; Ink's
     random walk scales by dt where it should scale by its square root.
   - `ReducedMotion` is read once per process, so changing the OS setting needs
     a restart.

Lower down: compare view, URL state, `Mask`, the `?effect=…&embed=1` docs route,
and the Phase 3 tooling work (Hot Design `.Design` assembly, Playground deep
links, Figma tag mapping). The attributes a Hot Design toolbox would read are
already in place.

## Open questions

- **§5 contradicts itself and the document should resolve it.** "The same pixels
  on every target" and "scale `Count` by a device-tier heuristic" cannot both
  hold for the live view. Current resolution: the render tests pin density to 1,
  so they compare the *effect* across heads while the tier applies to the
  *view*. §5 should say which it means.
- **Shader effects allocate one `SKShader` per frame.** The "no per-frame
  allocation" rule needs an explicit exemption or a reusable builder.
- **Determinism depends on the fixed timestep, not only the seed** — Fireflies
  and Flow field draw random numbers during `Step`. Worth stating plainly in §5,
  since it is what makes the cross-head hash comparison meaningful.
- **Windows head** (recommendation's own open decision): `SKCanvasElement` needs
  the Skia renderer, so WinAppSDK parity is unresolved. Item 4 answers it.
- **Board virtualization**: all 64 tiles are realized, ~28 MB of surfaces. The
  viewport check already stops off-screen clocks; an `ItemsRepeater` is the fix
  if the catalog grows well past 64.

## Relaunch

```
cd <your clone of Uno-particle-effects>

# build everything, both heads
dotnet build

# run the gallery
dotnet run --project samples/Gallery/Uno.Effects.Gallery -f net10.0-desktop

# open straight on one effect
samples\Gallery\Uno.Effects.Gallery\bin\Debug\net10.0-desktop\Uno.Effects.Gallery.exe --effect metaballs

# the test suite (exit code is the result)
samples\Gallery\Uno.Effects.Gallery\bin\Debug\net10.0-desktop\Uno.Effects.Gallery.exe --render-tests --output <dir>
```

Notes:

- **Kill the running app before building** — it locks
  `Uno.Effects.Particles.dll` and the build fails with MSB3021.
- Launch detached (`Start-Process`), not from a shell that exits; a backgrounded
  shell child gets torn down with the shell.
- Window automation on this desktop is flaky: the window intermittently
  minimizes or returns a torn/blank `PrintWindow` capture after a
  `SetWindowPos`. Restore with `ShowWindow(h, 9)`, let it settle, then capture.
  It is the automation, not the app.
- The design source is read through the `claude_design` MCP; the project is
  `7b2677f1-35c8-4329-ac51-392436f2222a`.
