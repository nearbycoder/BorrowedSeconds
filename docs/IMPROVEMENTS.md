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

## Round 4 scope

Written 2026-10-06 on `improvements-4`, after confirming `main` matched `origin/main` (`6666428`) with a clean tree. Chapter VIII stays in the lab (its theme is the owner's call) and touch controls stay out (no touch platform ships). This round targets things a player on a different setup from the development machine would hit. A baseline survey turned up four:

- **The thaw forecast relies on colour alone.** The safe/lethal ring under the player is the game's central forecast, and it's mint for safe and red for lethal (amber for "dies just after", gold for "latches a dial"). Nothing else on screen says which: the aim tag only reads "FREEZE 3.0s, repay in 5.0s". A red/green colour-blind player can't read the most important cue in the game.
- **The interface breaks on screens that aren't 16:9.** The menu tour at 2560×1080 (21:9) puts the Ledger's page tabs on top of the first row of level cards, and the Settings window fills the full height of the screen. At 1024×768 (4:3), the key-hint row runs under the pocket watch. The cause is the canvas scaler, which blends width and height 50/50, so a 21:9 canvas is only 935 units tall and a 4:3 canvas only 1662 wide, while the layouts assume 1920×1080. Captures: `Builds/r4/base-*` (not committed; the before/after sheets are).
- **Focus can only be held.** Focus (slow time while you aim) needs Shift, the right mouse button or LT held down while you also aim and borrow. There's no toggle, which is a standard accessibility option.
- **Scripted runs can still write to the real config directory.** Unity's player writes its own `Screenmanager …` keys (window size, position, fullscreen mode) into the real `prefs` on quit, even when the game's save is read-only. A baseline run at 2560×1080 with an isolated `XDG_CONFIG_HOME` showed it writing `Screenmanager Resolution Width = 2560`. Round 3 also noted that EditMode tests write `TestResults.xml` into the real `persistentDataPath`.

Items run cheapest first, and the stretch item comes last. Scratch output stays in `Builds/`.

### 1. Scripted runs and tests never touch the real config
- **Change:** `checks.sh`, `capture.sh`, `devcap.sh` and `inputbot.sh` run the game with `XDG_CONFIG_HOME` inside their output folder. A new `Tools/test.sh` runs the EditMode tests the same way. `play.sh` gains `BS_SIZE=WxH` for other window sizes. Plain `play.sh` (a person playing) keeps the real config.
- **Acceptance:** every tool run this round leaves the SHA-256 of every file under `~/.config/unity3d/Borrowed Seconds/` and `DefaultCompany/BorrowedSeconds/` unchanged. The isolated folder shows the writes that would otherwise have landed there.
- **Verify:** hashes before and after the round's final full run (recorded in `Builds/r4/save-before.sha`).

### 2. A thaw forecast you can read without colour
- **Change:** the verdict is spelled out in words and shapes as well as colour:
  - The aim tag gains a third line: *thaw safe*, *thaw lethal*, *dies after thaw* or *latches a dial*.
  - While a debt runs, the term line under the watch shows the same verdict for the tile you're standing on.
  - The lethal ring gets an X across it and the "soon" ring a broken outline, so the safe ring is the only plain disc.
- **Acceptance:** the HUD's verdict text matches `GhostPreview.Result` in every state, and in a deuteranopia-simulated capture, safe and lethal can be told apart by shape and text alone.
- **Verify:** a new `checks.sh` check (`forecast`) that steps scripted states on 1-2 into the safe and lethal verdicts and compares the HUD text to the forecast. Captures in colour, greyscale, and through a deuteranopia colour matrix (ImageMagick), looked at before committing.

### 3. Layout on any screen shape
- **Change:** canvases scale in `Expand` mode, so a canvas is never smaller than 1920×1080 units in either direction and anything laid out for 16:9 fits. Then fix whatever the captures still show.
- **Acceptance:** at 2560×1080 (21:9), 1280×800 (16:10), 1024×768 (4:3) and 1600×900 (16:9, unchanged), no menu or HUD element overlaps another or leaves the screen in the menu tour or a level capture.
- **Verify:** the menu tour (`-bsMenus`) and a 4-5 level capture at each size, with before/after contact sheets committed to `docs/media/improvements/round4/`. Autopilot 35/35 at 1600×900 to confirm nothing else moved.

### 4. Focus: hold or toggle
- **Change:** a Settings row, *Focus mode: Hold / Toggle*, saved with the other settings. In Toggle mode, a press of the Focus key, the right mouse button or LT turns Focus on and the next press turns it off. Focus switches off when the level restarts, ends or pauses. Key hints say "focus (toggle)".
- **Acceptance:** with Toggle on, a tap of Shift leaves time slowed after release (blend ≥ 0.9 a second later) and a second tap restores it. Hold mode behaves as before. The setting survives a save round trip.
- **Verify:** a new input-bot pass on virtual keyboard and gamepad, the existing passes still PASS, and the `checks.sh` settings round trip.

### 5. Stretch: give 7-4 an idea of its own
- Only if items 1–4 are done and the machine is quiet. One bounded sweep looks for a short-term level whose trick isn't "freeze the lane beam, let the debt hold the dial" (7-2's). If nothing proven turns up, it's deferred with notes, as in round 3.

**Not this round:** chapter VIII (owner), touch controls, WebGL, Windows (module), re-cutting the trailer, the audio mix by ear, human playtesting.

## Round 4 results

Items 1–4 shipped on `improvements-4`; the stretch item (5) wasn't attempted. Final verification ran on the **release** build with this round's code: EditMode 109/109 (`Tools/test.sh`), autopilot 35/35 at par, `checks.sh` all PASS (now including `forecast`; *Watch solution* 35/35; audio balance median +10.7 dB, worst +5.2 dB, peak 0.89), the input bot's five passes PASS, `Tools/shapes.sh` clean at four sizes, and `Tools/docs_check.py` clean. Images are in `docs/media/improvements/round4/`.

| # | Item | Commits | Verification |
|---|---|---|---|
| 1 | Scripted runs, tests and batch editor runs keep Unity's config out of `~/.config/unity3d` | `ce132ce`, `729ef03` | Every tool run this round left the real save's SHA-256 unchanged. Found along the way: the **batch editor** (every build) rewrote the real `prefs` from memory, with the same content but a new mtime. It could have undone progress saved by a game played during a build. `unity.sh` now sandboxes batch runs, and a release build after the fix left the real file's mtime alone. |
| 2 | A thaw forecast you can read without colour | `9a7b2f2` | New check `forecast`: aiming on 1-2, a thaw death on 1-2 and 1-5's solution. On every frame the HUD's verdict matches the ghost's, with the verdict spelled out under the watch and in the aim tag (296 frames, 0 mismatches, on the release build). Captures in colour, through a deuteranopia matrix and in greyscale: shot 07. |
| 3 | Layout on any screen shape | `0180b80`, `7679d15`, `5770379` | `Tools/shapes.sh` (menu tour plus a level) at 2560×1080, 1600×900, 1280×800 and 1024×768: the Ledger's tabs no longer cover the cards at 21:9, Settings fits, and the hints clear the watch at 4:3 (shots 01–03, 06). The camera now keeps every floor tile out from under the HUD. Before, the watch hid 4-5's bottom dial and the tip hid 4-4's (shots 04–05). The autopilot logs how far each level's framing moved: 35/35 clear at 16:9 (seven levels pull back 2.5–10 %, the rest only slide or stay put) and at 4:3 (sliding alone). |
| 4 | Focus: hold or toggle | `9037a4e` | New input-bot pass: the option goes on through its Settings row. A tap of Shift, the right mouse button or LT each holds Focus at 1.00 a second later, and a second tap returns it to 0.00. A pause ends it, and hold mode comes back. Shot 08. The key-hint row also scales down when it would reach the watch; it already touched the watch at 16:9. |
| 5 | Stretch: give 7-4 its own idea | — | Not attempted. See below. |

**Notes and limits.**
- **Screen shapes were checked in windows on one 4K monitor**, not on real ultrawide, 4:3 or Steam Deck screens. The framing search tries pull-backs up to 1.4× and slides up to two tiles each way. If nothing is clear, it keeps the framing with the fewest covered tiles; no shipped level needs that at the four sizes tested.
- **Framing changed on seven levels at 16:9** (1-3, 1-5, 4-2, 4-4, 5-2, 5-3, 6-3), by 2.5–10 % pull-back and a slide. The trailer and its stills frame levels the old way (they don't pass tiles), so the trailer cut stays reproducible.
- **Colour-blind play was checked by simulation**, not by colour-blind players. The X marks a lethal or "hit just after" thaw. The words say which, plus *SAFE, DIAL LATCHES* when the thaw latches a dial; the scripted runs never produced that verdict, so the check didn't see it.
- **README screenshots** were regenerated from the release build with `make_trailer.sh --stills`, so they show the new forecast line and aim-tag verdict. They keep the trailer's framing.
- **Load.** Input-bot and `checks.sh` runs waited for a load average under 22. The machine ran at load 2–32 during the round.
- **Stretch item.** Not attempted. Round 3 found the limit was design rather than compute: sweeps over the existing families kept collapsing into ideas the game already has. Replacing a shipped level also deserves a playtest that hasn't happened. It stays open.

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including colour-blind players and other screen shapes), a listen to the mix, WebGL, Windows (needs the module) and touch.

## Round 5 scope

Written 2026-10-06 on `improvements-5`, after confirming `main` matched `origin/main` (`ef11712`) with a clean tree. Chapter VIII (owner: theme), replacing 7-4 (needs a playtest first), the trailer re-cut (owner) and touch controls (no touch platform ships) stay out. A read of the menus, the HUD and the input code turned up five things a player would hit:

- **The Ledger gives away folded tips.** Ten levels fold their tip behind H because it spells out the trick (1-5, 2-4, 3-2, 3-3, 3-5, 4-1, 5-5, 6-5, 7-2, 7-4). But the Ledger's info panel prints `def.Hint` for every unlocked level, and it opens on the next level to play, so the tip for a level you haven't started is on screen before you choose it.
- **Every gamepad is an Xbox pad.** The HUD hints, the folded-tip line, the restart tag, the *Watch solution* line, the onboarding prompts and 1-1's gamepad tip all name A, B, X, Y, LB, RB, LT, Select and Start. A DualShock 4 or DualSense (which the Input System recognises on Linux through hidraw) or a Switch Pro controller gets the wrong names. The menu footers (title, Ledger, Settings, Controls) name keyboard keys even when you're on a pad.
- **Unplugging the controller doesn't pause.** Focus loss pauses a level, but a pad whose battery dies mid-level leaves the debt running.
- ***Watch solution* is all or nothing.** It plays the route at full speed, with no aim or forecast before each borrow and no way to slow it or see a moment again. For the levels that need the debt, the timing is the whole point, and it's over in a few seconds.
- **The README stills aren't the game's framing.** The stills tool clears the trailer's camera moves but frames the board without the HUD avoidance the game now uses, so 4-5's bottom dial sits under the watch in the *Settlement* still, which a player never sees.

Items run cheapest first. Scratch output stays in `Builds/r5/`. Every tool run has to leave the real save's SHA-256 unchanged (`Builds/r5/save-before.sha`).

### 1. The Ledger keeps spoiler tips folded
- **Change:** the Ledger shows a level's tip only if the tip isn't a spoiler or the level is already settled. Otherwise it says the tip is folded and names the device's hint key.
- **Acceptance:** with a fresh save, no spoiler tip appears in the info panel for any level; once a spoiler level is settled, its tip shows.
- **Verify:** a new `checks.sh` check (`ledger-tips`) selects every card on a fresh in-memory save and on a settled one and compares the panel text with `levels.json`; a screenshot of a folded and a settled spoiler level.

