# Uno.Effects

Particle and shader effects for Uno Platform, drawn with SkiaSharp through a
single `ParticleView` control, plus the gallery app that shows them off, lets
you tune them, hands you the code, and tests them.

![Uno.Effects library poster](media/art/00-library-poster.jpg)

This is **Phase 1** of the Uno Effects design recommendation, carried through
to the full set: `ParticleView` plus all **64** studies from the sheet, the board and the detail view, Desktop and WebAssembly heads, and
deterministic render tests over every effect.

```
src/Uno.Effects.Particles/     the NuGet: ParticleView, ParticleEffect, all 64 effects, .sksl resources
samples/Gallery/               one Uno app; heads: Skia Desktop and WebAssembly
  Catalog/Catalog.json         which effects the gallery shows, and in what order
tests/Uno.Effects.RuntimeTests/ the deterministic render harness, run by the gallery itself
docs/session-log.md            build notes and the decisions taken along the way
media/                         art, gallery screenshots, a walkthrough recording, and the scripts that made them
```

## Using an effect

```xml
xmlns:fx="using:Uno.Effects.Particles"

<fx:ParticleView Interactive="True">
    <fx:Snow Count="120" Speed="0.8" />
</fx:ParticleView>
```

`ParticleView` is the only control. Effects are its content, so a view is one
element with its layers as children. Effects are `DependencyObject`s, so every
knob is bindable, animatable from a Storyboard or VisualState, and visible to a
designer property panel.

All 64 studies from the sheet are ported, in its own order, so the number under
each tile matches the original board. They are grouped by family, one file each:

| Family        | Count | Effects |
|---------------|-------|---------|
| `Atmosphere`  | 11    | Dust, Snow, Rain, Smoke, Fog, Confetti, Bubbles, Pollen, Ash, Sandstorm, Leaves |
| `Light`       | 15    | Fireflies, Sparks, Twinkle, Orbs, Embers, Lightning, Bokeh, Fireworks, Fountain, Burst, Sparkle, Nebula, Meteors, Sparkler, Arcs |
| `Paths`       | 15    | Starfield, Trail, Loop, SinePath, Constellation, Orbits, Wave, Lissajous, Galaxy, Helix, Cascade, Breath, Phyllotaxis, Cube, Sphere |
| `Fields`      | 10    | FlowField, CurlNoise, Flock, Gravity, Ripples, Dipole, Attractor, Vortex, Tornado, Ink |
| `Interactive` | 4     | Follow, Repel, Lattice, Filings |
| `Procedural`  | 9     | Plasma, Metaballs, Grain, Halftone, Aurora, Shimmer, Static, PulseGrid, Blob |

Plasma and Metaballs are SKSL runtime shaders; everything else draws with
`SKCanvas`. Effects that add their own knob say so on their detail page —
`Snow.Sway`, `Fireflies.BlinkRate`, `Follow.Attraction`, `Repel.Reach`,
`Metaballs.Threshold`, `Plasma.Cells`.

Every effect also carries the common knobs it honours — `Count`, `Speed`,
`Size`, `Opacity`, `Seed`, and where they mean something, `Direction` and
`Gravity`. `Count` and `Size` treat 0 as "the value this effect was designed
around", so the defaults are the tuned ones and a written-out value is always a
deliberate one.

`Palette` is a list of brushes rather than colours, so an effect can be handed
theme resources and will follow a light/dark switch without any code. Index 0 is
the primary ink, index 1 the accent, and two more follow for the effects that
scatter colour (Confetti, Nebula, Leaves).

`ParticleView.Density` scales every effect's natural count. It also treats 0 as
automatic, and automatic means a device tier: WebAssembly and mobile get less
budget than a desktop, and so do machines with few cores, floored at a third so
an effect never stops reading as itself. `EffectiveDensity` reports what it
resolved to. The render tests pin it to 1 — identical pixels across heads and a
per-device particle budget would otherwise contradict each other, so the tests
compare the effect and the tier applies to the live view.

## The gallery

```
dotnet run --project samples/Gallery/Uno.Effects.Gallery -f net10.0-desktop
```

- **Board** — every catalog effect drawn by the real `ParticleView`. Idle tiles
  hold a settled frame and cost nothing; hovering starts the clock. Chips filter
  by category, by shader-based, and by measured cost.
