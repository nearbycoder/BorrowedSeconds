# Borrowed Seconds: improvement plan (round 1)

Written 2026-10-06 on the `improvements` branch, after a baseline run of every automated check and a read of the code, the level data and the autopilot captures. Everything below is grounded in something observed in this repository; where a claim could not be verified, it says so.

## Baseline (what passes today)

| Check | Command | Result |
|---|---|---|
| Development build | `Tools/devcap.sh /tmp/bs-cap` (build step) | Succeeded, 0 errors, 23 s (incremental) |
| In-game autopilot, all 30 levels | same | **30/30 PASS**, each wins at exactly its par tick; no exceptions in the player log |
| EditMode tests | `unity test . --mode EditMode` | **62/62 passed** |
| Input bot (virtual keyboard + mouse, 1-1) | `Tools/inputbot.sh /tmp/bs-inputbot dev` | **PASS**, won at tick 185 |
| Solver proofs (sample) | `Tools/validate.sh --level X --no-write` for 1-1, 1-5, 2-4, 3-5, 5-5, 6-5 | All OK (B/D proofs and margins match the README). The full run, including 4-5's 150M-state search, was skipped to spare the shared machine's RAM. |
| Release build / macOS / Windows / WebGL | not built in this phase | n/a |

Captures in `/tmp/bs-cap` show every level rendering correctly, with the HUD, ghosts, watch and tip panel all intact. The tooling is in good shape. The problems are in what a *human* hits that a replay never does.

## Findings from code and captures