### 2. Button names that match the controller
- **Change:** one table of pad button names, picked from the pad in use: Xbox names by default, PlayStation names (Cross, Circle, Square, Triangle, L1/R1, L2, Options, Share or Create) for a DualShock 4 or DualSense, and Nintendo names (B, A, Y, X by position, L/R, ZL, +, −) for a Switch Pro controller. Every place that names a pad button uses it, including 1-1's gamepad tip (written with tokens). The menu footers switch to the pad's buttons when a pad was used last.
- **Acceptance:** with a virtual DualSense, the HUD hints, folded tip, restart tag, onboarding prompt and menu footers name PlayStation buttons and contain no Xbox-only names. Likewise for a virtual Switch Pro controller, and an ordinary gamepad keeps today's text.
- **Verify:** a new input-bot pass that adds each virtual pad, presses a button and reads the HUD, tip, prompt and footer strings; screenshots. **Limit:** virtual devices. Whether a real DualSense reaches the game as a DualSense on a given Linux setup depends on hidraw permissions (without them Unity reports a generic gamepad, which gets Xbox names).

### 3. Pause when the controller disconnects
- **Change:** if a gamepad is removed or disconnected while a level is in play, the pause menu opens (as on focus loss), and the hints fall back to the keyboard when no pad is left.
- **Acceptance:** removing the pad mid-level freezes the tick with the pause menu up; nothing happens outside a level.
- **Verify:** the input bot removes its virtual pad mid-level and checks the state, the tick over 1 s and the hint row.

### 4. A solution you can follow
- **Change:** while watching, the aim highlight, ghost forecast and aim tag appear about 0.8 s before each of the solver's borrows. Holding Focus slows the replay as it slows play, and holding Rewind scrubs back; on release the replay carries on from there. Watching still records nothing and still ends at par.
- **Acceptance:** *Watch solution* wins at par on all 35 levels. Before every borrow in every replay, the aim is on that borrow's target. Rewinding mid-replay and letting go still wins at exactly par. Focus slows the tick rate.
- **Verify:** the `watch-solution` check (35/35, at par, progress untouched) extended to check the aim before each borrow. The input bot's gamepad pass holds the rewind button and LT during the replay and checks the tick goes back, slows, and still ends at par. Screenshot of the pre-borrow preview.

### 5. README stills in the game's framing
- **Change:** `make_trailer.sh --stills` frames boards the way the game does, keeping tiles out from under the HUD. The trailer's own shots keep their framing, so the cut stays reproducible.
- **Acceptance:** in the regenerated *Settlement* still, no floor tile sits under the watch or the HUD. The trailer code path is unchanged.
- **Verify:** regenerate the stills from the release build and look at every one before committing; before/after sheet.

**Not this round:** chapter VIII, replacing 7-4, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio mix by ear, human playtesting.

## Round 5 results

All five items shipped on `improvements-5`. Final verification ran on the **release** build with this round's code: EditMode 109/109 (`Tools/test.sh`), autopilot 35/35 at par, `checks.sh` with every in-game check PASS (including the new `ledger-tips` check and the extended `watch-solution` check), the input bot's six passes PASS, the solver on 1-1 (whose tip changed) OK, and `Tools/docs_check.py` clean. Images are in `docs/media/improvements/round5/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | The Ledger keeps spoiler tips folded | `fc96dfd` | New check `ledger-tips`: on an empty save, all 10 spoiler tips are folded in the info panel; on a settled save every tip shows (60 panels checked). Shot 01. |
| 2 | Button names that match the controller, pad-aware menu footers | `5225f8a` | New input-bot pass with virtual DualSense, DualShock 4, Switch Pro and plain pads: key hints, 1-1's tip, the folded tip, the restart tag, the move, borrow and focus prompts, and the title, Ledger and Settings footers all use that pad's names. Shot 02. |
| 3 | Pause when the controller is unplugged | `5225f8a` | The same pass removes the pad mid-level: the pause menu opens, the tick holds for 1 s, and the hints fall back to the keyboard. |
| 4 | *Watch solution* at your own pace | `0a1cf6a` | `watch-solution`: 35/35 at par, with every borrow in every replay aimed when it fires, and progress untouched. Input bot: 20.0 ticks/s, 3.9 with LT held; X rewound the replay from tick 44 to 13; it still won at 163 (par 163) and handed the level back. Shot 03. |
| 5 | README stills in the game's own framing | `b177dca` | Regenerated from the release build. 03 *Exactly Now*, 04 *In the beams* (2-4) and 10 *Settlement* changed framing; in each, no floor tile sits under the watch any more. The other stills differed only in noise (under 0.1 % of pixels; the title's watch shows the real time) and were kept as they were. Shot 04. |

**Found along the way, and fixed:**
- On a pad, the "A, freeze it" onboarding prompt never appeared, because a pad is always aiming at something. It now shows over the aimed obstacle, above the aim tag.
- With *Toggle Focus* on, round 4 changed the wrong prompt: the rewind prompt said "Press" (rewind is always held) and the focus prompt "Hold". Swapped.
- The focus/solution tag and the rewind tag share a spot and overlapped when both showed, in play too (holding Focus while rewinding). The focus tag now fades under the rewind tag.
- `inputbot.sh` and stills runs of `record.sh` lacked the 512 MB log cap the other tools have. Added. Clip recording stays uncapped because it writes long videos.

**Notes and limits.**
- **Audio peak, one failure.** The full `checks.sh` run failed `balance.py` on the mixed peak: 0.90x against a limit of 0.90 (every other number passed: median +10.0 dB, worst +5.1 dB). A rerun of the same scripted audio passed at 0.86. No audio code or asset changed this round. Round 2 already noted the limit has only 0.01–0.06 of headroom, and this run went past it. The threshold wasn't loosened; whether to lower the mix slightly or relax the limit is a judgement call (ideally by ear).
- **Controllers are virtual.** PlayStation and Switch names were checked on virtual devices created with Unity's own layouts, not real controllers. On Linux, Unity reads PlayStation pads as HID devices; without hidraw access (restricted on some distributions, per Unity's docs for the DualSense) it reports a plain gamepad, which gets Xbox names. The unplug pause was checked by removing a virtual device, not by pulling a cable.
- **Watch solution.** Rewinding a replay replays the same solver actions from the rewound tick, so it can't drift: the sim is deterministic and the check shows it still ends at par. The 0.8 s aim lead is real time at full speed, and longer while Focus slows the replay.
- **Load.** Input-bot and `checks.sh` runs waited for a load average under 24. The machine ran at load 12–54 during the round. The real save's SHA-256 and mtimes were the same before and after the round (`Builds/r5/save-*.sha`).

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4, waiting on a playtest), a trailer re-cut (it still says 30 levels and keeps the old framing), human playtesting (including real PlayStation and Switch controllers), a listen to the mix (and the peak margin above), WebGL, Windows (needs the module) and touch.

## Round 6 scope

Written 2026-10-07 on `improvements-6`, after confirming `main` matched `origin/main` (`70b9f64`) with a clean tree. Chapter VIII (owner: theme), replacing 7-4 (needs a playtest first), the trailer re-cut (owner), the audio peak limit (owner) and touch controls (no touch platform ships) stay out. Rounds 1–5 worked through the ranked list; what's left there is owner-blocked. So this round started from the first minutes of play and from players on other hardware, and found four things:

- **The game's first instruction is hard to do with a mouse.** 1-1's tip says "Hover the moving block, then click". That block moves at 2 ticks per tile, 10 tiles a second, along a 15-tile lane, and the pick collider is one tile wide. Aim is whatever is under the pointer that frame, so a pointer resting on the lane aims at the slider for only about two ticks each time it passes, and the aim tag, highlight and ghost flicker on and off. The input bot only manages it by moving its pointer onto the block every frame. The same goes for every fast slider (19 of 35 levels have a slider at 2–3 ticks per tile), a dark laser (only its turret is pickable) and a rotor between sweeps.
- **Windowed mode has no window size.** The only display option is a Fullscreen toggle. The game starts full-screen at the desktop's resolution, and switching to a window keeps that resolution, so the "window" is as big as the screen. There's no way to pick a size short of the `-screen-width` command line.
- **Nothing helps a weaker GPU.** Measured with the fps probe on the release build (load 10–19, no vsync): 215 fps at 1600×900, but about 105 fps in a 3072×1728 window, so cost tracks pixel count. This machine's iGPU (Radeon 8060S) is a fast one; on a typical laptop iGPU a 4K screen would likely run well under 60 fps. There's no resolution or render-scale option.
- **The rules aren't anywhere in the game once the prompts retire.** Onboarding prompts disappear for good once used, and each tip only shows in its own level. A player who comes back after a week, or skips a tip, has no in-game place to read what the debt, the term, the ghosts, dials, plates and the exit rule are, or which keys do what.

Items run in that order; item 4 is a stretch. Scratch output stays in `Builds/r6/`. Every tool run has to leave the real save's SHA-256 and mtime unchanged (`Builds/r6/save-before.*`).

### 1. Forgiving mouse aim
- **Change:** the pointer aims at an obstacle when it is over the obstacle itself (as now) *or* over the ground that obstacle owns: a slider's track, a laser's lane up to the first wall, a rotor's sweep. Where zones overlap, the obstacle nearest the pointer wins. A direct hit on a piece still wins over a zone. Gamepad and Tab aiming are unchanged. 1-1's tip says you can hover the block *or its track*.
- **Acceptance:** with the pointer parked on any tile of 1-1's lane, the aim stays on the slider on every frame of a full slider cycle (before: only the frames when the block is under the pointer). On every level, a pointer over each tile that belongs to exactly one obstacle's zone aims at that obstacle. The existing mouse pass still wins 1-1.
- **Verify:** a new `checks.sh` check (`aim-reach`) that measures both, run before the change (to record the baseline) and after. A new input-bot step that borrows 1-1's slider with the pointer parked in the alcove instead of tracking the block. Screenshot.

### 2. Window sizes
- **Change:** the Fullscreen row becomes a *Display* row: Fullscreen, or a window of 1280×720, 1600×900, 1920×1080, 2560×1440 or 3200×1800 (only sizes that fit the screen are offered). Choosing a window size sets it with `Screen.SetResolution`, and it's saved.
- **Acceptance:** from a running window, choosing each size through the Settings row gives that window size within a few frames, and the choice survives a save round trip.
- **Verify:** a new `checks.sh` check (`display`). **Limit:** the check does not switch to real fullscreen, because a fullscreen window on the shared desktop would cover other sessions' windows. Fullscreen-to-window is only checked by code reading.

### 3. Render resolution for weaker GPUs
- **Change:** a *Render resolution* row (100, 85, 70, 50 %) that sets URP's render scale. The 3D scene renders at that fraction of the screen and is scaled up; menus and the HUD stay at full resolution.
- **Acceptance:** the row changes URP's render scale and survives a save round trip. At 50 %, the fps probe in the same large window runs measurably faster than at 100 %. The HUD text stays sharp in a 50 % screenshot.
- **Verify:** the `display` check, the fps probe at 100 % and 50 % in a 3072×1728 window back to back (with the load noted), and a screenshot looked at before committing.

### 4. Stretch: How to play
- **Change:** a *How to play* page, opened from the title menu and the pause menu: the rules in a few short lines (borrow, the term and the debt, thawing, ghosts and the verdict, dials, plates and gates, the exit) and the controls, using the player's own key bindings or the pad's button names.
- **Acceptance:** the page opens from both menus and closes with Esc or the pad's back button. Its controls text follows a rebound key and a virtual DualSense's names. It fits at 16:9 and 4:3.
- **Verify:** a check that opens it and reads its text after a rebind, the input bot's controller-names pass reads it on each virtual pad, and screenshots at 1600×900 and 1024×768.

**Not this round:** chapter VIII, replacing 7-4, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio peak limit and the mix by ear, human playtesting.

## Round 6 results

All four items shipped on `improvements-6`, including the stretch item. Final verification ran on the **release** build with this round's code: EditMode 145/145 (`Tools/test.sh`; 36 new aim-zone cases), autopilot 35/35 at exactly par, `checks.sh` with every check PASS (including the new `aim-reach`, `display` and `how-to-play` checks; *Watch solution* 35/35; audio balance median +9.8 dB, worst +4.1 dB, peak 0.80), the input bot's six passes PASS, `Tools/shapes.sh` at four sizes, the solver on 1-1 (whose tip changed) OK, and `Tools/docs_check.py` clean. The real save's SHA-256 and mtimes were the same before and after the round (`Builds/r6/save-*`). Images are in `docs/media/improvements/round6/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | Forgiving mouse aim: an obstacle's track, lane or sweep aims it | `805a19f` | `aim-reach`: a pointer parked on 1-1's lane at (1,4), (7,4) or (13,4) stays aimed at the slider on 100 % of frames over a full 56-tick cycle; the piece alone (the old behaviour, measured in the same run) managed 7–13 %. On all 35 levels, 614 tiles owned by a single obstacle aim at it (245 did before, all of them under the piece itself); 10 tiles were skipped because another piece stands in front. The input bot borrows 1-1's block with its pointer parked mid-lane (aimed on 46/46 frames) and still wins at tick 185. Shot 01. |
| 2 | Display: fullscreen or a window of a chosen size | `54db083` | `display`: from a 1600×900 window, the Display row stepped the window through 1280×720, 1600×900, 1920×1080 and 2560×1440 (the sizes that fit this 3072×1728 desktop), each within 0.1–1.9 s, and the choice survives a save round trip. Shot 02. |
| 3 | Render resolution (50–100 %) | `54db083` | `display`: the row sets URP's render scale to 0.50 and back, and it survives a save round trip. The 50 % capture keeps the HUD sharp (shot 02). The fps probe, see below. |
| 4 | How to play, from the title and pause menus | `d72985f` | `how-to-play`: opens from both menus, lists the rules and the default keys, follows a rebound Borrow key, and returns to the menu it came from. The input bot's controller-names pass reads the controls on a virtual DualSense, DualShock 4, Switch Pro and plain pad. The menu tour captures it, and `shapes.sh` shows it fits at 21:9, 16:9, 16:10 and 4:3. Shots 03–05. |

