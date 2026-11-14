# Session log

## 2026-09-04 — Framework guidance leak (benchmark note)

Per the global instruction to flag framework-naming guidance found in this
environment: the ambient harness guidance here **does** name specific
application frameworks. Specifically:

- The available-skills listing names Uno Platform, WinUI 3 / Windows App SDK,
  MAUI and WPF (`uno-platform-agent`, `winui:winui-dev`, `wpf-to-uno-migration`,
  and ~90 further `uno-platform:*` entries).
- Two MCP servers ship framework-specific instructions: `uno` (Uno Platform
  documentation) and `winui` / `claude_ai_Microsoft_Learn`.
- No `CLAUDE.md` exists in this workspace, so the "authoritative and complete"
  project instructions the global rules point at were absent.

That guidance was **not** used to choose the stack. The stack comes from the
task itself: the design project under implementation
(`Uno Effects Recommendation.dc.html`) specifies an Uno Platform library and
gallery app built on the Uno 6 Skia renderer and `SKCanvasElement`, and the
workspace is an empty directory named `Uno-particle-effects`. Had the task been
framework-neutral, the leaked guidance would have been ignored.

## Source of truth

The implementation follows the Claude Design project
`Particle Effects Grid Study` (7b2677f1-35c8-4329-ac51-392436f2222a):

- `Uno Effects Recommendation.dc.html` — the specification being implemented.
- `Particle Studies.dc.html` — the 8x8 grid design (layout, type, palette).
- `effects.js` — the 64 canvas studies the C# effects are ported from.

Scope agreed with the user: the recommendation's **Phase 1** — `ParticleView`
plus 8 effects spanning the six categories, gallery grid and detail view,
Desktop + WebAssembly heads, deterministic render tests.

## Build notes

Things that were only discoverable by running the app, in the order they bit.

- **`EffectiveViewportChangedEventArgs.BringIntoViewDistanceX` is not
  implemented in Uno** and throws. The viewport pause now reads
  `EffectiveViewport` instead, and treats "I cannot tell" as visible: a view
  that is wrongly believed to be off screen would silently never animate.
- **A failed SKSL compile was retried on every frame.** The guard compared
  against the cached builder rather than the cached source, so a shader that
  did not compile recompiled 60 times a second and the app looked like it had
  hung. The failure is now remembered, and reported.
- **`EffectKnob.Write` unified its conditional to `double`.**
  `IsInteger ? (int)Math.Round(value) : value` has type `double`, so
  `PropertyInfo.SetValue` threw for every int-typed knob. Every `Count` and
  `Seed` slider in the property panel would have thrown on first drag. Caught
  by printing generated code from the test run, not by reading the code.
- **A board tile needs a hit-testable surface.** The tile template started as a
  bare `ContentPresenter`; a `Button` with nothing to hit does not click.
- **Shader effects reported "1 particles"** in the diagnostics overlay, because
  the particle count clamps to a minimum of one. Effects now say whether they
  simulate particles at all.

## Findings worth taking back to the recommendation

- The CI budget in section 5 (p95 under 8 ms on desktop) is comfortable for the
  particle effects and **not currently met by Metaballs** on the CPU raster
  backend. See the measured table in the README. This is a property of
  interpreting SKSL per pixel without a GPU surface, not of the effect, so the
  budget should either be stated per backend or the shader effects should get a
  coarser evaluation path before the budget becomes a gate.
- Shader effects allocate one `SKShader` per frame. The "no per-frame
  allocation" rule holds for the particle effects and needs an explicit
  exemption, or a builder that can be reused across frames, for shader ones.
- Fixed-timestep determinism holds for effects that draw random numbers during
  `Step` (Fireflies and Flow field both do). Determinism therefore depends on
  the timestep being fixed, not merely on the seed — worth saying plainly in
  the testing section, since it is what makes the cross-head hash comparison
  meaningful.

## Completing the set

The remaining 56 studies were ported in one pass, grouped into six files by
family rather than one file per effect. Things worth recording:

- **The palette grew from two colours to four.** Confetti, Nebula and Leaves
  scatter across the sheet's ink, amber, sage and clay. `PaletteAt(index)`
  wraps within whatever palette is supplied, so a two-brush palette recolours
  them completely rather than falling back to colours the caller never asked
  for.
- **Emitters use fixed-capacity pools**, not growable lists, and remove a dead
  particle by swapping the last live one into its slot. Sparks, Fireworks,
  Fountain, Sparkler, Trail, Sparkle, Meteors and Ripples all work this way,
  which is what keeps their allocation-per-frame in the tens of bytes.
- **Randomness moved out of drawing.** Arcs and Static generated their picture
  with random numbers inside the draw call. Both now build that state in Step,
  so drawing a frame twice gives the same frame twice — the property the whole
  render-test suite depends on.
- **The square-tile assumption was everywhere.** The sheet was drawn on square
  tiles, so grid effects used the column count for both axes and circular
  effects sized off the width. Rows are now derived from the cell size and radii
  from the shorter side; identical on a square tile, correct on a wide one.
- **A blank-frame assertion was added to the suite.** Emitters legitimately draw
  nothing at frame 0, and several effects therefore shared a frame-0 hash. That
  meant a completely dead effect could have passed on matching blank frames, so
  each effect must now differ from a cleared surface on at least one sampled
  frame.

All 64 are deterministic and each draws something; 62 of them run in under
1.3 ms per frame at 256x256 with allocation in the tens to low hundreds of
bytes.

## Performance pass

Ran against the 64-tile board, measuring before changing anything.