1. **The default loop (confirmed bug).** When you thaw inside a hazard (the game's signature failure, "Defaulted"), `LevelSession.BeginRewind(false)` rewinds 2 s (40 ticks). But the debt freeze lasts 3 s (60 ticks), so the rewind lands *inside the same freeze*. The player can't act, the sim is deterministic, and they die again on the same tick: shatter, shake, "DEFAULTED", rewind, forever, until they hold Z. A headless harness against the real `Simulation` (random play, 400 runs per level) found thaw deaths on 23 of 30 levels, and **100 % of them** landed the auto-rewind inside the freeze. The one-time "Hold Z to rewind further" prompt only appears until the player has used rewind once.
2. **No pause on focus loss.** `runInBackground: 1` is set and there's no `OnApplicationFocus` handler. If you alt-tab mid-level, the 20 Hz sim keeps running: the debt comes due, you default, the timer runs.
3. **Hard-stuck players have no way out.** Unlocks are strictly linear (`LevelSelectScreen.Unlocked`), there's no skip, and there's no help beyond the always-on tip. Some tips already spell out the whole solution (6-5 *Escrow*, 3-3, 3-5), while others are one-line principles. The difficulty curve comes from the solver alone and has never met a human. Meanwhile the `H` key is parsed into `InputReader.Hint` but nothing reads it, and a proven optimal replay for every level ships in `solutions.json`, with an autoplay path that already drives the real game loop.
4. **Plate→gate wiring is invisible.** Every plate and gate renders the same mint, and no view code reads `DeviceDef.Channel`. In 6-4 *Two Keys* (two plates, two gates) you can't tell which plate opens which gate except by experiment. The 2-2 tip even says "the gate of its colour", but there's only one colour. Laser links (a plate darkens a laser in 2-2 and 2-5) aren't drawn either. Later chapters will lean on this more.
5. **Few accessibility levers for a real-time puzzle.** Settings cover volume, fullscreen, shake, flashing and Focus strength. There's no global game-speed assist, even though `LevelSession.Speed` already exists and medals count sim ticks, so a slower clock is fair. Focus slows time but blocks movement, so it doesn't help execution.
6. **Platform reach.** The blog lists Windows, macOS and Linux; the release has Linux only. This editor has Mac and WebGL support but not Windows. `BuildScript` has Linux targets only.
   - **macOS:** a Mono build is possible today. It can't be run, signed or notarized here.
   - **Windows:** needs the Windows Build Support module, which the owner has to install.
   - **WebGL:** possible, but the "out of time" muffle is an `AudioLowPassFilter`, which Unity WebGL doesn't support, so it would need an audio fallback, plus touch and web layout work. The game suits the web (short single-screen levels), but it's a bigger lift than a build switch.
7. **Content is short.** The total par for 30 levels is about 5 minutes (306 s), and README estimates 25–40 minutes for a first clear. Four finished designs for later chapters (8-1, 9-1, 9-2, 9-5) sit in `Tools/lab/`, but `assemble.py` only ships complete chapters, and chapter VII (*Exposure*) has none. Also, README says chapters VII–XII are "planned in docs/PLAN.md", but PLAN.md §6 only covers chapters I–IV; the VII–XII names exist only in `LevelCatalog.Chapters`. That's a docs accuracy fix.
8. **Audio mix.** Measured with ffmpeg `ebur128`: the music loops master at -10.5 to -12.5 LUFS, while borrow, freeze and win sit at about -16 to -17 and death at -20. With the default volumes (music 0.7, effects 0.9), the music likely sits a few dB over the key SFX outside of ducking. Integrated LUFS is a rough measure for short one-shots, and this needs ears, not numbers.
9. **Small polish.**
   - The watch's second hand crosses its own status label ("LOAN READY", "REPAYING"; visible in `02_aim-and-focus.png` and the captures).
   - `R` restarts instantly, with no confirmation, and discards the rewind history.
   - Shift+Tab cycles aim *backwards* while focusing (Shift is also Focus).
   - There's no key rebinding.

## Ranked improvements

| # | Improvement | Impact | Effort | Risk | Notes |
|---|---|---|---|---|---|
| 1 | **Fix the default loop**: auto-rewind to a point where you can act again | High | S | Low | Every human hits this within minutes (it's *the* teaching death on 1-2). It's a pure function of history, so it's unit-testable. |
| 2 | **Pause on focus loss** | High | S | Low | Standard expectation; currently costs runs silently. |
| 3 | **Stuck-player help**: `H` reveals the level's tip on demand, and *Watch the solution* (solver replay) appears in the pause menu, nudged after repeated defaults | High | M | Low–Med | Reuses `Autoplay` and `solutions.json`. It's the safety net for untested difficulty. Watching must not award medals or count as a clear. |
| 4 | **Channel-coded plates, gates and laser links** (distinct tint plus a glyph or pip count, and a wire flash on press) | Med–High | S–M | Low | Fixes 6-4 readability and the false "its colour" tip; needed before any new chapter adds channels. |
| 5 | **Game-speed assist** (100 / 85 / 70 / 50 %) in Settings | Med | S | Low | Uses `LevelSession.Speed`. Medals stay valid because time is counted in ticks. Shown on the HUD when it isn't 100 %. |
| 6 | **macOS build** (universal, Mono) with a `package.sh` target and honest README text | Med | S–M | Med | Can't be launched or tested here; it will ship marked as untested on macOS. |
| 7 | Windows build | Med | S (once the module is installed) | Med | **Blocked on the owner** installing Windows Build Support. Untestable here, apart from maybe Wine/Proton. |
| 8 | Chapter VII: five new solver-proven levels (and later, promoting the 8-x/9-x designs) | Med–High | L | Med | Solver sweeps are CPU- and RAM-heavy on a shared box, and the designs are unplaytested. Better as its own round after the help systems exist. |
| 9 | WebGL build | Med | M–L | Med–High | Needs a muffle fallback without `AudioLowPassFilter`, web settings (no Quit, no fullscreen at boot) and probably touch. Best paired with an itch.io page; owner decision. |
| 10 | Audio mix pass (lower the default music level, or re-master the loops about 4 dB down) | Med | S | Med | Needs a human ear to confirm; numbers alone could make it worse. |
| 11 | Small polish: label clear of the watch hand, hold-to-restart or confirm when far into a level, Shift+Tab focus conflict | Low–Med | S | Low | Good filler once the items above land. |
| 12 | Key rebinding | Low–Med | M | Low | Physical-key bindings already cope with AZERTY; it helps one-handed play. |
| 13 | Touch controls | Low now | M | Med | Only matters if WebGL or mobile happens. |
| 14 | Docs accuracy: README's "planned in docs/PLAN.md" claim; add a short §6b for chapters V–XII to PLAN.md | Low | S | None | Do it alongside any README change. |

Human playtesting and choosing a license are owner tasks, not code.

## Proposed scope for this round

Six items, ordered so the cheap, high-impact fixes land first. Every item keeps the existing checks green: autopilot 30/30, the EditMode tests, and the input bot.

### 1. Fix the default loop
- **Change:** move the rewind-target choice into a small pure function (Game layer, testable from EditMode). After a death, rewind to the latest tick that is at least 2 s before the death **and** where the player was free to act, with at least 1 s left before the next forced freeze. In practice, that's before the `Due` that froze them. Fall back to the borrow tick, then to tick 0.
- **Acceptance:** for any thaw death, the restored state has `PFrozen == 0`, `!Pending`, and either `Countdown == 0` or `Countdown >= 20`. Non-thaw deaths keep today's 2 s rewind unless that lands in a freeze.
- **Verify:** add EditMode tests that, for every level, generate thaw deaths with the same seeded random-play harness I used for the baseline. They assert the new target meets the acceptance condition and that replaying from it with a different action avoids the death in at least one case. Run the existing 62 tests and the 30/30 autopilot.

### 2. Pause on focus loss
- **Change:** `OnApplicationFocus(false)` / `OnApplicationPause(true)` while `State == Playing` (not in scripted runs) opens the pause menu. Release any held input state.
- **Acceptance:** after a focus loss, the sim tick doesn't advance, and the pause menu is up on return.
- **Verify:** an input-bot step calls the focus handler mid-level, waits 2 s of real time, and asserts the tick is unchanged and `State == Paused`. Check by hand once with alt-tab in the dev build.

### 3. Stuck-player help: `H` hint and *Watch the solution*
- **Change:**
  - The tip panel starts collapsed, with only "H: hint" showing, on levels whose tip gives away the solution (flagged in the lab JSON). It stays always-on for the teaching levels (1-1 to 1-2, and the first level of each chapter).
  - Pause gets **Watch solution**: the level restarts muted-HUD under `Autoplay` with a "SOLUTION" banner, then returns you to a fresh start. Watching doesn't record a best time or unlock anything.
  - After 3 defaults in one attempt, a one-line prompt mentions it.
- **Acceptance:** the replay wins at par in the real game loop for every level, and the save is byte-identical before and after watching. `H` toggles the tip with keyboard and gamepad (Back/Select), and the control hints list it.
- **Verify:** extend autopilot with a `-bsWatch` mode that drives the pause-menu path for all 30 levels (30/30 PASS, save untouched). Capture screenshots of the collapsed and expanded tip and the banner, and look at them.

### 4. Channel-coded plates, gates and laser links
- **Change:**
  - Each channel (a–e) gets a distinct tint from a colourblind-checked set built around mint, plus a redundant shape cue (pip count or notch glyph) on the plate and on both gate posts.
  - Pressing a plate pulses a faint wire or glow to its gate(s) and linked laser.
  - Fix the 2-2 tip wording.
- **Acceptance:** in 6-4 each plate visibly matches exactly one gate in a still frame and in greyscale, and 2-2/2-5 show the plate→laser link.
- **Verify:** `Tools/capture.sh … -bsOnly 6-4` and `-bsOnly 2-2`, plus a greyscale conversion of the captures (ImageMagick) for review. Autopilot 30/30.

### 5. Game-speed assist
- **Change:** a Settings slider (100 / 85 / 70 / 50 %) applied through `LevelSession.Speed` (and focus on top), persisted in `SaveData`. When it isn't 100 %, a small "SPEED 70%" tag shows on the HUD.
- **Acceptance:** sim results are identical at any speed (same win tick), and the setting persists across restarts.
- **Verify:** an autopilot run with a `-bsSpeed 0.5` override wins all 30 levels at the same ticks; a save round-trip test covers persistence.

### 6. macOS build
- **Change:** a `BuildScript.BuildMac` entry (Mono, universal Intel and Apple silicon), `Tools/unity.sh build-mac`, and `Tools/package.sh` producing `BorrowedSeconds-v0.x-macos-universal.zip`. The README play section adds the unsigned-app/Gatekeeper workaround and says plainly that the macOS build is untested.
- **Acceptance:** the batch build succeeds, `file` reports a universal Mach-O (`x86_64` + `arm64`), and `Info.plist` has the right name and version.
- **Verify:** build log, `file`, `plutil`/`python plistlib` on `Info.plist`. Not verifiable here: that it launches on a Mac.

**Explicitly out of this round:** new levels (chapter VII), WebGL, Windows (blocked), the audio re-master, key rebinding and touch. The trailer and README media stay as they are, and the README only gains honest notes for what actually changed.

## Decisions for the owner

- **Windows:** install *Windows Build Support (Mono)* for 6000.6.2f1 in Unity Hub if you want a Windows zip. After that it's an S-sized task, but it would still be untested on Windows.
- **macOS:** the build will be unsigned and un-notarized, and untested on real hardware. Is shipping it labelled "untested" acceptable, or should it wait for a Mac test?
- **WebGL:** worth a dedicated round (it needs an audio-muffle fallback and touch)? It's the best fit for the blog and itch.io reach.
- **Solutions in-game:** is a *Watch solution* button acceptable design-wise, or should it unlock only after N defaults or minutes on a level?
- **Audio:** a human listen to the default mix (music vs. effects) would settle item 10.

## Round 1 results

All six items shipped on `improvements`, one commit per item. Each was verified in the built game, not just in the editor. `Tools/checks.sh` is a new scripted check run that makes the built game die, rewind, pause and open menus on purpose; it uses the `-bsChecks` flag (`GameRoot.Checks.cs`). Screenshots are in `docs/media/improvements/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | Default loop fixed (`Sim/Rewind.cs`) | `d5b3525` | 32 new EditMode tests (94/94 pass): every seeded thaw death on every level rewinds to a state the player can act from, and some play from there survives. In-game check `default-rewind` on 1-2, 2-1 and 4-5: rewound to 1 s before the freeze, no repeat death. Shot 01. |
| 2 | Pause on focus loss | `06bed62` | In-game check `focus-pause`: the tick stays frozen for 2 s with the menu up, then resumes. The focus loss is simulated by calling the handler; nobody alt-tabbed a real window, to avoid taking focus on a shared desktop. Shot 02. |
| 3 | `H` hint (8 spoiler tips folded) and *Watch solution* | `1ee5f0f`, `f035713` | Check `hint-toggle` (folded, open, nudge after 3 defaults, refold, non-spoiler stays open). Check `watch-solution`: all 30 levels via the pause-menu path win at par, return fresh, and leave progress untouched. Shots 03–06. |
| 4 | Channel-coded plates, gates and laser links | `f80af4e` | Check `channels` on 6-4 (one link per plate) and 2-2 (gate + laser). Colour and greyscale captures show the 1-dot/2-dot pairing. Shots 07–09. |
| 5 | Game-speed assist | `4b30947` | Check `game-speed`: the row steps to 70 %, survives a save round trip, and the level runs at 13.8–14.0 ticks/s (expected 14.0). An autopilot at `-bsSpeed 0.5` wins all 30 levels at exactly their par ticks. Shots 10–11. |
| 6 | macOS build | `c676a2a` | The batch build succeeds. The executable and dylibs are universal Mach-O (x86_64 + arm64) with Unity's ad-hoc signature. `Info.plist` reads `com.nearbycoder.borrowedseconds` 0.1.0. `package.sh 0.1.0 macos` zips it. **Not run on a Mac; not published.** `build-windows` fails cleanly without the module. |

Infrastructure: `066d850` caps scripted-run log files at 512 MB. A player stuck in a GL-context retry loop once wrote a 6 GB log into the shared `/tmp`; the same full disk is the likely cause of one native crash during an autopilot run, which didn't recur once output moved to disk.

Still open from the ranked list: chapter VII content, WebGL, Windows (needs the module), an audio mix pass by ear, small polish (#11), key rebinding, touch, and human playtesting.
