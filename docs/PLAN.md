# Borrowed Seconds: Game Design & Technical Plan

## 1. Pitch

**Freeze any obstacle for three seconds, but you have to pay those seconds back: a few seconds later your own body freezes for three seconds wherever it happens to be.**

You plan where your future self will be standing when the debt comes due, and the best solutions turn that penalty into the answer.

## 2. Design pillars

1. **One verb, felt instantly.** Borrowing is a single click on a moving thing. It responds with a hard *snap*: a hit-stop, a crystal shell, a cut in the sound. You understand it in the first five seconds.
2. **The debt is a plan, not a punishment.** Every level is built around *where you'll be* when you freeze. The aha is when the freeze itself becomes the solution: you're frozen on a time-lock as a hazard passes through you.
3. **Truthful time.** The simulation is integer-only and runs on a fixed 20 Hz tick. Ghost previews come from the real simulation, so what you see is what will happen. A solver proves every level can be solved, and that the intended trick is required where it was designed to be.
4. **Clean diorama.** Each level is a compact porcelain board floating in a midnight void. It's readable at a glance, and every hazard has a distinct shape and a telegraph.
5. **Zero-friction retries.** Rewind is instant (hold **Z**), death auto-rewinds, restart is one key, and levels take 20–60 seconds. Every level has a solver "par" time to chase.

## 3. Core loop

*Moment to moment (seconds):* Read the board, then move tile to tile, dodging the cycling hazards. Aim at an obstacle to see the ghost preview, then borrow. The obstacle snaps into a crystal, your countdown starts, and you rush and position yourself. You freeze and the world keeps moving: did you plan it right? Then you thaw and carry on.

*Level (20–60 s):* Work out what each hazard does and find the place where a freeze (theirs or yours) breaks the pattern. Execute, rewinding freely. Reach the exit with your debts settled.

*Meta (minutes):* Medals against the solver's par time unlock the next level, then the next chapter card. Five-level chapters each introduce one new idea and end on a capstone. Total running time is about 25–40 minutes for a first clear, and longer if you chase gold medals.

## 4. Rules in detail

### 4.1 Time

- The simulation runs at **20 ticks per second** with integer state only (no floats, no unordered containers, no randomness). Rendering interpolates between the previous and current tick.
- The **loan** is a 3.0 s (60 tick) freeze of the target obstacle.
- The **term** is how long until the debt is due. Each level sets its own (default 5.0 s = 100 ticks), and the HUD shows it on the pocket watch.
- The **debt** freezes the player for 3.0 s (60 ticks), starting when the countdown reaches zero.
- **Only one loan at a time.** You can't borrow while your countdown is running or while you're frozen. After you thaw you can borrow again. Some levels cap total loans; the HUD shows them as pips.

### 4.2 The player

- Moves on a grid, one tile per **3 ticks (0.15 s)**. Input is buffered one move ahead, and holding a direction walks continuously.
- Can't enter walls, void, closed gates, solid objects (sliders, rotor pivots, anything frozen), or tiles with an active hazard (a lit beam or a resting rotor arm). Trying to do so plays a soft bump.
- On the first tick of a move the player occupies both the origin and the destination tile. After that, only the destination.
- **Dies** when a non-frozen hazard overlaps a tile the player occupies at the end of a tick, for example a slider moving onto them or a beam switching on. Death shatters the player, briefly slows time, then **auto-rewinds 2 seconds**.
- Borrowing requires the player to be standing still. A click during a step is buffered and fires the moment they land, at most 2 ticks later.
- **If the debt comes due mid-step**, the player freezes on landing.

### 4.3 Frozen things ("out of time")

- **Frozen obstacles** stop dead and become solid crystal: harmless, but they block the player, sliders and beams. A laser frozen while lit leaves a solid crystal bar; frozen while dark, the lane is open. A frozen slider or rotor is a crystal wall.
- **The frozen player is phased out of time.** They are immune to every hazard, and hazards pass straight through them. They don't block anything, **but their weight still holds plates and time-locks.**
- **Thaw is the dangerous moment.** If a hazard occupies your tile on the tick you thaw, you die. Planning a debt means planning a safe *thaw*.