- **Detail** — full-size preview, a property panel built from each effect's
  declared ranges, a light/dark surface toggle that flips the palette, the
  diagnostics overlay, and the generated XAML, C# and SKSL.
- **Copy** — the generated markup writes only the knobs that differ from the
  effect's own defaults, so it stays as short as the snippet above however long
  the panel gets.

Two switches, both used by the tests and the docs embed:

```
--effect <id>                  open straight on that effect's detail page
--render-tests [--output DIR]  run the deterministic render tests and exit
```

## Testing

`--render-tests` drives every effect at a fixed 1/60 s timestep onto a 256x256
surface and hashes frames 0, 60 and 300, twice, comparing the runs. It writes the
frames as PNGs and reports median frame time. Each effect must also pass two
more checks, or the run fails:

- **Frame rate.** The effect runs the same 5 seconds again at 1/144 s (and at
  1/30 s), and the last frame's coverage (share of pixels visibly off the
  surface) and intensity are compared with the 60 fps frame. 144 fps must land
  within 25% of 60 fps. Pixels cannot match, since random numbers are drawn per
  step, so statistics stand in for them. 30 fps is reported but not asserted:
  there a trail is stamped once per two tuned steps and reads dimmer, which only
  sub-stepping would close.
- **Allocation budget.** Managed bytes allocated by `ParticleEffect.Render`
  alone, per frame after the pools exist, against
  `tests/Uno.Effects.RuntimeTests/AllocationBudgets.cs`. The budget is zero;
  the seven exceptions each carry their reason.

The harness lives in the app rather than beside it. That is deliberate: effects
are `DependencyObject`s, so they need a running Uno app, and running the tests
*inside* the gallery means one harness covers every head instead of a second,
headless implementation of the same thing. Comparing the printed hashes between
heads is the render-consistency test.

Measured on Skia Desktop (Windows, CPU raster), 256x256, all 64 effects:

| | median frame time | allocation per frame |
|---|---|---|
| 57 particle and CPU effects | 0.03 – 1.3 ms | 0 B |
| Sine path, Flock, Blob, Grain, Meteors | 0.03 – 0.8 ms | 88 – 208 B |
| Plasma (SKSL) | ~3 – 5 ms | 319 B |
| Metaballs (SKSL) | ~5 – 18 ms | 240 B |

Allocation figures are from November 2026 on Skia Desktop (Linux), measured
around `Render` alone; earlier figures of 10 – 230 B included the harness's own
hashing and fade paint. The five non-zero CPU effects each create one SkiaSharp
wrapper per frame (a detached `SKPath`, an `SKImage`, a gradient `SKShader`).
Frame times are from the original Windows measurement. The shader effects used to
allocate ~10 KB a frame, almost all of it from re-reading their embedded `.sksl`
resource on every draw.

CI (`.github/workflows/ci.yml`) builds both heads with warnings as errors and
runs the render tests on Skia Desktop under Xvfb on every pull request.

The particle effects hold the recommendation's budget with room to spare and
allocate essentially nothing per frame — the emitters keep fixed-capacity pools
and swap dead particles out rather than using a growable list, which is what
keeps that number flat.

**The shader effects do not**, and the reason is worth knowing before the CI
budgets in Phase 2 are written: on the CPU raster backend every SKSL
instruction is interpreted per pixel, and each frame still builds one `SKShader`.
Metaballs in particular sits at or above the recommendation's 8 ms desktop
budget at 256x256. Adding a `break` once the active blob count is passed cut it
roughly threefold; the rest is the backend. A GPU-backed surface, or evaluating
the field at a coarser resolution, is the Phase 2 fix. Until then Metaballs is
honestly labelled heavy.

## Performance

The per-effect numbers above are one half of the picture; the other half is what
a board of 64 live views costs, which is where the real problems were.

**Settling is time-boxed.** A newly emitted pool is settled over 90 frames so an
idle tile shows a real frame rather than a first tick. Doing all 90 in one go
made the worst tile (Metaballs) hold the UI thread for **226 ms**, and the board
as a whole did **2.5 seconds** of settling — twice, because layout runs at two
different tile widths before it comes to rest. `ParticleView` now spends at most
4 ms a frame settling and continues on the next frame. Measured on the same
board: **one** frame over 8 ms, worst **9.7 ms**. The rendered frames are
byte-identical before and after, which the render-test hashes confirm.