**Notes and limits.**
- **The fps probe couldn't settle item 3's speed-up.** The iGPU is shared with the other sessions, and their editors and players kept it at 95–100 % busy for most of the round. Baseline at a quiet moment (load 10–19): 215 fps at 1600×900 and about 105 fps in a 3072×1728 window. Back-to-back runs in that window, 100 % then 50 %: 39–42 against 46–51 fps (load 22–23), and 41–47 against 72–79 fps (load 25). A third pair ran at about 17 fps either way, with the GPU at 100 % from other processes. So 50 % was faster whenever the GPU had room, but there's no clean number, and it hasn't been tried on a weaker GPU.
- **Fullscreen was never entered by a script.** On the shared desktop, a fullscreen window would cover other sessions' windows. Leaving fullscreen now calls `Screen.SetResolution` with the saved window size, but that path was only checked by reading the code. Changing other settings no longer touches the window, so a window the player dragged to a new size keeps it.
- **Overlapping zones go to the nearest piece.** A rotor's sweep covers a lot of floor (up to a 7×7 disc), so a click near a rotor borrows it. Clicks only ever borrow, so this can't move the player by mistake, but nobody has played with it yet.
- **One failed run, under load.** The first input-bot run (dev build, load spiking to 67) failed the controller-names pass on two fixed-time waits (the focus prompt after 0.3 s and the folded tip two frames after Select). Neither touches this round's code. Run again on the release build at load 14–16, every pass passed.
- **The trailer's settings shot** selects rows by index; `shots.json` now points at the same three rows (Music, Reduce flashing, Focus slow-motion), so a re-record shows the same adjustments. The trailer itself wasn't re-cut.
- **Audio.** No audio changed. Balance passed this time, at peak 0.80; the worst stinger margin, +4.1 dB, is the lowest so far (the limit is +3 dB), measured while the machine was under load.
- **Load.** Input-sensitive runs waited for a load average under 24. The machine ran at load 12–73 during the round.

**Decisions for the owner.**
- The earlier owner decisions are still open: chapter VIII's theme, replacing 7-4, the trailer re-cut, the audio peak limit, Windows Build Support, signing and notarization, a license, and releases.
- Should *Watch solution* (or some number of defaults) be allowed to unlock the next level? Today a player stuck on one level can't reach any later level.
- The sweep zone around rotors is generous. Keep it, or limit rotor zones to the tiles the arms rest on?

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including mouse aim near rotors, window sizes and render resolution on weaker GPUs), a listen to the mix, WebGL, Windows (needs the module) and touch.

## Round 7 scope

Written 2026-10-07 on `improvements-7`, after confirming `main` matched `origin/main` (`02bafd4`) with a clean tree. These stay out because they wait on the owner or on a playtest: chapter VIII's theme, replacing 7-4, the unlock rule for *Watch solution* and failed attempts, how generous rotor aim should be, the audio peak limit and the trailer re-cut. Touch controls and WebGL stay on the ranked list (see the results for why). Rounds 1–6 cleared the ranked list down to owner-blocked items, so this round again started from what a player touches and found five things:

- **The game wears Unity's logo.** No icon is set in Player Settings, so the Linux build's `BorrowedSeconds_Data/Resources/UnityPlayer.png` (the window, taskbar and alt-tab icon) is the default Unity cube. The release zip has no launcher, so a player who wants it in their applications menu has to write a `.desktop` file by hand.
- **Progress can't be erased.** There's no way to start over from scratch, or to hand the game to someone else in the house, short of deleting `~/.config/unity3d/…/prefs`, which also throws away settings and key bindings.
- **A pad never vibrates.** Borrowing, the debt falling due, defaulting, a dial latching and settling a level all shake the screen and play a stinger, but on a gamepad nothing reaches your hands.
- **The mouse wheel does nothing in a level.** It turns Ledger pages, but in play a mouse player who can't (or doesn't want to) hover an obstacle has to reach for Tab, Q or E.
- **The HUD text is small on small screens.** At 1280×800 (a Steam Deck's screen, or a small laptop) the UI scales to two-thirds, so the key-hint row and the line under the watch are about 10 px tall and the tip about 13 px (`Builds/r6/shapes/1280x800`). There's no way to make them bigger.

Items run in that order; item 5 is a stretch. Scratch output stays in `Builds/r7/`. Every tool run has to leave the real save's SHA-256 and mtime unchanged (`Builds/r7/save-before.*`).

### 1. A game icon
- **Change:** `ArtSource/build_ui_assets.py` renders an icon from the game's own pocket-watch model (the title-screen emblem). It's set as the default icon in Player Settings, so the Linux player, the macOS app and a future Windows build use it. `Tools/package.sh` adds `icon.png`, a `BorrowedSeconds.desktop` launcher and a one-line install note to the Linux zip.
- **Acceptance:** in a fresh Linux build, `UnityPlayer.png` is the watch icon, not Unity's. The running game's window carries it (`_NET_WM_ICON`, read with `xprop`). The packaged zip contains `icon.png` and a `.desktop` file that passes `desktop-file-validate`.
- **Verify:** build, compare the files, read the running window's icon property, list and validate the zip; a sheet with the icon at 256, 64, 32 and 16 px, looked at before committing. **Limit:** the macOS app isn't rebuilt or run (no Mac).

### 2. Erase progress
- **Change:** a Settings row, *Erase progress*. The first press arms it (the row asks you to press again, and the arm lapses after 4 s or when you move off the row). The second press clears medals, best times, the last level, the finished flag and the retired onboarding prompts, and keeps settings and key bindings.
- **Acceptance:** one press erases nothing; two presses erase exactly those fields. Afterwards the Ledger has only 1-1 open and the title reads *Begin*. Moving off the row disarms it.
- **Verify:** a new `checks.sh` check (`erase-progress`) on the scripted run's in-memory save (scripted saves are read-only, so the real file can't be touched), and the real save's SHA-256 and mtime before and after. Screenshot of the armed row.

### 3. Controller vibration
- **Change:** short rumble pulses when you borrow, when the debt falls due, when you default, when a dial latches and when you settle a level, on the pad you're using. A Settings toggle, *Controller vibration* (on by default). The motors stop when the game pauses, loses focus or leaves a level, and stay off during *Watch solution*, the title screen's replays and scripted runs.
- **Acceptance:** with a virtual gamepad, borrowing on 1-1 sends a motor command with non-zero speeds; with the toggle off it sends none; pausing sends a stop.
- **Verify:** a new step in the input bot's gamepad pass that watches the pad's device commands (`InputSystem.onDeviceCommand`). **Limit:** a virtual pad has no motors, so nobody will have felt the pulses.

### 4. The mouse wheel aims
- **Change:** in a level, the wheel cycles the aim like Tab (down: next, up: previous). Moving the pointer goes back to hover aiming, as Tab does today.
- **Acceptance:** on a level with two or more obstacles, wheel steps move the aim forward and back through them.
- **Verify:** a step in the input bot's keyboard-and-mouse pass that scrolls a virtual mouse on 1-3 (two sliders) and reads the aim.

### 5. Stretch: larger HUD text
- **Change:** a Settings row, *HUD size* (100, 125, 150 %), that scales the level title, clock, tip, key hints, watch and the line under it. The camera's HUD keep-out boxes scale with it, so floor tiles still stay out from under the HUD.
- **Acceptance:** at 1280×800 and 150 %, the hint row and the line under the watch are at least 14 px tall, nothing in the HUD overlaps, and the autopilot reports no floor tile under the HUD (`hud=0`) on all 35 levels at 1280×800.
- **Verify:** autopilot at 1280×800 with the HUD at 150 %, screenshots at 100 % and 150 %, and the save round trip in `checks.sh`.

**Not this round:** chapter VIII, replacing 7-4, the unlock rule, rotor aim, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio peak limit and the mix by ear, human playtesting.

## Round 7 results