### 4.4 Obstacles (the three types)

| Obstacle | Motion grammar | Hazard | Frozen behaviour | Notes |
|---|---|---|---|---|
| **Slider** (graphite cube, coral edges) | Translates along a track (ping-pong or loop), *n* ticks per tile, optional dwell at the ends | The tiles it overlaps (both tiles mid-move) | Solid crystal block | Presses plates and locks. Reverses when blocked by a solid. |
| **Laser** (wall turret) | Toggles: on/off cycle with phase, or always on; can be disabled by a plate | The beam's tiles until the first solid | Lit: crystal bar (solid). Dark: lane stays open | Telegraphs with a flickering charge line for 10 ticks before firing. |
| **Rotor** (pivot drum with 1, 2 or 4 arms) | Rotates 90° steps (*turn* ticks sweeping, *hold* ticks resting) | Arm rays when resting; the swept wedge while turning | Arms become crystal walls | Reverses when its next sweep is blocked by a solid. |

### 4.5 Devices

- **Plate** (`a`–`e`): pressed by the player (moving, standing or frozen) or by a slider (moving or frozen).
- **Gate** (`A`–`E`): open while any plate of the same letter is pressed. It stays open while something stands in it, so it never crushes.
- **Laser link:** a laser can be configured to go dark while a plate is held.
- **Time-lock** (`L`): needs **3.0 s of continuous pressure** to latch, and latches permanently. Its 12 dial segments fill as it charges and reset instantly if pressure lifts. The exit is sealed until every lock is latched.
- **Exit** (`E`): win by standing on it while it's unsealed **with no outstanding debt**. It reads "settle up before you leave": if you arrive in debt, you pay on the exit tile and leave as you thaw.

### 4.6 Tick order (deterministic)

1. Apply the player action (borrow / start a step) if the player is idle and not frozen.
2. Obstacles advance (frozen ones count down instead).
3. The player's step advances, and they land if it's complete.
4. Debt countdown and freeze timers; a due debt freezes the player now, or on landing.
5. Plates, then gates, time-locks and laser links.
6. Beams are traced against the current solids.
7. Hazard check (death), then the win check.

## 5. Ghost previews and readability

The simulation is cheap and deterministic, so previews come from cloning the state and running it forward:

- **Aim preview:** hover or aim at an obstacle to show a crystal ghost of it frozen in place, the 3.0 s ring, and a timeline under the pocket watch (*now → it thaws → you freeze → you thaw*).
- **Thaw forecast:** while your countdown runs, translucent **red echoes** show where every hazard will be on your thaw tick, and **cyan echoes** show where they'll be when you freeze. The tile under you shows ✓ (safe to thaw here) or ✗. The frozen player never blocks anything, so the forecast is exact unless you press a plate that changes the world.
- **Telegraphs:** slider chevrons point the way they're travelling, lasers flicker before firing, and rotors draw a faint arc ahead of their next sweep.
- **Focus** (hold **Shift** / **RMB** / **LT**): time slows to 20% for precise borrows. You can't move while focusing; it's for aiming, not dodging.

## 6. Content: 20 levels in 4 chapters

