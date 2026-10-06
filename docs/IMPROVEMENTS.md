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

## Round 2 scope

Picked from the ranked list (#8 chapter VII, #10 audio, #11 polish, #12 rebinding) plus two things round 1 turned up:
- The gamepad bindings for the hint and for leaving *Watch solution* were never exercised.
- Reading `AudioDirector` for the audio item showed that **music ducking never fires**. `Play` only ducks for `world` one-shots at volume ≥ 0.9, but borrow, freeze, death, latch and win all go through `Sfx.Play`, which passes `world = false`, and no `Sfx.World` call is that loud. So PLAN §9's "music ducks under big SFX" doesn't actually happen.

The order runs smallest and most certain first, with the new chapter last because it's the biggest and riskiest. All scratch output goes to `Builds/` (gitignored), never `/tmp`.

### 1. Audio balance: working duck plus a measured mix
- **Change:**
  - The key gameplay stingers (borrow, freeze, thaw, death, dial latch, exit open, win) duck the music, whichever route they're played through.
  - Rebalance the music against the effects using numbers, not taste: a new `Tools/audio/balance.py` renders music and effects stems from the game's own audio event log (reusing `Tools/demo/mix.py`) and reports, for every key stinger, how far the effects sit above the music in the 400 ms after it fires.
- **Acceptance:**
  - `checks.sh` records an audio log over three levels at default settings.
  - Median stinger-over-music ≥ +6 dB, and no key stinger below +3 dB.
  - The mixed peak stays under the soft-clip knee (0.9).
  - Before/after numbers are recorded in this file.
- **Verify:** `balance.py` on before and after logs from the same scripted run. **Limit:** this is measured, not listened to. The owner should still give the mix a listen.

### 2. Gamepad input bot
- **Change:** `Tools/inputbot.sh` gains a gamepad pass that plays 1-1 on a *virtual* gamepad. It walks with the stick, aims with RB, focuses with LT and borrows with A, and also checks Select (hint), Start (pause) and B (stop watching).
- **Acceptance:** PASS for keyboard+mouse and for the gamepad, with the bindings listed in the log.
- **Verify:** `Tools/inputbot.sh`. Honest limit: virtual devices, not real hardware.

### 3. Key rebinding (keyboard)
- **Change:**
  - Settings gets a **Controls** page that rebinds Borrow, Focus, Rewind, Restart, Hint and Pause/Back's key, plus an alternate key for each move direction. The arrow keys and mouse always keep working.
  - Bindings persist in the save.
  - The key hints, keycaps and onboarding prompts show the bound key names.
  - A reset-to-defaults row.
  - Conflicts swap rather than duplicate.
- **Acceptance:**
  - A binding survives a save round trip.
  - The input bot passes 1-1 with rebound keys (e.g. Focus on F, Borrow on K, Rewind on X).
  - The HUD hint row shows the new names.
- **Verify:** an input-bot variant with rebinds, a `checks.sh` round-trip check, and screenshots of the Controls page and HUD.

### 4. Small polish
- **Change:**
  - The pocket watch's status label ("LOAN READY", "REPAYING") no longer sits under the watch hand.
  - `R` restarts instantly in the first 3 s, but after that you **hold** R for 0.6 s, with a fill ring, so a stray key can't throw away a long attempt.
  - Shift+Tab no longer flips aim cycling while Focus (Shift) is held.
- **Acceptance:**
  - A screenshot shows the label clear of the hand in the READY, DUE and REPAYING states.
  - A check shows a tap of R late in a level does nothing and a hold restarts.
  - The input bot still passes.
- **Verify:** `checks.sh` (restart-hold) and screenshots.

### 5. Chapter VII: five solver-proven levels
- **Change:** five new levels built with the existing lab pipeline (`Tools/lab/designs`, `sweep.py`, `assemble.py`). Each is proven to need a loan, with at least two proven to need the debt, a margin of at least ±3 ticks, par between 6 and 20 s, and each adding one idea. The levels get chapter card text, tips, music routing and Ledger paging, which already exist for 12 chapters.
- **Acceptance:**
  - `validate.sh --level 7-x` reports OK for each, with the claimed B/D proofs.
  - EditMode replay tests cover 35 levels.
  - The autopilot wins 35/35 and `checks.sh` *Watch solution* passes 35/35.
  - The README level table and counts are updated.
- **Verify:** the solver per level (never the full 4-5 run), tests, autopilot, checks, and screenshots of each new level and the chapter card.
- **Fallback:** if a level can't reach these proofs in reasonable sweep time, the chapter doesn't ship (`assemble.py` only ships complete chapters) and the designs stay in `Tools/lab/` with notes.

## Round 2 results

All five items shipped on `improvements-2`. Final verification ran on the **release** build: autopilot 35/35 at par, `checks.sh` all PASS, the input bot's four passes PASS twice in a row, and 109/109 EditMode tests. Screenshots are in `docs/media/improvements/round2/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| — | Tool defaults moved off the shared `/tmp` | `2b0e419` | Every script now writes under `Builds/`. |
| 1 | Audio: a music duck that actually fires, measured balance, stinger pile-up trim | `6a7baff` | `Tools/audio/balance.py` on the same scripted run, before → after. Median stinger over music +3.5 → +10.6 dB (+10.1 to +11.0 across later runs). Worst −1.3 → ≥ +5.0 dB. Borrow −1.0 → +5.5 dB. Mixed peak 0.97 → 0.84–0.89. **Not listened to.** |
| 2 | Gamepad input bot, which found three bugs: the menu selector plate never moved, Select didn't switch the hints to the pad, and 1-1's tip named mouse controls | `fdf0b79` | The bot's gamepad pass (D-pad, stick, RB, LT, A, Select, Start, B, pause menu, *Watch solution*), including a check that the selector plate reaches the chosen row. Shots 01–03. Virtual devices only. |
| 3 | Key rebinding (Settings > Controls), plus a fix for Esc/Start never closing the pause menu | `43f8a4a` | The bot's rebind pass goes through the real menus, then wins 1-1 on J/L/Tab/F/K, and checks the old key is dead and the hints follow. The `key-bindings` check covers swap, reserved keys, a damaged save and the JSON round trip. Shots 04–06. |
| 4 | Hold-to-restart after 3 s; watch label clear of the sub-dial hand | `7b80c21` | The bot's restart pass: an early tap restarts, a late tap only shows the tag, a hold restarts. Shots 07–08. |
| 5 | Chapter VII *Overdraft*, five levels (35 total) | `c8406e3` | `validate.sh --level 7-1` … `7-5` ALL OK: B and D proven, margin ±4. The 30 existing solutions are byte-identical. Tests 109/109, autopilot 35/35, *Watch solution* 35/35, chapter cards 7/7. Shots 09–13. |

Notes and loose ends:
- **How chapter VII was built.** Three of its levels (7-1, 7-3, 7-5) are the short-term designs that were already finished in `Tools/lab` (as 9-2, 9-1, 9-5). They were renumbered, and the chapter order changed so VII is *Overdraft*. The other two came from a new 160-candidate sweep (`designs/d7.py`, about 28 minutes on 4 cores, 3 passes). Two of the five ideas overlap: 7-2 and 7-4 both freeze the lane laser and let the debt hold a dial. Nobody has played any of them.
- **Two one-off failures.** One full `checks.sh` run failed *Watch solution* on 2-3 (no win in 40 s). It didn't recur in two reruns, and the check now logs the cause if it happens again. The release input bot once failed the gamepad focus assertion because of a timing race in the bot itself (at about 110 fps it pressed A before the Focus blend reached 0.9). Fixed, and it then passed twice.
- **Thin audio headroom.** The audio peak limit (0.90) has only about 0.01–0.06 of headroom from run to run.
- **macOS.** The macOS build script is unchanged, but the app was not rebuilt this round.
- **Media.** The trailer and README screenshots still show the 30-level game.

Still open: human playtesting, a listen to the mix, chapters VIII–XII (8-1 is ready in the lab), WebGL, Windows (needs the module), and touch.

## Round 3 scope

Written 2026-10-06 on `improvements-3`, after confirming `main` matched `origin/main` (`092ed1c`). The biggest gap a player feels is content: the README promises a 60-level arc, 35 levels ship, and chapter VIII's first design (8-1 *Instalments*) has been sitting in the lab. The other items close loose ends from round 2 that a visitor or the orchestrator would trip over. As before, items run smallest and most certain first, scratch output stays in `Builds/` (solver temp files too, via `TMPDIR=Builds/tmp`), and the full 150M-state 4-5 search isn't rerun unless something touches it.

### 1. Make the *Watch solution* check robust (the 2-3 flake)
- **Finding:** the check gives each replay a fixed 40 s of *wall-clock* time. `LevelSession` clamps a frame to 0.1 s, so when the shared machine stalls the game to a few frames per second, a replay that's still advancing correctly can run out of wall-clock time. That fits the one "no win in 40 s" failure on 2-3, which never recurred. No game code path stops a replay from finishing, since the sim is deterministic and input is ignored while watching.
- **Change:** the check fails a replay only if its tick stops advancing for 10 s (a real hang) or it runs past a generous overall cap. It also logs the frame rate and tick on every failure.
- **Acceptance:** `watch-solution` passes on every level. If it fails, the log line says whether the replay hung, died or ran slow.
- **Verify:** `checks.sh` on the release build (the full run at the end of the round), plus a run limited to watch-solution.

### 2. Chapter VIII *Amortize*: five two-loan levels (40 total)
- **Change:** five levels for "Pay it back a little at a time". Each must **provably need at least two loans** (the solver's min-loans proof, as for 1-3 and 4-2) and need a borrow. At least two must also be proven to need the debt. Each needs a margin of ±3 ticks or more, a par between 8 and 30 s, and a distinct idea, which I'll check by reading the solver's traces and the in-game replays. 8-1 *Instalments* is already proven (B, D, margin ±4). The other four come from sweeps over the existing lab families (`d82` crossings, `dtwo8` two-lane U and loop block, gates), with designs written down as they're picked. Tips are folded behind H where they give the trick away.
- **Acceptance:** `validate.sh --level 8-x` is OK for each with the claimed proofs, and the 35 existing solutions are byte-identical. EditMode tests cover 40 levels. Autopilot is 40/40, `checks.sh` is all PASS (*Watch solution* 40/40, chapter cards 8/8), and the input bot still passes. The README and PLAN level tables are updated.
- **Fallback:** as in round 2, a chapter ships only whole. If four more levels can't be proven in reasonable sweep time, nothing new ships and the candidates stay in `Tools/lab/` with notes.

### 3. README media for the current game
- **Change:** regenerate the README screenshots with the repo's own `Tools/trailer/make_trailer.sh --stills` (the release build plays its shot list), so the Ledger, the chapter card and the level shots match the shipped game. Add one still from a chapter VIII level. Stills are recorded by `stillsOnly` shots, so the trailer's cut doesn't change. The trailer itself isn't re-cut, because that's an owner decision.
- **Acceptance:** every README screenshot comes from the current build, and the Ledger shows the shipped level count. Each image is looked at before it's committed.

### 4. Docs accuracy
- **Change:** PLAN.md §6 lists chapters V–VIII as shipped (it stops at IV). README's content tables, counts, badge and "Status and known issues" follow what this round actually lands.
- **Acceptance:** every level named in the README is in `levels.json` with the proof the README claims, and the reverse holds too. A small script cross-checks the README table against `levels.json` and the solver output.

### 5. Stretch: give chapter VII five distinct ideas
- 7-2 *Float* and 7-4 *Cutoff* both freeze the lane laser and let the debt hold a dial. **Only if** items 1–4 are done and a sweep finds a proven short-term level with a different idea, it replaces 7-4. If not, it's deferred with notes. Round 2's chapter VII hasn't been released, so no player save has a 7-4 time yet.

**Not this round:** chapters IX–XII, touch controls (no touch platform ships), WebGL, Windows (blocked on the module), re-cutting the trailer, the audio mix by ear, and human playtesting.

## Round 3 results

Three of the five items shipped on `improvements-3`. Chapter VIII (item 2) did **not** ship: three of its five levels are proven and kept in the lab, but no fourth or fifth design with an idea of its own turned up. The stretch item (5) wasn't attempted. Final verification ran on the **release** build with this round's code: EditMode 109/109, autopilot 35/35 at par, `checks.sh` all PASS (*Watch solution* 35/35, the new `run-watch` check, audio balance median +10.4 dB, worst +5.0 dB, peak 0.84), the input bot's four passes PASS, and `Tools/docs_check.py` clean. Images are in `docs/media/improvements/round3/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | Scripted runs time out on a stalled tick, not wall-clock time (`RunWatch`, used by the autopilot, *Watch solution* and the audio log) | `2e9b2ac` | New check `run-watch`: a replay crawling at 1 tick/s stays alive past the 10 s limit (12 ticks in 13 s), and a replay stopped dead is caught (after 300 frames and 10 s; 25.7 s at 12 fps). *Watch solution* 35/35 on the dev and release builds. |
| 2 | Chapter VIII *Amortize* | not shipped; designs in `8020465` | See below. `levels.json` is unchanged and `assemble.py` skips the incomplete chapter. |
| 3 | README screenshots regenerated from the current build, plus a chapter VII still (7-5 *Payroll*) | `7a56e01` | `make_trailer.sh --stills` from the release build. Every image was looked at: the Ledger shows 35 levels and pages I–IV / V–VII, and the HUD shows the hint key and folded spoiler tips. Before/after sheets: shots 01–02. The trailer was not re-cut. |
| 4 | Docs accuracy: PLAN §6 covers chapters V–VII; `Tools/docs_check.py`; README status | `0a5f4ff`, `ee528f9`, `7a56e01` | `docs_check.py` matches 35 README rows, 7 chapter rows and 35 PLAN rows against `levels.json`/`solutions.json`. It caught both errors planted to test it (a dropped D claim and a wrong debt count). For V–VII, PLAN quotes each level's tip as the intended trick rather than claiming routes nobody has traced. |
| 5 | Stretch: separate 7-2 and 7-4's shared idea | — | Not attempted; the solver time went to chapter VIII. |

**About item 1.** Scope called a wall-clock deadline on a slow machine the likely cause of round 2's one 2-3 failure. That is still a hypothesis. The failure didn't recur this round, so nothing confirms it, but the machine did run the checks at 11–12 fps under load, which is the condition the old deadline handled badly. A failure now logs whether the replay died, stalled, ran over the cap or was replaced, with its tick and frame rate.

**Chapter VIII: what was tried.** All runs used 3–6 niced solver jobs while the machine's load ran between 10 and 85.
- **The lab's "ready" 8-1 *Instalments* wasn't new.** Its map and laser are 7-3 *Same Day*'s, with a 5 s term instead of 2 s, so it was dropped.
- **Proven and kept** (`Tools/lab/c81`, `c82`, `c85`; margin ±4 each):
  - *Shade* (hand-made, `d88`): two always-lit lanes and one chute. Proven: B, needs ≥ 2 loans. In the solver's route, lane 1 is shaded by freezing its laser while the chute blocks the beam, and lane 2 by freezing the chute.
  - *Bridge* (`d85`): the first debt lands you inside a dead-end shuttle corridor and the shuttle passes through you; a second loan, taken on the thaw, freezes the lane shuttle. Proven: B, needs ≥ 2 loans. The forgiven-debt search runs past 30M states, so D isn't claimed.
  - *Joint Account* (`d84`): your debt holds one dial, and a frozen chute's weight holds the other. Proven: B, D, needs ≥ 2 loans (30M-state budget).
- **Families that produced nothing usable:**
  - Crossings (`d82`): about 90 of 200 candidates in 55 min, 0 passes; stopped.
  - A loop block on a ring of dials: one loan still covered three dials.
  - A single loop block walked twice: on a closed ring the short way round is at most half the ring (45 ticks), so one freeze or none covers it.
  - The Escrow gate family with two loans (36 candidates, 0 passes).
  - Two three-tile gates held by one plate: all 16 were solvable without a loan, because a gate held by someone standing in it lets you through on a single opening.
  - The two-lane U family (`dtwo8`, 52 of 60): 4 passes, all blinking-beam-and-dial lanes that repeat 7-2, 7-3 and 4-2. Rejected.
- **The real obstacle is design, not compute.** "Two loans" is already chapter VII's texture: 7-1, 7-3 and 7-5 take two or three loans, and 1-3 and 4-2 chain loans too. Most two-loan layouts collapse into 1-3 *Grace Period* (two obstacles in a row, settle up between them). Whether VIII keeps this theme is an owner decision.

**Save safety.** The real save (`~/.config/unity3d/Borrowed Seconds/…/prefs`) has the same content before and after every run. One write did happen: Unity's EditMode test runner always saves a copy of its results to `persistentDataPath/TestResults.xml`, even when given `-testResults`. That file (a test report from earlier rounds) now holds this round's report. Running the tests with `XDG_CONFIG_HOME` pointed into `Builds/` should avoid it next time; untested.

Still open: chapter VIII (two more designs, or a new theme) and IX–XII, chapter VII's shared idea, a trailer re-cut, human playtesting, a listen to the mix, WebGL, Windows (needs the module) and touch.