All five items shipped on `improvements-7`, including the stretch item. Final verification ran on the **release** build with this round's code: EditMode 145/145 (`Tools/test.sh`), autopilot 35/35 at exactly par, `checks.sh` with every check PASS (including the new `erase-progress` and `hud-size` checks; *Watch solution* 35/35; audio balance median +10.1 dB, worst +5.0 dB, peak 0.89), the input bot's passes all PASS (with the new vibration and wheel steps), `Tools/shapes.sh` at four sizes, the solver on 1-3 OK (no level data changed: `levels.json` and `solutions.json` are byte-identical to `main`), and `Tools/docs_check.py` clean. The real save's SHA-256 and mtimes were the same before and after the round (`Builds/r7/save-*`). Images are in `docs/media/improvements/round7/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | A game icon, and a launcher in the Linux zip | `42107e2` | The release build's `UnityPlayer.png` is the watch (it was the Unity cube). On KDE Plasma (Wayland) the running window showed KWin's generic "wayland" icon; with the launcher from `install-launcher.sh` installed, KWin reported the watch for the same window (decoded from its icon data). The launcher passes `desktop-file-validate`, and GLib parses its `Exec` and `Icon` back to the right files from folders whose names contain spaces, `$`, quotes, backticks and backslashes. The zip lists `icon.png` and `install-launcher.sh`. Shot 01. |
| 2 | Erase progress | `b515dd4` | `erase-progress`: one press only arms it; moving off the row disarms it; the arm lapses after 4.0 s; two presses clear medals, times, the last level, the finished flag and the onboarding prompts, keep every setting and key (the save's JSON matches the expected one), the title reads *Begin* and the Ledger opens only 1-1. Shot 02. |
| 3 | Controller vibration | `ecf75f1` | The input bot's gamepad pass reads the motor commands sent to its virtual pad: 1-1 sent a pulse each for the borrow (0.2/0.5), the debt (0.5/0.25) and the win (0.35/0.7), each followed by a stop; with the setting off a borrow sent nothing; switching it on sent a sample pulse; Start mid-pulse paused and stopped the motors. |
| 4 | The mouse wheel aims | `a3bd8f8` | The keyboard-and-mouse pass on 1-3: a pointer on nothing aims nothing; down, down, up go 0 → 1 → 0; hovering slider 1 then down aims 0; moving the pointer goes back to hover aim. |
| 5 | Stretch: HUD size 100/125/150 % | `4af8dc3` | `hud-size` at 1024×768, 1280×800 and 1600×900: two hint rows, nothing in the bottom HUD overlapping, no floor tile under the HUD, saved. At 1280×800 the hint labels go from 11.0 to 17.9 px and the line under the watch from 12 to 18 px (font size on screen). A framing-only autopilot (`-bsFrameOnly`) at 21:9, 16:9, 16:10 and 4:3 × 100/125/150 %: 35/35 levels clear of the HUD in all twelve; at 100 % every level's framing is identical to round 6. Shots 03–04. |

**Found along the way, and fixed:**
- `Tools/package.sh` would have shipped player logs: the player resolves a relative `-logFile` against its own folder, so earlier rounds' fps runs had left `Builds/Linux/Builds/r4`, `r6` logs inside the build. The zip now leaves `Builds/` out. (Those older logs are still on disk; this session didn't create them, so it didn't delete them.)
- The first version of the 150 % HUD left 1–6 tiles under the watch on 9 levels at 16:9 (the watch grew by half). The watch now grows by a quarter, and a larger HUD may pull the camera back further (1.7× at 150 % against 1.4×).
- The erase arm first counted the menus' clamped frame time, so it lasted 7 s at a low frame rate. It uses real time now.
- `inputbot.sh`'s cap went from 180 to 300 s: at about 11 fps under load the bot took 176–179 s, and one run was cut off at 180 s before its last pass.

**Notes and limits.**
- **A mistake of mine.** While building the HUD-size check, I first reused the display check's switch that lets a scripted run apply its save to the window. The sandboxed save defaults to fullscreen, so two dev runs went fullscreen (3072×1728) on the shared desktop for about 10 s each before quitting. The check now has its own switch that touches only the HUD size, and every later run stayed windowed.
- **The X11 window icon wasn't seen.** Launched under XWayland, the player hung at startup (the known issue `play.sh` works around), so `_NET_WM_ICON` was never set; I stopped my process. Unity writes the icon to `UnityPlayer.png`, which is what its X11 player reads, but that path is unverified here. For the KWin test, the launcher went into the real `~/.local/share/applications` for under a minute and was removed again.
- **Vibration is unfelt.** Virtual pads have no motors; the pulse strengths and lengths are a first guess.
- **Wheel and Tab now step on from the hovered obstacle** rather than jumping to the nearest one first. Gamepad aiming is unchanged.
- **The game assembly allows unsafe code** now, for the bot's device-command spy (the Input System hands it a raw pointer, and its rumble command type is internal).
- **The trailer's settings shot** selects rows by index; `shots.json` now points at the same rows (Reduce flashing 6 → 7, Focus slow-motion 7 → 8). The Settings rows are 52 units tall (were 62), so a re-recorded settings shot would look slightly denser. The trailer wasn't re-cut.
- **The local release zip** in `Builds/Release/` was rebuilt by `package.sh` (not published).
- **Load.** Input-sensitive runs waited for a load average under 24. The machine ran at load 18–39 during the round.

**Decisions for the owner.**
- Still open: chapter VIII's theme, replacing 7-4, whether *Watch solution* or failed attempts unlock the next level, how generous rotor aim should be, the audio peak limit (this run: 0.89 against 0.90), the trailer re-cut, Windows Build Support, signing and notarization, a license, and releases.
- A new release would be the first with the icon and the launcher; whether and when to cut one is yours.
- Should *HUD size* default to 125 % on small screens (under 1600 px wide), instead of 100 % everywhere?

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including the vibration pulses, the larger HUD on a real Steam Deck, and the icon on X11 desktops), a listen to the mix, WebGL and touch (not attempted: a WebGL build needs an audio fallback for the low-pass filter, browser verification and hosting, more than fits beside this round's items), and Windows (needs the module).

## Round 8 scope

Written 2026-10-07 on `improvements-8`, after confirming `main` matched `origin/main` (`0b00321`) with a clean tree. These stay out because they wait on the owner or on a playtest: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, whether *HUD size* defaults to 125 % on small screens, the audio peak limit and the trailer re-cut. WebGL and touch stay deferred (an audio fallback and browser testing). The ranked list is down to owner-blocked items again, so this round started from a Linux player's first launch and from players who replay levels, and found three things:

- **The released game can hang before it opens a window.** A short run of the release build on this KDE Plasma (Wayland) desktop, with no arguments but a windowed size, picked Unity's X11 backend (XWayland) and stopped after `Desktop is 3840 x 2160` with its main thread waiting in `poll` (no GL context, no window; killed after 25 s). With `-force-wayland`, or `SDL_VIDEODRIVER=wayland`, it starts. The zip's README tells the player to run `./BorrowedSeconds.x86_64` and only mentions `-force-wayland` as an afterthought, and the launcher from `install-launcher.sh` runs the bare binary. A `force-wayland=1` line in `boot.config` doesn't help (tested: still X11). So on a Wayland desktop like this one, the first thing a player does can hang.
- **The music keeps playing behind other windows.** Losing focus pauses a level (round 1), but the title, pause menu and Ledger music carry on while you're in a browser or a call.
- **A player chasing a medal can't see where they stand.** The HUD shows the clock and par, but not your best time or when gold (par + 1 s) and silver (par + 4 s) slip away. You find out on the *Settled* screen, after the run. The Ledger names a level's medal but not what the next one needs.

Items run in that order; item 4 is a stretch. Scratch output stays in `Builds/r8/` (and `Logs/`). Every tool run has to leave the real save's SHA-256 and mtime unchanged (`Builds/r8/save-before.*`). Test windows stay windowed.

### 1. A Linux launcher that avoids the XWayland hang
- **Change:** a `BorrowedSeconds.sh` next to the binary in the Linux zip. On a Wayland session it starts the game with Unity's native Wayland backend (`-force-wayland`); `BS_X11=1` skips that, and other arguments pass through. `install-launcher.sh` points the menu entry at it, the zip's README and the README's *Play it* say to run it, and `Tools/play.sh` uses the same rule.
- **Acceptance:** from an unpacked test zip, `./BorrowedSeconds.sh` (with a window size and the menu tour) starts on the Wayland backend, reaches the title and finishes the tour; with `BS_X11=1` it picks X11; with no Wayland session it passes no Wayland flag. The zip lists the script as executable, and the launcher's `Exec` names it and still passes `desktop-file-validate`.
- **Verify:** package a test zip into `Builds/r8/`, run the script there with a sandboxed config, read `Selected window backend` in each log; the launcher is generated into a throwaway `XDG_DATA_HOME`, never the real applications folder. **Limit:** the X11 path still hangs on this machine (that's Unity and XWayland, not something the game can fix before it starts), and fullscreen on the Wayland backend isn't entered on the shared desktop.

### 2. Mute when the window is in the background
- **Change:** a Settings toggle, *Mute in background* (on by default). When the window loses focus the whole mix fades out within a fraction of a second, and it fades back when focus returns. Scripted runs never touch it unless a check forces it.
- **Acceptance:** with the toggle on, a focus loss takes the listener volume to 0 and a focus gain back to 1; with it off, nothing changes; the setting survives a save round trip.
- **Verify:** a new `checks.sh` check (`background-mute`) that calls the same focus handler. **Limit:** like round 1's focus pause, no real window is alt-tabbed on the shared desktop.

### 3. Medal pace on the HUD and targets in the Ledger
- **Change:** on a level you've already settled, the line under the clock adds your best time, and a small medal coin beside the clock shows the best medal still in reach: gold until par + 1 s, then silver until par + 4 s, then bronze. The Ledger's info panel says what the next medal needs (*gold: 9.15s or better*). First attempts and *Watch solution* show neither, so a first run stays about the puzzle.
- **Acceptance:** on a settled level the coin is gold at par + 1 s and silver one tick later, silver at par + 4 s and bronze one tick later, and the line reads the saved best; on an unsettled level and while watching, neither shows. The Ledger target matches `SaveData.MedalFor`'s thresholds for every level. Framing at 100 % and 150 % HUD stays clear of the HUD.
- **Verify:** a new `checks.sh` check (`medal-pace`) on an in-memory save, the existing `hud-size` and framing checks, and screenshots.

### 4. Stretch: Reset settings
- **Change:** a Settings row that puts the volumes, shake, flashing, Focus, game speed, HUD size, render resolution, vibration and background mute back to their defaults, on two presses like *Erase progress*. Progress, key bindings and the window choice stay.
- **Acceptance:** one press changes nothing; two restore exactly those fields.
- **Verify:** an extension of the `erase-progress` check.

**Not this round:** chapter VIII, replacing 7-4, the unlock rule, rotor aim, the HUD-size default, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio peak limit and the mix by ear, human playtesting.

## Round 8 results

Items 1–3 shipped on `improvements-8`; the stretch item (4) didn't. Final verification ran on the **release** build with this round's code: EditMode 145/145 (`Tools/test.sh`), autopilot 35/35 at exactly par (every level `hud=0`), `checks.sh` with every check PASS (including the new `background-mute` and `medal-pace` checks; *Watch solution* 35/35; audio balance median +9.9 dB, worst +5.2 dB, peak 0.84), the input bot's passes all PASS, `Tools/shapes.sh` at four sizes, the solver on 1-1 OK (no level data changed), and `Tools/docs_check.py` clean. The real save's SHA-256 and mtimes were the same before and after the round (`Builds/r8/save-*`). Images are in `docs/media/improvements/round8/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | `BorrowedSeconds.sh`: the Linux zip starts Unity's native Wayland backend on a Wayland desktop | `83d8f78` | Diagnosis: the release binary run directly (windowed, sandboxed config) logged `Selected window backend: x11`, then stopped after `Desktop is 3840 x 2160` with its main thread in `poll` and no window, until killed at 25 s; three runs, the same each time. `force-wayland=1` in `boot.config` still chose X11; `SDL_VIDEODRIVER=wayland` and `-force-wayland` chose Wayland and started. From an unpacked test zip, `./BorrowedSeconds.sh` (windowed, with the menu tour) chose Wayland and finished the tour (48 captures, exit 0); `BS_X11=1` and a session without `WAYLAND_DISPLAY` both chose X11. A stub binary showed the arguments pass through intact (including one with a space) and `-force-wayland` isn't doubled. The menu entry, generated into a throwaway `XDG_DATA_HOME`, runs the script and passes `desktop-file-validate`; GLib parses its `Exec` back to the executable script. |
| 2 | *Mute in background* (Settings, on by default) | `075e45d` | `background-mute`: through the handler Unity calls on a focus change, the listener goes silent in 0.26 s and back to full in 0.26 s; with the toggle off it stays at 1.00; the row flips it; it survives a save round trip, and a save from before this round loads with it on. The Settings page now has 16 rows (49 units tall, were 52) and fits at 21:9, 16:9, 16:10 and 4:3 (shot 02). |
| 3 | Medal pace on the HUD, next-medal targets in the Ledger | `59333e8` | `medal-pace`: on 1-1 settled before (best 10.65 s), the coin by the clock is gold at tick 0 and at par + 20 ticks, silver at par + 21 and par + 80, bronze at par + 81, and the line reads *PAR 8.15 · BEST 10.65*. A level never settled shows neither, and nor does *Watch solution*. The Ledger names gold's time at silver, silver's at bronze and nothing at gold, right on 105/105 panels. The coin sits inside the clock's HUD box, so framing is unchanged: autopilot `hud=0` on all 35 levels and `hud-size` clear at 100 % and 150 %. Shot 01. |
| 4 | Stretch: *Reset settings* | — | Not done. Settings is at 16 rows after item 2; a 17th needs denser rows or a second page, which is a layout decision rather than a quick add. |

**Notes and limits.**
- **The XWayland hang is worked around, not fixed.** It happens before any game code runs, so the game can't avoid it from inside. Only the shipped script and menu entry avoid it; a player who double-clicks `BorrowedSeconds.x86_64` on a Wayland desktop like this one still gets the hang. Whether it happens on other machines (other compositors, GPUs, scaling) isn't known. Two things weren't checked here: the native Wayland backend in real fullscreen (it would cover the shared desktop), and how sharp it looks with fractional scaling (this desktop runs at 125 %, and on Wayland the player reported a 3072×1728 desktop where X11 reported 3840×2160). The published v0.1.0 zip doesn't have the script, so the README tells v0.1.0 players to add `-force-wayland`.
- **No real alt-tab.** As in round 1, the background mute was checked through the focus handler, not by moving focus on the shared desktop.
- **Pace shows only on settled levels**, so a first attempt stays about the puzzle. Rewinding past a threshold flips the coin back, since the clock rewinds too.
- **Load.** The input bot ran at load 17–23 and `checks.sh` at 21–25. `checks.sh` took 7 min 17 s against its 10-minute cap, so the cap has less headroom than before.
- **Scratch files.** The test zip, its unpacked copy and the copied build used for the `boot.config` test were deleted at the end of the round; logs stay in `Builds/r8/`.

**Decisions for the owner.**
- Still open: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size default on small screens, the audio peak limit, the trailer re-cut, Windows Build Support, signing and notarization, a license, and releases.
- A new release would be the first with `BorrowedSeconds.sh` (and the icon and launcher from round 7). Until then the release page's zip hangs on Wayland desktops like this one unless the player adds `-force-wayland`.
- *Mute in background* defaults to on. Keep it, or default to off?
- Settings has 16 rows. Further options (such as *Reset settings*) want a second page or a split into Audio / Display / Gameplay.

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including the native Wayland backend in fullscreen and on other desktops), a listen to the mix, WebGL and touch, and Windows (needs the module).

## Round 9 scope

Written 2026-10-07 on `improvements-9`, after confirming `main` matched `origin/main` (`7c8491b`) with a clean tree. These stay out because they wait on the owner or on a playtest: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size default, *Mute in background*'s default, a second Settings page (so *Reset settings* waits too), the audio peak limit, the trailer re-cut and a release. WebGL and touch stay deferred. This round looked at what a player learns from a failure and at the paths the last rounds could only check by script, and found four things:

- **A default doesn't say what happened.** Every death shows the same banner, *DEFAULTED, rewinding…*, for 0.85 s. The simulation already works out which obstacle killed you (`SimState.DeathCause`), but nothing reads it. On a level with two sliders and a laser, a new player has to work out from a rewind which one it was, and whether they thawed into it or it ran into them.
- **The *Settled* screen gives medal rules, not times.** Its bar is labelled *TIME THIEF within par + 1.0s, SILVER within par + 4.0s*, so a player on silver has to add par and a second in their head to know what to beat on *Retry*. The Ledger has named the target time since round 8; the screen you're on when you decide to retry doesn't.
- **Test windows open on the shared desktop, and so can't test some things.** Every scripted run opens a real window on the desktop the other sessions use. That has caused trouble (round 7's fullscreen runs, round 8's stray window), and it's why the focus-loss pause and background mute were only ever tested by calling their handler, and why fullscreen on the Wayland backend, 125 % scaling and the X11 path were never tried. A headless nested KWin (`kwin_wayland --virtual`, on its own D-Bus session and config folders) runs the game unchanged: a menu tour inside one took 24 s and produced the same captures as round 8's.
- **`checks.sh` is close to its time cap.** It took 7 min 17 s of its 10 minutes in round 8, and its log doesn't say which checks take the time.

Items run in this order; item 5 is a stretch. Scratch output stays in `Builds/r9/`. Every tool run has to leave the real save's SHA-256 and mtime unchanged (`Builds/r9/save-before.*`), and the real KWin config (`~/.config/kwinrc`, `kwinoutputconfig.json`) unchanged (`Builds/r9/kwin-before.sha`).

### 1. Test windows in a private compositor
- **Change:** `Tools/nested.sh` starts a headless `kwin_wayland --virtual` (its own socket, D-Bus session and config, cache and data folders under the output folder; optional size, scale and XWayland) and runs a command inside it, then stops it. The scripted tools (`checks.sh`, `inputbot.sh`, `capture.sh`, `devcap.sh`, `shapes.sh`) run the game through it when `kwin_wayland` is installed; `BS_NESTED=0` opts out. Plain `play.sh` (a person playing) is unchanged.
- **Acceptance:** each tool passes as before with its game window inside the nested compositor: the player's environment names the private socket, its log says Wayland, and no game window opens on the shared desktop. The compositor and its D-Bus exit with the run (no leftover processes). The real KWin config hashes are unchanged.
- **Verify:** run each tool; read `/proc/<pid>/environ` of the player during a run; the process list after; hashes before and after.

### 2. Real focus loss, fullscreen, 125 % scaling and X11, in the private compositor
- **Change:** checks that need a real compositor run only inside the nested one (the tools pass `-bsNested`; without it they're skipped, never run on the shared desktop):
  - `real-focus`: mid-level, the game opens a second window (`kdialog`) that takes focus, as alt-tabbing would; then closes it.
  - The `display` check also chooses *Fullscreen* and then a window size again.
- **Acceptance:** a real focus loss pauses the level (the tick holds) and fades the sound out; focus coming back fades it in, and the level stays paused until resumed. Fullscreen fills the nested output and returns to the chosen window size. The same runs at 125 % scaling (a 3840×2160 output at scale 1.25) record what size the game renders at. A nested run with XWayland records whether Unity's X11 path starts there. Defects found are fixed if they can be fixed from the game, otherwise written up.
- **Verify:** `checks.sh` inside the nested compositor at scale 1 and 1.25; a menu tour with `BS_X11=1` in a nested compositor with XWayland; captures looked at.

### 3. Say what defaulted you
- **Change:** the death banner names the obstacle and how: *you thawed inside the beam* when the debt's freeze ended inside a hazard, *the slider caught you* otherwise (slider, beam, rotor arm). The obstacle that did it glows red through the death pause and stops when the rewind starts.
- **Acceptance:** the text comes from `DeathCause`, so it names the right kind on every death. Thaw deaths say *thawed*, other deaths don't. Exactly one piece is marked during the pause, and none after.
- **Verify:** EditMode tests for the wording on every obstacle of every level; the `default-rewind` check (thaw deaths on 1-2, 2-1 and 4-5) reads the banner and the marked piece, and a new case dies by standing in 1-2's slider lane. Screenshots.

### 4. Medal times on the *Settled* screen
- **Change:** the bar's label names the times: *TIME THIEF 9.15s or better · SILVER 12.15s or better*.
- **Acceptance:** on every level the two times match `SaveData.MedalFor`'s thresholds.
- **Verify:** the `medal-pace` check reads the label for all 35 levels; screenshot.

### 5. Stretch: `checks.sh` headroom
- **Change:** each check logs how long it took; then the slowest part is cut down or the cap raised, whichever the numbers support.
- **Acceptance:** a full run finishes with at least a third of its cap to spare at load ≤ 24, and every check still passes.

**Not this round:** chapter VIII, replacing 7-4, the unlock rule, rotor aim, the HUD-size and background-mute defaults, a second Settings page and *Reset settings*, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio peak limit and the mix by ear, human playtesting.

## Round 9 results

All five items shipped on `improvements-9`, including the stretch item. Final verification ran on the **release** build with this round's code, every game window inside the private compositor: EditMode 216/216 (`Tools/test.sh`; 71 new), autopilot 35/35 at exactly par (every level `hud=0`), `checks.sh` with every in-game check PASS (including the new `death-report`, `real-focus` and `fullscreen` checks and the extended `medal-pace`; *Watch solution* 35/35), the input bot's passes all PASS, `Tools/shapes.sh` at four sizes, the solver on 1-2 OK (no level data changed; `DeathReport.cs` is in `Sim`, which the solver compiles), and `Tools/docs_check.py` clean. The audio balance failed once on its peak limit; see below. The real save's SHA-256 and mtimes, and the real `~/.config/kwinrc`, `kwinoutputconfig.json` and `kglobalshortcutsrc`, were the same before and after the round (`Builds/r9/save-*`, `kwin-*`). Images are in `docs/media/improvements/round9/`.

| # | Item | Commits | Verification |
|---|---|---|---|
| 1 | Test windows in a private headless KWin (`Tools/nested.sh`), used by `checks.sh`, `inputbot.sh`, `capture.sh` and `shapes.sh` | `ee716b9`, `4d77bf3` | During a run, the player's environment named only the private socket (`WAYLAND_DISPLAY=bs-nested-…`, no `DISPLAY`) and its log said Wayland. The input bot passed every pass inside it (load 19.6), `checks.sh` passed in full (6 min 10 s, load ~19), and a menu tour produced the same captures as round 8's on the desktop. A run cut off by `timeout` left no process or socket behind. The real KWin config files kept their hashes. Fixed along the way: `play.sh` lost a `dev` argument when re-running itself inside the compositor, so nested `dev` runs first used the release build (`4d77bf3`). |
| 2 | Real focus loss, fullscreen, 125 % scaling and X11, in the private compositor | `8731415` | `real-focus`: a `kdialog` window took focus 0.2 s after it opened; the level paused with its tick held and the sound went to 0.00; closing it gave focus back, the sound returned to 1.00, and the level stayed paused until resumed. `fullscreen`: the Display row went from a 1280×720 window to fullscreen at the output's size in 0.1 s and back. Both passed on the Wayland backend and, with `BS_X11=1`, on X11 through KWin's XWayland. Shot 03, and see the findings below. |
| 3 | A default says what did it | `a9253d6` | `death-report`: thaw deaths and other deaths on 1-2, 2-1 and 3-1 read *you thawed inside the block / the beam / a rotor arm* or *the block / the beam / a rotor arm caught you*, matching `DeathCause`; exactly the blamed piece is marked during the pause and none once the rewind starts. EditMode: the line for every obstacle of every level, and 150 seeded random games per level in which every death blames an unfrozen obstacle. Shot 01. |
| 4 | Medal times on the *Settled* screen | `667e532` | `medal-pace` reads *TIME THIEF 9.15s or better · SILVER 12.15s or better* (par + 1 s and par + 4 s) on 35/35 levels; `shapes.sh` shows it fits at 21:9 and 4:3. Shot 02. |
| 5 | Stretch: `checks.sh` headroom | `ac1aa1e` | Each check now logs its wall time. Full release run: 419 s in-game, 7 min 07 s wall at load 19–27, of which *Watch solution* took 190 s and the audio log 43 s. That left 29 % of the old 10-minute cap; the cap is now 15 minutes. *Watch solution* still plays at real speed, so it still tests what a player sees. |

**Findings from the private compositor.**
- **KWin's `--scale` doesn't emulate scaling on the virtual backend.** It multiplies the output's size (a 3840×2160 request became a 4800×2700 output at scale 1). `nested.sh` sets the real output scale with `kscreen-doctor` once KWin is up, giving 3840×2160 at 125 % (3072×1728 logical), like the development desktop, and clears KWin's saved output config before each run.
- **The Wayland backend renders fullscreen at the logical size.** At 125 % it reports a 3072×1728 desktop, its mode list stops at 2560×1600, and fullscreen renders 3072×1728, which KWin scales up by 1.25. A compositor screenshot (with `spectacle` in the private session) shows visibly softer text than X11, which renders 3840×2160 in the same compositor (shot 04). Asking for 3840×2160 on the command line came back as 3072×1728. The player's SDL2 backend has no fractional-scale support, so I found no fix from game code. Only 125 % was tried.
- **The X11 hang is particular to the development desktop.** In a fresh headless KWin with XWayland, the release build on X11 started normally, finished the menu tour and passed `real-focus` and `fullscreen`. Why the desktop's own XWayland hangs it is still unknown. I didn't attach a debugger to a hung player on the shared desktop, because if it hadn't hung, a window would have opened there.

**Notes and limits.**
- **Audio peak, one failure.** The full release run failed `balance.py` on the mixed peak, 0.93 against the 0.90 limit; median +10.9 dB and worst +5.9 dB passed. No audio code or asset changed this round. The same scripted audio measured 0.82 earlier in the round and 0.83 and 0.85 in two reruns afterwards. The limit wasn't changed; it's the owner's call (rounds 2, 5 and 7 noted the thin headroom).
- **The private compositor isn't a desktop.** It has no panel, no other applications and nobody using it, and its output is a virtual framebuffer. The focus test used a second window, not a real alt-tab. Frame rates there are capped at its 60 Hz output.
- **The culprit mark** reuses each obstacle's hover ghost in red with a slow pulse (about 1 Hz); under *Reduce flashing* it pulses the same way. The death banner now stays up for 1.5 s (was 0.7 s) when it has a cause to name, so the line can be read through the rewind.
- **Other sessions' nested compositors.** During the round, other sessions on the machine ran their own `kwin_wayland --virtual`. None of them were touched; `pgrep` and cleanup only matched this round's own `bs-nested-*` sockets and PIDs.
- **Load.** Input-sensitive runs started at a load average of 19–27 (two at about 25–26, a little over the round's guideline of 24, because the load stayed there); every one passed.

**Decisions for the owner.**
- Still open: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size and *Mute in background* defaults, a second Settings page (and with it *Reset settings*), the audio peak limit (0.82–0.93 this round), the trailer re-cut, Windows Build Support, signing and notarization, a license, and releases.
- On a fractionally scaled Wayland desktop, `BorrowedSeconds.sh` trades sharpness for starting at all: the Wayland backend is softer in fullscreen, while X11 (`BS_X11=1`) is sharp but hangs on this machine's desktop. Keep Wayland as the default?

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including the death banner's wording and the culprit mark), a listen to the mix, WebGL and touch, Windows (needs the module), why this desktop's XWayland hangs the player, and fractional scaling on the Wayland backend.

## Round 10 scope

Written 2026-10-07 on `improvements-10`, after confirming `main` matched `origin/main` (`5b29fc6`) with a clean tree. These stay out because they wait on the owner or on a playtest: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size and *Mute in background* defaults, a second Settings page (and *Reset settings*), the launcher default (Wayland or X11), the audio peak limit, the trailer re-cut and a release. WebGL and touch stay deferred. This round looked at the first seconds of a level, at a player who is stuck, and at a player who comes back to beat a time, and found three things:

- **The clock starts before you've read the board.** A level runs 0.6 s after it appears (`LevelSession.IntroTime`), so a new player reads the board while the sliders move and the debt-free first seconds tick away. Par counts ticks from tick 0, so standing still to read costs medal time, and a restart throws you straight back into motion. Holding the level at tick 0 until the player acts changes no tick count, so par, medals and every saved solution mean the same.
- **Help jumps from a one-line tip to the full answer.** A stuck player has the tip (folded behind H on ten levels) and then *Watch solution*, which plays the whole route. There's nothing in between that points at the trick and leaves the timing to the player.
- **A player chasing a medal races only a number.** Since round 8 the HUD shows your best time and the medal still in reach, but not *where* your best run was at this moment, which is what tells you where you lost the time.

Items run in this order; item 3 is the largest. Scratch output stays in `Builds/r10/`. Every tool run has to leave the real save's SHA-256 and mtime unchanged (`Builds/r10/save-before.*`), and the real KWin config unchanged (`Builds/r10/kwin-before.sha`). Every game window opens in the private compositor.

### 1. Time waits for your first move
- **Change:** a fresh start (entering a level or restarting it) holds the simulation at tick 0 until the first move, borrow or Focus press. Aiming works during the hold, so the highlight, ghost forecast and aim tag can be read before anything moves; a borrow as the first action fires on tick 0. A HUD tag says the level is waiting and what starts it. A death's automatic rewind, a manual rewind, *Watch solution*, the title's replays and scripted runs don't hold (a check switches it on for itself).
- **Acceptance:** with no input, the tick stays at 0 for 3 s and the tag shows; hovering an obstacle aims it and the aim tag shows a verdict while the tick is still 0; a move starts the clock; a borrow as the first action fires on tick 0; a restart holds again; the auto-rewind after a death doesn't. *Watch solution* still wins 35/35 at par and the autopilot 35/35 at par.
- **Verify:** a new `checks.sh` check (`ready-hold`), a new input-bot step through the real keyboard and mouse on 1-1 (hold, hover aim with verdict, then a key press starts it), the autopilot and *Watch solution*; screenshot.

### 2. A clue before the solution
- **Change:** the pause menu gains *Show a clue*, above *Watch solution*. It marks, in gold, the obstacle the solver's route borrows first and the tile where the route stands when its first debt falls due, and the tip panel says the same in words (*Clue: freeze the beam first, and be on the gold ring when your debt falls due*). It stays for the rest of your time on the level, restarts included, until you leave or choose *Hide the clue*. You still play it yourself, so times and medals count as usual. The nudge after three defaults mentions the clue before *Watch solution*.
- **Acceptance:** on all 35 levels the marked obstacle is the first borrow of `solutions.json`'s route and the marked tile is the route's tile on the tick its first debt freezes it (by replay); the words name that obstacle's kind; the mark doesn't rely on colour alone (a ring with a label). The nine-row pause menu fits at 21:9, 16:9, 16:10 and 4:3.
- **Verify:** EditMode tests of the clue for every level; a new `checks.sh` check (`clue`) that opens it through the pause menu on all 35 levels and compares the marked piece, tile and words; `Tools/shapes.sh`; screenshots.

### 3. Race your best run
- **Change:** a settled level shows a translucent ghost of your best run, moving in step with your clock. The game keeps the actions of the run that survives your rewinds (the same form as the solver's routes), and saves them with a new best time, or with any clear if none is saved yet. A pause-menu row, *Best-run ghost: On/Off*, is saved. First attempts, *Watch solution* and the title's replays show no ghost; older saves load with no ghost until the next clear.
- **Acceptance:** a win saves a run that replays in the simulation to a win at exactly the time saved; a rewind mid-run leaves only the surviving timeline in the saved run; on the next attempt the ghost stands on the run's tile on every tick, the ghost stays hidden when the toggle is off, and the toggle survives a save round trip. A save from before this round loads with no ghost.
- **Verify:** EditMode tests (run encoding round trip, a recorded run with a rewind replays to the saved tick on every level); a new `checks.sh` check (`best-ghost`); the input bot wins 1-1 through real input and checks the saved run replays to the same tick; screenshot.

**Not this round:** chapter VIII, replacing 7-4, the unlock rule, rotor aim, the HUD-size and background-mute defaults, a second Settings page and *Reset settings*, the launcher default, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio peak limit and the mix by ear, human playtesting.

## Round 10 results

All three items shipped on `improvements-10`. Final verification ran on the **release** build with this round's code, every game window inside the private compositor: EditMode 288/288 (`Tools/test.sh`, `Builds/r10/tests-final`; 72 new), autopilot 35/35 at exactly par with every level `hud=0` (`Builds/r10/capture-release/autopilot.log`, each tick compared with `solutions.json`), `checks.sh` with every check PASS, including the new `ready-hold`, `clue` and `best-ghost` checks and the real-compositor `real-focus` and `fullscreen`, *Watch solution* 35/35, audio balance median +10.5 dB, worst +5.7 dB, peak 0.81 (`Builds/r10/checks-release/checks.log`, 7 min 44 s at load 28), the input bot's passes all PASS (`Builds/r10/inputbot-release/inputbot.log`, load 21), `Tools/shapes.sh` at four sizes, the solver on 1-2 OK (no level data changed; the new `Sim` files compile in the .NET solver) and `Tools/docs_check.py` clean. The real save's SHA-256 and mtimes, and the real `~/.config/kwinrc`, `kwinoutputconfig.json` and `kglobalshortcutsrc`, were the same before and after the round (`Builds/r10/save-*`, `kwin-before.sha`). Images are in `docs/media/improvements/round10/`.

| # | Item | Commits | Verification |
|---|---|---|---|
| 1 | Time waits for the first move | `84bd450` | `ready-hold`: 1-2 still at tick 0 after 3.5 s with the *TIME WAITS* tag up, and again after a restart; *Watch solution* and a title replay ran on. Input bot (real keyboard and mouse): 1-1 held at tick 0 for 2 s; the pointer on the lane aimed the block with the aim tag reading *SAFE*; a tap of S started the clock; walking into the lane died, rewound to tick 9 and ran on with no input; a held R restarted into a fresh hold; a click as the first action borrowed on tick 0. Autopilot and *Watch solution* still win at exactly par. Shot 01. |
| 2 | A clue before the solution | `4f65c59` | `clue`: through Pause > *Show a clue* on 35/35 levels, the gold mark is on the route's first borrow (exactly one piece), the dashed ring on its first debt's tile, both labelled (*FREEZE FIRST*, *DEBT HERE*), and the tip says it in words; on 1-5 it lasts through a restart, *Hide the clue* removes it, and it's gone after leaving the level. EditMode: 35 cases check each clue against a separate replay (free the tick before, frozen on the tile at the clue's tick, before the win). Shot 02. The pause menu fits at 21:9 and 4:3 (shot 04). |
| 3 | Race your best run | `e9b1126` | `best-ghost`: after a death on 1-2 rewound to tick 98, the run kept 39 of 48 actions, all before 98; a win on 1-1 at 163 saved 19 actions that replay to a win at 163; on the next attempt the ghost matched the run's tile and freeze on 163/163 ticks; the pause row hid it, the save kept *Off* and the run, *On* brought it back; a save from before this round loads with no ghost; an unsettled level and *Watch solution* show none. Input bot: the real-input win on 1-1 saved 15 actions that replay to a win at tick 185. EditMode: on every level, the route with a random detour that a rewind takes back records as the route and replays to par; the text format round-trips and refuses damaged text. Shot 03. |
| — | `nested.sh` stops its session's leftovers | `20d6628` | See below. After the fix, an input bot run, the full `checks.sh`, the autopilot and `shapes.sh` left no process behind. |

**Found along the way, and fixed: leftover processes from the private compositor.** Every `Tools/nested.sh` run left two D-Bus-started helpers running after its session ended (`ksecretd` and `xdg-desktop-portal`), so the machine collected one pair per test run. A re-run into the same output folder then failed outright: a leftover kept recreating its data folder while the tool deleted it. `nested.sh` now stops, when the session ends, exactly the processes whose environment names that run's private data folder. I stopped the 22 leftovers from this round's own runs before the fix (PIDs in `Builds/r10/ksecretd-killed.txt`, each checked against its `Builds/r10/…` folder). Leftovers from round 9's runs (folders under `Builds/r9/`) and from other games' sessions are still running; this session didn't start them, so it didn't stop them.

**Notes and limits.**
- **Ticks didn't change.** The start hold only stops the clock before tick 0 runs, so par, medals and every saved solution mean the same. A first borrow at tick 0 was always possible in the simulation; the solver's routes don't need the hold. *Focus* also starts the clock (slowly), so a player who holds Shift out of habit isn't stuck.
- **The clue comes from the solver's route,** so it names *one* way through. Where several routes exist, a player may have found another; the clue doesn't say it's the only one. Its gold glow on the obstacle is faint on some pieces (the hover ghost, lit gold), and the dial-coloured ring is faint on a dial; the two labels carry it.
- **The ghost shows the run's pawn only.** Its own frozen obstacles aren't drawn, so on 1-1 it walks through a block that is frozen in its run but moving in yours. Older saves get a ghost from their next clear; a clear slower than the saved best is kept only if no run was kept yet, and then the ghost runs at that clear's own time.
- **The pause menu has nine rows** (was seven). Scripts that pick pause rows by position were updated (the input bot); the trailer's shot list only selects Settings rows, so it's unaffected, but a re-recorded pause shot would show the new rows. The trailer wasn't re-cut.
- **Load.** Timing- and input-sensitive runs started at a load average of 19–37. Several were over the guideline of 24, because the load stayed there: the final full `checks.sh` at 28, dev check runs at 32–37 and a dev input bot run at 29. The final input bot (21), autopilot (23) and `shapes.sh` (21) runs were under it. Every final run passed.

**Decisions for the owner.**
- Still open: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size and *Mute in background* defaults, a second Settings page (and *Reset settings*), the launcher default (Wayland or X11), the audio peak limit (0.81 this round), the trailer re-cut, Windows Build Support, signing and notarization, a license, and releases.
- The level now waits for the first move by default, with no setting to turn it off (Settings is full). Keep it as a rule of the game, or make it an option once Settings has room?
- Should the clue, or the best-run ghost, ever be off-limits (for example, only after a few defaults, like the nudge)? Today both are always in the pause menu.
- Round 9 left leftover compositor helpers running on the shared machine (under `Builds/r9/`); they can be stopped by whoever owns that run.

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including the start hold, the clue and the best-run ghost), a listen to the mix, WebGL and touch, Windows (needs the module), why this desktop's XWayland hangs the player, and fractional scaling on the Wayland backend.

## Round 11 scope

Written 2026-10-08 on `improvements-11`, after confirming `main` matched `origin/main` (`faee7a9`) with a clean tree. These stay out because they wait on the owner or on a playtest: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size and *Mute in background* defaults, a second Settings page (and *Reset settings*, and a setting for the start hold), whether the clue or ghost should wait for a few defaults, the launcher default (Wayland or X11), the audio peak limit, the trailer re-cut and a release. WebGL and touch stay deferred. This round read the code for moments where the game knows something the player needs and doesn't say it, and found four:

- **A sealed exit says nothing.** On the 19 levels with gold dials the exit stays shut until every dial is latched. A player who walks onto it early just stands there: nothing says why it won't let them leave, or how many dials are still open. The same goes for arriving in debt: the rules say you pay on the exit and leave as you thaw, but nothing on screen says so.
- **The *Settled* screen doesn't say what you gained.** A new best shows *NEW BEST* but not by how much, which is the number a player chasing a medal wants.
- **The clue stops after the first loan.** It marks the route's first borrow and first debt tile. Five levels need more than one loan (1-3, 4-2, 4-5, 7-3 and 7-5, among the hardest in the game), and there a player who follows the clue is left with nothing between it and the full solution.
- **The best-run ghost walks through things for no visible reason.** It draws only the pawn, so on 1-1 it walks through a block that its run froze but that's moving in yours. Round 10 noted this as a limit.

Items run in this order. Scratch output stays in `Builds/r11/`. Every tool run has to leave the real save's SHA-256 and mtime unchanged (`Builds/r11/save-before.*`), and the real KWin config unchanged (`Builds/r11/kwin-before.sha`). Every game window opens in the private compositor.

### 1. The exit says why you can't leave yet
- **Change:** standing on a sealed exit shows a pill over the player: *Exit sealed: latch every dial (1 of 2 latched)*. Standing on an open exit with a debt still to pay shows *Pay your debt here: you leave as you thaw*. These show on every attempt, not only until learned, and never in replays.
- **Acceptance:** on every dial level, the pill shows while the player stands on the sealed exit and names the right count; it's gone once the exit opens or the player steps off. On a level where the route reaches the exit in debt, the debt pill shows. No pill on any other tile.
- **Verify:** a new `checks.sh` check (`exit-pill`) that puts the player on the exit before the dials latch on each dial level and reads the pill; EditMode tests of the wording; screenshot.

### 2. The *Settled* screen says what you gained
- **Change:** a new best reads *NEW BEST −0.40s* (by how much it beat the old one).
- **Acceptance:** the ribbon shows the difference to the saved best, to the tick; a first clear still says *FIRST CLEAR* and a slower clear still shows the best.
- **Verify:** the `medal-pace` check reads the ribbon after a first clear, a slower clear and a faster clear; screenshot.

### 3. The clue follows your loans
- **Change:** the clue becomes one step per loan of the solver's route. Before a borrow it marks the obstacle (*FREEZE FIRST*, then *FREEZE NEXT*) and the tile where that loan's debt freezes the route (*DEBT HERE*); once you've borrowed, the obstacle mark goes and the tile stays until you thaw; after you thaw it moves on to the next loan. The step comes from the loans you've taken, so a rewind takes it back. The tip names the step (*loan 2 of 3*). Single-loan levels behave as before, except the obstacle mark goes while your loan is out.
- **Acceptance:** for every loan of every route, the step's obstacle is that borrow's target and its tile is the route's tile on the tick that loan's debt freezes it (by replay). In game, following the route on each multi-loan level, the mark and ring match the step for the loans taken on every tick.
- **Verify:** EditMode tests on all 35 levels (each step against a separate replay); the `clue` check extended to play the route on the five multi-loan levels and compare every tick; screenshot.

### 4. The best-run ghost shows what it froze
- **Change:** while the ghost's run has an obstacle frozen, a violet crystal outline of it is drawn where it froze (a block on its tile, a laser's bar, a rotor's arms), so the ghost's path through it makes sense and you can see where and when your best borrowed.
- **Acceptance:** on every tick, the outlines shown are exactly the obstacles frozen in the run on that tick, at the run's positions; none show with the ghost off, on a first attempt, or while watching the solution.
- **Verify:** the `best-ghost` check extended to compare the outlines with the run's frozen obstacles on every tick of 1-1 and of a laser and a rotor level; screenshot.

**Not this round:** chapter VIII, replacing 7-4, the unlock rule, rotor aim, the HUD-size and background-mute defaults, a second Settings page and *Reset settings*, the launcher default, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio peak limit and the mix by ear, human playtesting.

## Round 11 results

All four items shipped on `improvements-11`. Final verification ran on commit `dea40d5` (the last code commit; the commit after it changes only docs and images), on the **release** build, with every game window inside the private compositor:
- **EditMode:** 379/379 (`Tools/test.sh`, `Builds/r11/tests-final`; 91 new).
- **Autopilot:** 35/35 at exactly par, every level `hud=0` (`Builds/r11/capture-release/autopilot.log`, each tick compared with `solutions.json`).
- **`checks.sh`:** every check PASS, including the new `exit-pill`, the extended `clue`, `best-ghost` and `medal-pace`, and the real-compositor `real-focus` and `fullscreen`. *Watch solution* 35/35; audio balance median +10.9 dB, worst +6.0 dB, peak 0.86. The run took 572 s in game, 9 min 40 s wall (`Builds/r11/checks-release/checks.log`).
- **Input bot:** every pass PASS (`Builds/r11/inputbot-release/inputbot.log`).
- **`Tools/shapes.sh`:** four sizes, each level pass (`Builds/r11/shapes`).
- **Solver:** 1-2 and 4-2 OK (`Builds/r11/validate.log`). No level data changed; the new and changed `Sim` files compile in the .NET solver.
- **`Tools/docs_check.py`:** clean.

The real save's SHA-256 and mtimes, and the real `~/.config/kwinrc`, `kwinoutputconfig.json` and `kglobalshortcutsrc`, were the same before and after the round (`Builds/r11/save-*`, `kwin-before.sha`). No process from this round's runs was left running. Images are in `docs/media/improvements/round11/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | The exit says why you can't leave yet | `58d2c87` | `exit-pill`: on 18 of the 19 dial levels, a run that reaches the exit before its dials latch shows *Exit sealed: latch every dial (0 of 2 latched)* (or *latch the dial first*), matching the simulation's count. Each run is the solver's route up to some tick and then straight for the exit, or seeded play. On 1-4, stepping off clears the pill. 2-1's route waits on the open exit in debt and reads *Pay your debt here: you leave as you thaw*; the pill is gone after the win. No exit pill showed off the exit. EditMode: the wording and counts on all 19 dial levels, and along all 35 routes, every tick spent standing on the exit without winning has one of the two reasons. Shots 01, 02. |
| 2 | *Settled* says what you gained | `f6a3b4f` | `medal-pace`: real wins on 1-1 at par read *FIRST CLEAR* with no saved best, *NEW BEST −0.75s* over a best 0.75 s slower, and show no ribbon over a faster best. The ribbon widens to fit. Shot 05; `shapes.sh` shows it at 4:3. |
| 3 | The clue follows your loans | `60e5ca3` | `clue`: still 35/35 through the pause menu. On the five multi-loan levels (1-3, 4-2, 4-5, 7-3, 7-5), playing the route with the clue on, the mark, ring, label (*FREEZE FIRST* / *FREEZE NEXT*) and tip matched the loans taken on all 890 sampled ticks; *FREEZE NEXT* showed on 39 free ticks. EditMode: on all 35 levels, one step per borrow, each with its target and the tile the route is frozen on at that debt (by replay); the step at each borrow and each freeze; past the last loan it stays on the last; an earlier state, which is what a rewind restores, is back on step 0. Shot 03. |
| 4 | The best-run ghost shows what it froze | `dea40d5` | `best-ghost`: on 1-1 (block), 4-3 (lit laser bar) and 3-1 (rotor arms), the violet crystals drawn on each of 162, 161 and 169 drawn ticks were exactly the run's frozen obstacles, at the place a separate replay gives (60 ticks with one frozen on each). The round-10 parts of the check still pass. Shot 04. |

**Notes and limits.**
- **6-3's exit can't be reached sealed.** The exit sits beyond the dial on a one-tile lane, and the only way past the lane's block is the debt trick, which also latches the dial. Neither the route-then-exit search nor 200,000 seeded runs (in a scratch program) put a player there with the dial unlatched. Its wording is covered by the EditMode test.
- **The exit note is common.** On 11 of the 35 routes (2-1, 2-2, 3-1, 4-2, 4-3, 5-1 to 5-4, 6-1 and 6-2) the solver's route reaches the open exit with its debt still running and waits there, so the *Pay your debt here* pill is something most players will see. It shows over the pawn, where the onboarding pills go, and takes priority over them.
- **The clue moves on when you can borrow again,** which is on the tick you thaw. The routes of 1-3, 4-5, 7-3 and 7-5 borrow on that very tick, so on those routes *FREEZE NEXT* is only seen by a player slower than the solver. On a one-loan level, the obstacle's mark now goes while your loan is out and comes back after you thaw. Before, it came back as soon as the block unfroze.
- **The ghost's crystals use the ghost's violet.** Where your run freezes the same thing in the same place, your own cyan crystal covers it.
- ***Settled* doesn't show the old best on a slower clear.** That was already the case, and is unchanged: the ribbon only appears for a first clear or a new best. The scope's line saying a slower clear "still shows the best" was wrong about the existing behaviour.
- **The trailer wasn't re-cut.** If it is re-recorded, a replay that waits on an exit with prompts on would now show the exit pill.
- **Load.** The release `checks.sh` started at load 24 and the 5-minute average rose to 41 during it (other sessions); it passed. The input bot ran at 21–25 and the autopilot at 23–32. Development runs during the round went up to 29.

**Decisions for the owner.**
- Still open: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size and *Mute in background* defaults, a second Settings page (and *Reset settings*, and a setting for the start hold), whether the clue or the ghost should wait for a few defaults, the launcher default (Wayland or X11), the audio peak limit (0.86 this round), the trailer re-cut, Windows Build Support, signing and notarization, a license, and releases.
- The exit pill shows on every attempt, not just until learned. Keep it that way, or retire it after a few levels like the onboarding pills?

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including the exit pill, the clue's steps and the ghost's crystals), a listen to the mix, WebGL and touch, Windows (needs the module), why this desktop's XWayland hangs the player, and fractional scaling on the Wayland backend.

## Round 12 scope

Written 2026-10-08 on `improvements-12`, after confirming `main` matched `origin/main` (`cd4ac0b`) with a clean tree. This round's focus is polish: graphics, interface and the feel of a finished release, with a Graphics Fidelity setting required. Gameplay, balance, levels and story don't change. The items come from a baseline menu tour and level captures of the current build (`Builds/r12/menus-before`), which found:

- **There's no graphics setting.** The look is fixed: 4096 soft shadows, 4× MSAA with SMAA, high-quality bloom. Weaker GPUs can only lower *Render resolution*, and nothing uses what a strong GPU could add (ambient occlusion, a deeper void, light from the glowing pieces).
- **Settings is full and says nothing about its rows.** It has 16 rows and no room for another, and a row like *Render resolution* or *Mute in background* doesn't explain itself.
- **The HUD shows through every menu window.** The HUD only dims to 25 % behind *Paused*, *Settings* and *How to play*, so the tip panel, the key row and the pocket watch show through and cross the windows' text and buttons (*Back* sits over the tip; the Settings footer over *THAW HERE*). The watch's *LOAN READY* is wider than the ring around it, which cuts its first and last letters.
- **The board doesn't read against the void.** The plinth under the floor is nearly the void's colour, so a level reads as loose tiles floating in the dark rather than a finished diorama. PLAN §13 planned a brass-trimmed plinth edge that was never built.

Items run in this order. Scratch output stays in `Builds/r12/`. Every tool run has to leave the real save's SHA-256 and mtime unchanged (`Builds/r12/save-before.*`) and the real KWin config unchanged (`Builds/r12/kwin-before.sha`). Every game window opens in the private compositor.

### 1. Graphics fidelity: Low, Medium, High, Ultra
- **Change:** a *Graphics fidelity* slider with four steps, saved with the other settings. *High* is the default and is exactly today's look. Each step sets shadows (type, resolution, cascades), anti-aliasing (MSAA and SMAA/FXAA), bloom resolution and filtering, the particle count, and the pocket watch's render texture. *Ultra* adds screen-space ambient occlusion, a depth-of-field blur on the void below the board (the board itself stays sharp), point lights from the glowing pieces (the pawn's core, the exit, lit lasers), 8× MSAA, an 8192 shadow map and 1.5× particles. *Low* drops to hard 1024 shadows, FXAA, quarter-resolution bloom and half the particles. *Render resolution* stays a separate control.
- **Acceptance:** each step reaches URP (MSAA, shadow resolution, the SSAO feature's state, the volume's bloom and depth of field) and survives a save round trip; a save from before this round loads as *High*. Same-frame screenshots of every step show the differences, and the frame time falls from Ultra to Low.
- **Verify:** a new scripted mode (`Tools/fidelity.sh`) that holds one level on a fixed tick and screenshots it at each step, then measures frame times at each step on three levels with vsync off (with the load noted); a new `fidelity` check in `checks.sh` that steps the row and reads the pipeline; EditMode tests of the step table and the old-save default. Screenshots and a table of the steps, what each changes and its frame time in the results.

### 2. Settings explain themselves, and Display gets its own page
- **Change:** *Graphics fidelity* takes the *Display* row's place in Settings, and *Display* and *Render resolution* move to a *Display* page opened from the row after it, so every existing setting stays reachable and the main list keeps its 16 rows (the rows after it keep their places). A line under the list says what the selected row does, and for *Graphics fidelity* what the selected step changes.
- **Acceptance:** every row on both pages has a line that fits the window at 16:9 and 4:3; the page opens and closes with mouse, keyboard and pad; the `display` check passes through the new page.
- **Verify:** the `display` and `fidelity` checks; the input bot's gamepad pass reads the Settings footer; `Tools/shapes.sh` and screenshots at 1600×900 and 1024×768.

### 3. Menus clear the HUD
- **Change:** while a menu window is open, the HUD's lower half (the tip, the key row, the watch and its line) fades out instead of dimming to 25 %, and the top corners dim further; they come back when the window closes. The watch's small label fits inside its ring.
- **Acceptance:** with *Paused*, *Settings*, *Controls* or *How to play* open, the lower HUD's alpha is 0 and no HUD text overlaps the window; after *Resume* the HUD is back at full. *LOAN READY* fits the ring's inner width.
- **Verify:** a check that opens each window and reads the HUD's alphas, and screenshots before and after.

### 4. A finished board
- **Change:** a brass trim runs along every outer edge of the board, with a small tick at each tile, and the plinth below gets a lighter, lit face that fades into the void, so the board reads as one diorama piece. Built in code from the level's tiles, with no new imported assets.
- **Acceptance:** every exposed floor or wall edge on every level gets trim, and no trim sits between two tiles; the autopilot still wins 35/35 at par and every level still frames clear of the HUD.
- **Verify:** an EditMode test of the edge rule on every level; autopilot screenshots of every level; `shapes.sh`.

**Not this round:** chapter VIII, replacing 7-4, the unlock rule, rotor aim, the HUD-size and background-mute defaults, *Reset settings*, a start-hold setting, the launcher default, touch controls, WebGL, Windows (module), re-cutting the trailer, the audio peak limit and the mix by ear, human playtesting.

## Round 12 results

All four items shipped on `improvements-12`, plus two fixes that the final checks turned up. Final verification ran on commit `60a209f` (the last code commit; the commit after it changes only docs and images), on the **release** build, with every game window inside the private compositor:
- **EditMode:** 421/421 (`Tools/test.sh`, `Builds/r12/tests-final`; 42 new).
- **Autopilot:** 35/35 at exactly par, every level `hud=0` (`Builds/r12/capture-release/autopilot.log`, each tick compared with `solutions.json`).
- **`checks.sh`:** every check PASS, including the new `fidelity`, `settings-help`, `menus-clear-hud` and `camera-hitch`, the reworked `display` and `fullscreen`, and *Watch solution* 35/35; audio balance median +10.3 dB, worst +5.7 dB, peak 0.84. 596 s in game, 604 s wall, at load 24.7 falling to 22.8 (`Builds/r12/checks-release/checks.log`).
- **Input bot:** every pass PASS, at load 20.8 (`Builds/r12/inputbot-release/inputbot.log`).
- **`Tools/shapes.sh`:** four sizes, each level pass (`Builds/r12/shapes`).
- **`Tools/fidelity.sh`:** same-frame shots of every step and frame times (`Builds/r12/fidelity-release`, `Builds/r12/fidelity-final-1920x1080`, `Builds/r12/fidelity-final-2560x1440`).
- **Solver:** 1-2 and 4-2 OK (`Builds/r12/validate.log`). No level data changed.
- **`Tools/docs_check.py`:** clean.

The real save's SHA-256 and mtimes, and the real `~/.config/kwinrc`, `kwinoutputconfig.json` and `kglobalshortcutsrc`, were the same before and after the round (`Builds/r12/save-*`, `kwin-before.sha`). No process from this round's runs was left running. Images are in `docs/media/improvements/round12/`.

| # | Item | Commit | Verification |
|---|---|---|---|
| 1 | Graphics fidelity: Low, Medium, High (default), Ultra | `4b85c89`, `60a209f` | `fidelity`: the Settings row steps Low to Ultra, and at each step the URP asset's MSAA, shadow resolution and cascades, the camera's anti-aliasing, the sun's shadow type, the volume's bloom, the SSAO feature's state and settings, Ultra's glow lights (3 on 4-5), the particle multiplier and the watch's texture match the table; each survives a save round trip, a save without the key loads as High, and the row goes back to High. `Tools/fidelity.sh`: the same held frame of 4-5, 2-3 and 3-5 at every step (shots 01–03) and the frame times below. EditMode: four steps from Low to Ultra, High equal to the values the game always had, each step costing at least the one below, a line per step that fits, the old-save default, and bloom kept high-quality. |
| 2 | Settings explain themselves; Display gets its own page | `4485e6d` (the page itself landed with item 1) | `settings-help`: 19 rows on the two pages each put a line under the list that fits its box, at 1600×900 (and 3072×1728 in an earlier run); the *Graphics fidelity* line names the step and what it changes at each of the four. `display` and `fullscreen` now go through *Settings > Display* and its *Back*. `shapes.sh` shows both at 21:9, 16:9, 16:10 and 4:3. The input bot's Settings footer and Controls row are unchanged. Shot 04. |
| 3 | Menus clear the HUD | `4568ff6` | `menus-clear-hud`: on 2-1, the HUD's lower half reads 0.00 and the top 0.35 behind *Paused*, *Settings*, *Display*, *Controls* and *How to play*, and both are back at 1.00 after *Resume*; *LOAN READY* fits the ring's inner width. Shot 05 (before and after, same pause screen). |
| 4 | A finished board | `14baf19` | EditMode: on all 35 levels the trim runs along exactly the tile sides that face void or the map's edge, once each, counted both ways. Autopilot 35/35 at par with `hud=0` (the trim and plinth sit below the floor, so framing is unchanged). Shot 06 (same frame of 4-5 before and after). |
| – | The camera survives long frames | `ab401c7` | `camera-hitch`: after a punch-in, eight 400 ms frames leave the spring at a peak of 0.025 and the board on screen. The same check on the old spring: a peak of 6.6×10¹⁰. |

**The fidelity steps.** Same held frame of 4-5 (tick 196) at each step: shots 01 and 02. Frame times are the median over six five-second runs per step (4-5, 3-5 and 2-4, each played twice, Low to Ultra then back), vsync off, release build, in the private compositor.

| Step | Shadows | Anti-aliasing | Ambient occlusion | Bloom | Extras | Median frame, 1920×1080 (load 21→16) | Median frame, 2560×1440 (load 16→15) |
|---|---|---|---|---|---|---|---|
| Low | hard, 1024, 1 cascade | FXAA | off | quarter size | particles ×0.5, watch texture 384 px 2× MSAA | 10.2 ms (5.1–12.3) | 11.4 ms (9.0–16.1) |
| Medium | soft, 2048, 2 cascades | 2× MSAA + SMAA medium | half size, fast blur | half size | particles ×0.75, watch 512 px 4× | 12.3 ms (9.2–19.5) | 13.9 ms (11.8–17.7) |
| High (default, the original look) | soft, 4096, 2 cascades | 4× MSAA + SMAA high | full size, intensity 0.4 | half size | particles ×1, watch 512 px 8× | 12.9 ms (11.3–14.1) | 14.6 ms (10.0–19.1) |
| Ultra | soft, 8192, 2 cascades | 8× MSAA + SMAA high | full size, intensity 0.9, wider | half size | point lights from the pawn, the exit and lit beams; particles ×1.5; watch 1024 px 8× | 12.5 ms (11.9–22.2) | 15.0 ms (12.3–21.3) |

**Notes and limits.**
- **The frame times are noisy.** The iGPU was shared with other sessions throughout (earlier runs at load 45–65 were discarded), and single runs of the same step varied by up to 2×. The medians order Low < Medium < High < Ultra at 2560×1440; at 1920×1080 High and Ultra were within the noise of each other. Nothing was measured on a weaker GPU.
- **SSAO was already on.** The scope said Ultra would add ambient occlusion; the main renderer turned out to have URP's SSAO on all along (intensity 0.4), so it is part of High's look. Low turns it off, Medium halves its resolution, Ultra deepens it. Only settings that need no other shader variant change per step: the sample count is a variant URP strips, so it stays at medium.
- **No depth of field.** Ultra was planned with a far-only blur on the void. The void sits so close behind the board in depth that a blur leaving the whole board sharp barely reached it (shots looked identical), and URP strips the depth-of-field shaders from builds when no volume profile in the project uses them. It was dropped rather than shipped as an effect that costs time and shows nothing.
- **Bloom stays high-quality on every step.** Turning bloom's high-quality filtering off on Low and Medium removed the beams' glow in builds, because URP only keeps the bloom variants the project's volume profiles use. Found in the first release shots (`60a209f` fixes it); Low uses quarter-size bloom instead.
- **The camera fix.** The first full release `checks.sh` (at load 34) failed `aim-reach` with every tile "off screen" and `game-speed` at 10.9 ticks/s: from the chapter-cards check on, the 3D view was black. The camera's punch-in spring was stepped with the raw frame time, which overshoots and grows on frames longer than about 0.15 s and eventually reaches NaN. The bug predates this round; slow frames under load (and slightly heavier level builds with the trim) set it off. Both checks passed alone, and the whole run passed after the fix.
- **The crystal sparkle changed.** Each sparkle is now a soft round glint at the centre of its cell instead of the whole square cell, which read as blocky pixels on the watch glass and on frozen pieces.
- **The HUD's lower half also fades behind *Settled*.** The time stays at the top right, dimmed. A faint dark disc in the void below the board, there before this round, wasn't traced.
- **Settings is no longer full.** The *Display* page has room, and Settings has none to spare (16 rows and the help line).
- **The trailer wasn't re-cut.** Its settings shot selects rows 1, 7 and 8, which didn't move.
- **Load.** Release runs waited for a load average under 24 where timing mattered; the machine ran at 15–80 during the round.

**Decisions for the owner.**
- Still open: chapter VIII's theme, replacing 7-4, the unlock rule, rotor aim, the HUD-size and *Mute in background* defaults, *Reset settings* and a start-hold setting (the *Display* page now has room), whether the clue or the ghost should wait for a few defaults, whether the exit tag retires, the launcher default, the audio peak limit (0.84 this round), the trailer re-cut, Windows Build Support, signing and notarization, a license, and releases.
- Keep *High* as the default, or pick the step at first launch from the GPU? Ultra's lights and the board's new trim are worth a look on a real screen.

Still open: chapter VIII (owner: theme) and IX–XII, chapter VII's shared idea (7-2/7-4), a trailer re-cut, human playtesting (now including the new look and the Settings lines), a listen to the mix, Graphics fidelity on a weaker GPU, WebGL and touch, Windows (needs the module), why this desktop's XWayland hangs the player, and fractional scaling on the Wayland backend.
