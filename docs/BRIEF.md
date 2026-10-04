# Build this game in Unity: Borrowed Seconds

The designer framing for this whole batch: each game should have **one mechanic that feels great immediately**, a premise you can explain in a sentence, and enough depth to make players say "one more run." It should feel like a complete indie game, and the first version must stay manageable in scope. The real test: does a five-minute play make someone ask to play again?

## The concept (from the designer)
**Solve compact puzzles by borrowing time from your future self.**

Freeze a moving obstacle for three seconds, but those seconds become a scheduled freeze for your character later. You must plan where you'll be when the debt comes due. A visible countdown makes the consequences predictable.

- **The fun:** Clever solutions where your future penalty becomes part of the plan.
- **The polish:** Ghost previews, clean geometric environments, and sharp time-distortion effects.
- **Buildable first version:** Twenty single-screen levels, one borrowing ability, and three obstacle types.
- **Trailer moment:** Your character freezes on a pressure plate at exactly the right moment.

The simulation must be fully deterministic (fixed timestep) so the ghost preview is truthful. Build a solver or replay validator that proves each level is solvable and that the intended "debt as a tool" trick is required where you designed it to be.

## How to work (applies to every game in this batch)

You're one of eight sessions building a separate game at the same time. Your game lives only in this project directory. Don't touch anything outside it.

**Goal:** a polished, complete-feeling indie game at AAA quality: juicy feedback, cohesive art direction, real audio, smooth UX, and no placeholder look in the final result. Treat the "buildable first version" scope above as the content target, and make the quality bar high.

**Tooling on this machine**
- Unity **6000.6.2f1** (licensed, Personal) at `~/Unity/Hub/Editor/6000.6.2f1/Editor/Unity`. Use URP. The `unity` CLI is installed (`unity --help`, `unity commands`). It can create projects, drive a running Editor, enter play mode, capture the game, and build. Use it.
- Blender **4.5 LTS** is on PATH as `blender`. Do **all 3D modeling in Blender**, scripted with `bpy` (for example `blender -b -P ArtSource/build_assets.py`). Keep `.blend` sources and generator scripts in `ArtSource/` and export FBX/GLB into `Assets/`. Render-check models from Blender and look at the renders before importing.
- Audio: there are no stock libraries. Synthesize SFX and music procedurally (for example Python and numpy to WAV, layered and enveloped). Don't use copyrighted assets.
- Reference projects from the same developer (read only, don't modify): `packthetrunk` (Unity 6.6 URP puzzle game) and `peggle` (Unity plus a Blender `ArtSource/` pipeline). Skim them for working pipeline patterns.

**Process**
1. **Plan first.** Write `docs/PLAN.md`. It should be a thorough game design and technical plan: one-sentence pitch, design pillars, core loop, mechanics in detail, every level/case/day/object enumerated with its purpose, difficulty curve, narrative beats, art direction (palette, shapes, lighting, camera), audio direction, UI/UX and controls, game feel and juice list, code architecture, asset list (Blender models), milestone plan, risks, and a "5-minute prototype test" describing what must make a player ask to play again. Then **go straight into building** without waiting for approval.
2. **Prototype the core mechanic first** and make it feel great before you produce content. Capture play-mode screenshots, look at them, and iterate.
3. Build out the full content scope, then polish: animation, particles, post-processing, transitions, audio mix, menus (title, pause, settings, level select), save/progress, and an onboarding flow that teaches without walls of text.
4. Verify by actually running the game. Use screenshots and captures, check the console for errors, and fix them. Whenever possible, add automated validation (for example a solver or test that proves every puzzle is solvable).
5. `git init` at the start, use a good Unity `.gitignore`, and commit at each milestone. Don't create GitHub repos or push.
6. Finish with a `README.md` (pitch, controls, rules, content, project layout, how to rebuild assets) and a Linux standalone build in `Builds/`.

**Shared-machine etiquette:** Seven other Unity and Blender sessions run on this machine at the same time (32 cores, 109 GB RAM). Prefer batch mode and headless Blender, close Editors you open when you finish with them, and never kill processes you didn't start.

Report honestly. If something isn't done, doesn't work, or is still placeholder, say so plainly in your updates and in the README.
