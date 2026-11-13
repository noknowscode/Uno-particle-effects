# CLAUDE.md

Project context lives in `README.md`, `HANDOFF.md` and `docs/session-log.md`.
Read `HANDOFF.md` first at the start of a session.

## Skills

### `/learn-taste`

Source: <https://aiforui.dev/skills/learn-taste> (from `emilkowalski/skills`)

Install:

```bash
npx skills@latest add emilkowalski/skills --skill learn-taste
```

What it does: it collects every design correction made during a session and
turns each one into a strict rule with its reason. It also catches corrections
that were never stated out loud, such as edits made directly to files, repeated
nudges and reverted changes. A fix to one component becomes a rule for every
similar component. When no reason was given, it proposes one and asks for
confirmation. It writes the rules to `DESIGN.md`, or suggests a lint/analyzer
rule when a check would catch the mistake more reliably.

When to use it:

- At the end of any session where the agent's UI or visual work was corrected
  more than once: gallery layout, property panel, effect tuning (colors,
  timing, density, easing), generated XAML/C#/SKSL output.
- `/learn-taste look at my last 3 sessions too` when the same fixes keep recurring.
- `/learn-taste prune` when `DESIGN.md` gets long.

Rules for this project:

- Read `DESIGN.md` before touching the gallery UI or any effect's visual
  defaults, and follow its rules. Ask before overriding one.
- Every rule in `DESIGN.md` carries its reason. Write no rule without one.
- Express rules in this project's terms: XAML resources and theme keys,
  `ParticleView` properties, effect parameters. The skill's examples are
  web/CSS (Tailwind shadow tokens, `.tsx` files), so translate them.