**Measured first.** Instrumenting the settle step showed 128 warm-up events for
64 tiles — every tile settles twice, because layout runs at 152 px and then at
167 px before coming to rest — totalling 2,478 ms, with a single worst frame of
225.8 ms (Metaballs) holding the UI thread on its own.

**Fixed.** Settling is now time-boxed to 4 ms per frame and continues on the
next frame, requesting one if the clock is stopped, which it is for every idle
tile. Re-measured on the same board: one frame over 8 ms, worst 9.7 ms. That is
the 226 ms hitch gone and the ~1.2 s per-layout stall spread out.

**Verified no pixels moved.** The render suite produces byte-identical hashes
for all 64 effects before and after. This is the payoff for having built the
determinism harness first: a performance change to the frame loop could be
proved behaviour-neutral rather than eyeballed.

Also, from the skill's SkiaSharp guidance:

- `ParticleEffect` is now `IDisposable` and releases its `SKPaint`;
  `ShaderEffect` also releases its compiled effect and builder; `ParticleView`
  releases its blit paint and diagnostics font, not just its surface. The
  gallery disposes the effect it replaces on reset and on navigating away.
- The surface colour was being re-read from a dependency property and
  re-converted from a brush twice per frame per view. It is now resolved when it
  changes.

**Not done, deliberately.** The board realizes all 64 tiles instead of
virtualizing with an `ItemsRepeater`. That is roughly 28 MB of surfaces for
tiles that may be off screen; the viewport check already stops their clocks, and
the memory only starts to matter well past 64 effects.

## Device-tier density

The recommendation lists "scale `Count` by a device-tier heuristic unless
overridden" among the behavioural rules to hold everywhere. `ParticleView`
documented exactly that — and did not do it. The property defaulted to 1.0 and
nothing ever computed a tier, so the XML doc, and the README that repeated it,
were both claiming behaviour the code did not have.

Now implemented:

- `Density` defaults to 0, meaning automatic, following the same convention as
  `Count` and `Size`, so an explicit 1 is a real instruction rather than an
  indistinguishable default. `EffectiveDensity` reports what it resolved to.
- `DeviceTier` estimates the budget once: WebAssembly ×0.6 (it rasterizes on one
  thread, shared with the page), Android and iOS ×0.75, and a further ×0.6 or
  ×0.8 for machines with two or four cores, floored at a third. Coarse and
  deliberately so — it is a budget, not a benchmark.
- The suite now asserts it: the count tracks the density it is handed, and an
  explicit `Count` still wins. This is the one behavioural rule that is
  invisible in a screenshot, so it needed an assertion rather than a look.

**Worth taking back to the document:** this rule and the render-consistency rule
in §5 are in tension. Identical pixels on every head and a per-device particle
budget cannot both hold for the live view. The resolution here is that the tests
pin the density to 1, so they compare the *effect* across heads, while the tier
applies to the *view*. §5 should say which of the two it means.

All 64 render hashes are unchanged, and this machine (16 cores, desktop)
resolves to 1.00x, so nothing on the board moved.

## Frame-rate independence and allocation budgets

Idea borrowed from MonacoLabs.Presto's particle tests (ideas only, no code):
run the same simulation at different step sizes and assert the same result, and
fail on allocation instead of reporting it.

**How it is measured.** Different frame rates cannot produce the same pixels,
because random numbers are drawn per step. So the suite compares statistics of
the frame at t = 5 s: coverage (share of pixels more than 24 summed RGB levels
off the surface) and mean intensity. Calibrated on all 64 effects before any
fix, the time-driven effects (shaders, grids, projections) matched to three
decimals; every effect that drifted was a trail effect, plus Starfield.

**What it found.**

- `ParticleView.FadeSurface` faded by `TrailFade` once per frame, so trail
  length depended on the frame rate: coverage for Vortex was 0.096 / 0.063 /
  0.036 at 30 / 60 / 144 fps. Scaling the alpha by the frame time fixed 30 fps
  and broke 144 fps the other way: the per-frame alpha got so small that 8-bit
  SrcOver stopped short of the background (coverage 0.68 at 144 fps, grey
  ghosts). The fix keeps a 60 Hz cadence: the fraction carries between frames
  and the fade applies whole steps only. Vortex is now 0.055 / 0.063 / 0.069.
- Starfield drew each streak from the previous frame's depth, so streaks were
  2.4 times longer at 60 fps than at 144 fps. It now draws one tuned step of
  travel.
- Ink's random walk scaled by dt; diffusion scales by √dt. Arcs rebuilt every
  frame, stacking 2.4 times more arcs into its trail at 144 fps. Both were
  already in the review findings, and they were the only two effects outside
  25% at 144 fps once the fade was fixed.

**What it does not assert.** 30 fps is printed, not checked. Below 60 fps a
particle is stamped once per two fade steps, so trails are dotted and dimmer
(intensity down by up to half). That is sampling, not a fade bug; sub-stepping
would close it.

**Allocation.** The old figure counted the harness's own hashing, PNG writes
and per-frame fade paint, which is why nearly every effect showed ~11 B a frame.
Measured around `Render` alone, 57 effects allocate nothing; the seven that do
each create one SkiaSharp wrapper a frame and are listed with their reasons in
`AllocationBudgets.cs`.

**A small surprise worth knowing:** fading a freshly cleared surface towards
its own colour is not a no-op in 8-bit; a few pixels shift by one level. Frame 0
no longer fades (no time has passed), which moved the frame-0 hash of every
trail effect.