**Native objects are released deterministically.** `ParticleEffect` is
`IDisposable` and disposes its `SKPaint`; `ShaderEffect` also disposes its
compiled effect and builder; `ParticleView` disposes its surface, blit paint and
diagnostics objects when it unloads. Filtering the board discards 64 views at a
time, so waiting for finalization here is the difference between a steady
process and one that holds far more native memory than it is using.

**Colours are resolved once a frame, not at every read.** The surface colour and
each effect's palette are read from their brushes once per frame and cached for
the draw. Once a frame rather than once per change, so a theme switch or a
`ColorAnimation` that changes a brush in place is followed, without holding
callbacks on app-lifetime brushes that would keep views alive.

**Per-step constants follow the frame time.** Spawn counts and damping factors
were tuned per 1/60 s step. `TunedSteps`, `PerTunedStep` and `SpawnThisFrame`
rescale them to the real frame time, so an effect looks the same at 120 fps or at
`Speed="0.25"` as at 60 fps. At exactly 1/60 s they reduce to the constants, so
the fixed-timestep hashes did not move.

**Trails fade on a 60 Hz cadence.** `TrailFade` is the fraction faded per 1/60 s.
Applied once per frame, trails were 2.4 times longer at 30 fps than at 144 fps.
Rescaling the alpha every frame fixed the length but, at 144 fps, left the
per-frame alpha so small that 8-bit blending stopped short of the background and
left grey ghosts. `ParticleView` now carries the fraction between frames and
fades by whole steps only, as `SpawnThisFrame` does for spawns. The frame-rate
check in the render tests found this, and Starfield (streaks drawn from the
previous frame's position), Ink (a random walk scaled by dt instead of √dt) and
Arcs (rebuilt every frame) with it.

Still open, and deliberately so: the board realizes all 64 tiles rather than
virtualizing them, so every tile owns a surface whether or not it is scrolled
into view. At ~450 KB a tile that is around 28 MB, and the viewport check
already stops the clock for tiles that are off screen. An `ItemsRepeater` would
fix the memory too, and is the obvious next step if the catalog grows well past
64.

## Decisions and deviations from the recommendation

- **Catalog.json lists effects; attributes describe them.** The recommendation
  puts name, category, tags, cost and knobs in the catalog file. Those all live
  on the effect type instead, in `[Effect]` and `[EffectProperty]`, because the
  designer toolbox has to read the same metadata and two copies would drift.
  Catalog.json keeps what it alone knows: which effects the gallery shows and in
  what order. Adding effect 65 is still one C# class plus one catalog entry.
- **`ParticleView.Surface`, not `Background`.** `FrameworkElement.Background`
  already exists in Uno, and shadowing a base dependency property is a good way
  to get a binding that silently targets the wrong one.
- **Composition knobs.** `Blur`, `BlendMode` and `Paused` are implemented.
  `Mask` is not; it needs a brush-to-shader conversion that earns its place
  later rather than in Phase 1.
- **Windows head.** Not enabled. `SKCanvasElement` needs the Skia renderer, so
  the WinAppSDK head is exactly the open question the recommendation raises, and
  Phase 1 does not need to answer it.
- **Hot Design metadata** (the `.Design` assembly) is Phase 3. The attributes it
  would read are already in place.
- **One file per family, not per effect.** The recommendation says adding an
  effect is one C# class plus one catalog entry, and that still holds; sixty-four
  single-class files simply made the folder harder to read than six that group
  by category.
- **Square-tile assumptions.** Several studies sized their circles off the width
  alone, because the sheet was drawn on square tiles. Here they use the shorter
  side, and the grid effects derive their row count from the cell size. Both are
  identical on a square tile and stay inside the frame on a wide one.

## Media

[`media/`](media) holds art, screenshots and a walkthrough recording of the gallery, all
rendered by the library itself, plus the shot lists and scripts that regenerate them.
[`media/README.md`](media/README.md) explains how each image was made.