Each level is a single screen (at most 15×9 tiles). The geometry is iterated against the solver; this table is the design intent, and the validation target each level must satisfy.
*B* = "borrowing required" (unsolvable with no loans). *D* = "debt-as-tool required" (unsolvable even with unlimited, debt-free loans, so the player's own freeze is essential).

### Chapter I: PRINCIPAL. *"Every second you take, you give back."* (Sliders)
| # | Name | Purpose / new idea | Intended solution | Proof |
|---|---|---|---|---|
| 1 | First Loan | Teach moving, aiming, borrowing; see the debt freeze harmlessly | A fast slider orbits a ring corridor you must cross; freeze it off your arc, walk through, freeze in the quiet far room | B |
| 2 | Due Date | Thawing in a hazard kills | After the first crossing a second lane follows; dawdle and you freeze in it. Stop in the pocket between lanes, or time the cross so your thaw is clear | B |
| 3 | Grace Period | One loan at a time | Two separate crossings; settle the first debt in the middle room before taking the second loan | B (≥2 loans) |
| 4 | Collateral | Time-locks: 3 s of pressure | A sealed exit; the lock sits behind a slider crossing; borrow to cross, then simply stand on it | B |
| 5 | Exactly Now | **The aha.** Freeze *on* the lock | The lock sits in a slider's lane and the slider returns every 2 s, so you can't stand on it for 3. Borrow to reach it so your debt lands on the lock; the slider passes through you; thaw clear | B, D |

### Chapter II: INTEREST. *"Light keeps no promises."* (Lasers, plates, gates)
| # | Name | Purpose / new idea | Intended solution | Proof |
|---|---|---|---|---|
| 6 | Blink | Lasers: cycles, telegraphs, beam lanes | Hop across blinking beams on timing, then walk *along* a beam lane by freezing it dark | B |
| 7 | Counterweight | Plates & gates; sliders press plates | Freeze the slider while it's on the plate so the gate stays open for 3 s | B |
| 8 | Crystal Bar | A frozen lit beam is a wall | Freeze the laser while lit; its crystal bar bounces a slider back early, opening your path | B |
| 9 | Hold Still | Freeze on a lock inside a beam | The lock is in a beam lit 2 s of every 3; freeze on it while lit and thaw in the dark | B, D |
| 10 | Fine Print | Short term (3 s): the debt lands as the obstacle thaws | Capstone of slider, lasers and lock, with tight placement of the payment | B, D |

### Chapter III: MOMENTUM. *"What goes around comes around."* (Rotors)
| # | Name | Purpose / new idea | Intended solution | Proof |
|---|---|---|---|---|
| 11 | Turnstile | Rotors: sweeps, holds, reversal | Freeze a two-arm turnstile with its arms along the walls and slip through | B |
| 12 | Clockwork | Rotor arms pass through a frozen you | The lock lies under a rotor's sweep; freeze on it as the arms come round | B, D |
| 13 | Out of Phase | A freeze permanently shifts an obstacle's timing | Two rotors in sync never leave a gap; freeze one to desync them for good | B |
| 14 | Pass-Through | The debt as a dodge | A dead-end corridor swept by a slider with no alcove; borrow on something else so you're frozen exactly as it passes through you | B, D |
| 15 | Escapement | Capstone of rotor, slider, plate and lock | Two-phase plan: a plate-held gate, then a lock under a sweep | B, D |

### Chapter IV: COMPOUND. *"Pay it all back."* (Everything)
| # | Name | Purpose / new idea | Intended solution | Proof |
|---|---|---|---|---|
| 16 | Double Entry | One loan pays twice | Freeze a slider on lock A (its crystal charges it) while your debt lands you on lock B | B, D |
| 17 | Refinance | Chained loans | Three loans in sequence; each repayment spot is the setup for the next | B |
| 18 | Leverage | Crystal-bar walls + plates + rotor | A frozen beam steers a slider onto a plate that opens the rotor room | B |
| 19 | Long Term | Long term (8 s): plan across the whole board | Borrow early; the debt lands on a lock at the far side after a run through two hazards | B, D |
| 20 | Settlement | Finale: two locks, all obstacles | Choreographed run ending frozen on the final lock as every hazard converges on you. Thaw, the vault opens, dawn | B, D |

**Difficulty curve:** a single new idea per level within a chapter. Levels 1–3 are under 30 s and gentle, and 5 is the first aha (within roughly 5 minutes of starting). The middle chapters alternate "learn" and "twist" levels. Chapter capstones combine two ideas. The finale combines four. Solver margins (§10) make sure timing demands stay humane.

## 7. Narrative beats

The game is wordless apart from chapter cards. You are a small porcelain courier locked inside **the Escapement**, a vault where time is currency. Each chapter card is a "statement" from the vault's Teller, one line long (the epigraphs above). Death reads *"Defaulted."* and rewinds. The finale ends as you settle the last debt: the vault doors open onto warm dawn light and a closing card reads *"Account settled. Time well spent."* The credits follow, then your total time against the solver's total.

## 8. Art direction

- **Look:** a board-game diorama in the style of *Hitman GO* / *Monument Valley*, with clean geometry and soft light. One porcelain board floats in a deep indigo void above slowly turning, faintly glowing clock-face rings (parallax, time motif).
- **Palette:**
  - Void: `#0D1020` → `#1C2142` gradient.
  - Floor porcelain: `#ECE6DA` / `#E1D9CB` (subtle checker).
  - Walls: slate `#323A5C`, top `#4A5582`.
  - Brass trim: `#C9A15A`.
  - Player: porcelain with an **amber** core `#FFB547`.
  - **Hazards: coral** `#FF4F64` on graphite `#20243A`.
  - **Frozen/borrowed time: ice cyan** `#7CF4FF`.
  - Plates and gates: **mint** `#55E0AE`.
  - Locks and exit: **gold** `#FFD27A`.
- **Shapes:**
  - Player: a rounded pawn with dot eyes and a glowing ring.
  - Sliders: beveled cubes with a chevron.
  - Lasers: squat turrets.
  - Rotors: drum and bars.
  - Plates: inset squares.
  - Locks: round dials with 12 ticks.
  - Exit: an arch with an hourglass.
  - Everything is beveled, nothing is noisy.
- **Lighting:** a warm key directional (`#FFE6C4`) from upper left with soft shadows, a cool ambient sky, and emissive accents with bloom.
- **Camera:** perspective, FOV about 30°, pitched about 58° with no yaw (up on the keyboard means up on screen), framed to each board. It has a gentle drift and gives small punch-ins on borrows and freezes.
- **Post:** ACES tonemapping, bloom, vignette and light color grading. A custom full-screen **time-distortion pass** adds a radial ripple and chromatic split on borrow, a cool tint and a heartbeat pulse while you're frozen, and scanline smear and desaturation while rewinding.
- **Juice list:**
  - Squash-and-stretch hops, dust puffs and footstep pitch variation.
  - Borrow: an 80 ms hit-stop, a cyan tether from the player to the target, the crystal shell growing with a crack, a camera punch and a ripple.
  - A countdown ring above the head that pulses in the last second, with accelerating ticks.
  - Freeze: the player glasses over, the world's audio is muffled by a low-pass, and the screen tints cyan.
  - Thaw: shard burst, whoosh and audio release.
  - Death: shatter, red chromatic split, slow-mo, then rewind.
  - Lock: segments tick on with rising notes, then a latch chord and a light pillar to the exit.
  - Win: exit bloom, camera push and a medal stamp.

## 9. Audio direction

Everything is synthesized procedurally in Python with numpy (Blender's bundled interpreter) and written to WAV.

- **SFX:**
  - Footsteps: soft porcelain taps with random pitch.
  - Bump.
  - Borrow: rising shimmer, glass crack and sub-thump.
  - Freeze-encase, then thaw-shatter.
  - Debt warning ticks at three rates.
  - Slider hum and bounce thunk.
  - Laser charge whine, zap and buzz.
  - Rotor whoosh and clank.
  - Plate click and chime, gate slide.
  - Lock segment notes (pentatonic, rising) and latch chord.
  - Exit open sparkle, level-complete arpeggio, medal stamp.
  - Death shatter with reverse cymbal, rewind tape-whoosh.
  - UI hover, click and back.
- **Music:** four seamless loops at 96 BPM in a "clockwork ambient" style: soft pads, kalimba/marimba arpeggios, ticking percussion and sub bass. One loop for the title, one each for chapters I–II and III, and a fuller finale loop. Music ducks under big SFX, gets a 600 Hz low-pass and slight pitch drop while you're frozen ("out of time"), and has a lighter low-pass in Focus. Rewind plays the reversed stinger.

## 10. Validation: solver and replays

- `Assets/Scripts/Sim/` is **pure C#**, with no UnityEngine dependency. The game, the ghost previews, the solver and the tests all run this exact code.
- **Solver** (`Tools/Solver/`, a .NET 8 console app compiled with Unity's bundled SDK): an exact breadth-first search over ticks (a Dial bucket queue). Moves cost 3 ticks; waiting and borrowing are per tick. It reports:
  1. **Solvable**, plus the optimal solution, which sets the **par time** and is saved as a replay.
  2. **No-borrow variant** exhausted as **unsolvable** (proves *B*).
  3. **Forgiven-debt variant** exhausted as **unsolvable**. This variant allows unlimited debt-free loans and is a strict relaxation in every respect *except* the player's own freeze, so unsolvability proves *D*: the debt is needed as a tool.
  4. **Safety margin**: the solver is rerun treating every hazard as occupying its tiles for ±*k* ticks. The largest *k* that remains solvable is the timing slack a human gets; the target is *k* ≥ 2 (±100 ms) on every level.
- **Replay test:** saved solutions are replayed through the sim inside Unity (Mono runtime) as an EditMode test, to confirm cross-runtime determinism.
- **In-game autoplay** (`-bsAutoplay <dir>`) plays each solution in the *real* game loop in the built player and saves screenshots, to check views, FX and the win flow end to end.

## 11. UI / UX and controls

| Action | Keyboard + mouse | Gamepad |
|---|---|---|
| Move | WASD / arrows | Left stick / D-pad |
| Aim / preview | Hover an obstacle (or **Tab / Q / E** to cycle) | LB / RB cycle |
| Borrow | Left click (or **Space** on the aimed target) | A |
| Focus (slow time for aiming) | Hold Shift or RMB | Hold LT |
| Rewind | Hold **Z** / Backspace | Hold X |
| Restart | R | Y |
| Pause | Esc | Start |

- **Title:** a live diorama in the background (an attract-mode replay of a level's solution), logo with a ticking second hand. Menu: Continue / Levels / Settings / Quit.
- **Level select:** four chapter rows of five, with medal and best time per level; levels unlock one at a time.
- **HUD:** level number and name top left; run time and par top right; **pocket watch** bottom centre showing READY, the countdown, or the remaining freeze, plus the term and loan pips.
- **Onboarding:** no text walls. Contextual key glyphs float in the world on the first levels ("WASD", "hover", "click", "Z") and fade after use. Level 1 is built so the only thing to try is the slider.
- **Pause:** Resume / Restart / Levels / Settings / Title.
- **Settings:** master, music and SFX volume; fullscreen; screen shake; flash reduction; Focus strength. All persisted.
- **Level complete:** time vs par, medal stamp (Bronze: clear; Silver: within par + 4 s; Gold "Time Thief": within par + 1 s), then **Next** (Space) or **Retry** (R).
- **Saves:** PlayerPrefs JSON holding best ticks, medal and unlock state per level, plus settings.

## 12. Code architecture

```
Assets/
  Scripts/Sim/        Pure C#: LevelDef + JSON loader, SimState (clone/hash), Simulation.Step,
                      Solver (BFS variants), Replay. Integer-only, deterministic.
  Scripts/Game/       Boot (RuntimeInitializeOnLoadMethod), GameFlow state machine, LevelSession
                      (20 Hz accumulator, input buffer, focus, history ring for rewind, death/win),
                      SaveData, Settings, InputRouter (Input System), Autoplay/Capture.
  Scripts/View/       BoardBuilder (FBX instances + materials), PlayerView, SliderView, LaserView,
                      RotorView, DeviceViews, GhostPreview, CameraRig, Fx (particles, shake,
                      hit-stop, post-processing driver), MaterialLibrary.
  Scripts/Audio/      Sfx pool, MusicDirector (crossfades, low-pass/duck).
  Scripts/UI/         Runtime-built uGUI + TextMeshPro: Title, LevelSelect, Hud, Pause,
                      Settings, Complete, ChapterCard, Credits, Hints.
  Shaders/            Crystal, Ghost, Beam, Ring (radial fill), Background (void + clock rings),
                      TimeDistortion (full-screen pass).
  Resources/          Levels/levels.json, Levels/solutions.json, Models/*.fbx, Audio/*.wav,
                      Fonts/ (Fira Sans, OFL), Materials/.
  Editor/             ProjectSetup (URP + renderer features), BuildScript, import postprocessors.
  Tests/Editor/       Replay determinism tests.
ArtSource/            build_assets.py (Blender bpy generator), *.blend, previews/*.png
Tools/                unity.sh, play.sh, validate.sh, Solver/ (.NET console), audio/synth.py
```

There is a single scene (`Main.unity`) containing the camera, light and volume. Everything else is built at runtime, so content lives in data files.

## 13. Asset list (Blender, generated by `ArtSource/build_assets.py`)

| Asset | Notes |
|---|---|
| Player pawn | Body, head and core ring as separate objects so they can animate; dot eyes |
| Floor tile | 1×1 beveled porcelain slab, two variants |
| Wall block | Beveled slab with top inset; edge/corner trim variants |
| Board plinth edge | Side skirt with brass tick marks |
| Slider | Beveled cube, coral edge strips, chevron |
| Laser turret | Base, housing, lens |
| Rotor pivot + arm | Drum with ring; arm bar with tip light (length scaled per level) |
| Plate | Inset square pad with rim |
| Gate | Barrier slab with brass frame posts |
| Time-lock | Dial base + 12 separate segment meshes |
| Exit | Arch with hourglass and portal disc |
| Crystal shell | Faceted shard shell (cube- and capsule-fitting) |
| Shards | Small faceted fragments for shatter particles |
| Background | Large clock-ring fragments and floating gears for the void |

Previews of every model are rendered headless to `ArtSource/previews/` and reviewed before import.

## 14. Milestones

1. **M0 Setup:** plan, URP project, packages, git, tool scripts.
2. **M1 Simulation and solver:** pure-C# sim, console solver, first levels proven.
3. **M2 Prototype:** playable in Unity with primitive art, covering input, borrowing, debt, ghosts, rewind and death/win. Iterate on feel with screenshots. **Commit.**
4. **M3 Art pass:** Blender models, shaders, lighting, post-processing, FX. **Commit.**
5. **M4 Content:** all 20 levels designed and proven (B/D/margin), par times. **Commit.**
6. **M5 UX:** title, level select, HUD, pause, settings, saves, onboarding, chapter cards, ending. **Commit.**
7. **M6 Audio:** SFX, music and mix. **Commit.**
8. **M7 Polish:** juice pass, autoplay verification of every level in the real game, performance, bug sweep. **Commit.**
9. **M8 Ship:** README, Linux build in `Builds/`, final verification. **Commit.**

## 15. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Real-time precision makes puzzles feel unfair | Solver safety margin (target ±100 ms), Focus slow-mo, auto-rewind on death, generous telegraphs |
| Solver state explosion | Integer-packed states, one-loan-at-a-time rule, small boards, loan caps where needed, per-level budget |
| Ghost preview untruthful | The preview *is* the simulation; the frozen player never blocks obstacles, so forecasts don't depend on your future moves |
| Unity on CachyOS (`libxml2.so.2` missing) | `Tools/unity.sh` adds a local `libxml2.so.2` to `LD_LIBRARY_PATH` |
| Editor automation flakiness | Batch-mode scripts plus in-player autoplay/capture as a second verification path |
| Shared machine (8 sessions) | Batch/headless wherever possible, limited Blender threads, close Editors after use |
| Too many rules for players | Each chapter adds one thing; previews show consequences; no text walls |

## 16. The 5-minute test

A new player picks this up cold. Within **20 seconds** they've hovered a slider, seen its crystal ghost, clicked, and *felt* the snap. Within **1 minute** their own countdown has ticked down and they've frozen safely, and they get it: "oh, I pay it back." Within **3 minutes** they've died thawing inside a hazard, rewound with one key, and fixed their plan. Around **minute 5**, on level 5, the lock is unreachable for 3 seconds, until they realise their debt *is* the 3 seconds. They walk onto the lock at exactly the right moment, freeze, the slider passes through their crystal body, the dial fills, and the exit opens. The test passes if they immediately want to see what the next idea is, and if they glance at the par time and think "I can beat that."
