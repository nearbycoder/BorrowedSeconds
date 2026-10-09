// Checks the browser build in a headless browser: it loads to the title screen with no page or console errors.
// Exits 0 only then. With --play it also checks what a browser player depends on: sound starts after the first
// click (with the browsers' autoplay rule emulated, see autoplayPolicy), a settings change (Graphics fidelity,
// master volume) survives a reload, and level 1-1 is played from the keyboard to a win that is still saved after
// another reload. --play changes the site's saved game in that browser
// profile, so it is meant for a fresh headless profile (each run gets one), not a player's browser.
//
//   node Tools/check-pages.mjs https://nearbycoder.github.io/BorrowedSeconds/     the live site
//   node Tools/check-pages.mjs --local [--play]                                     Builds/Pages, served under /BorrowedSeconds/
//   options: --browser chromium|firefox|webkit (default chromium)   --out DIR (default Logs/pages-<browser>)
//
// Needs playwright-core: PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core, else the newest one found under
// ~/Sites/*/node_modules. Firefox is the system Firefox (FIREFOX_PATH, default /usr/bin/firefox) over WebDriver BiDi,
// which needs playwright-core 1.63 or later; Chromium is CHROMIUM_PATH or the newest cached Chromium (or headless
// shell) in ~/.cache/ms-playwright. Browser temp profiles go under Logs/tmp, not the shared /tmp.
import { createRequire } from "node:module";
import { createServer } from "node:http";
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";

const require = createRequire(import.meta.url);
const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), "..");
const argv = process.argv.slice(2);
const opt = (name, def) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : def; };
const flag = (name) => argv.includes(name);
const engine = opt("--browser", "chromium");
const local = flag("--local");
const play = flag("--play");
let url = argv.find((a, i) => !a.startsWith("--") && !["--browser", "--out"].includes(argv[i - 1]));
if (!url && !local) {
  console.error("usage: node Tools/check-pages.mjs <url> | --local  [--play] [--browser chromium|firefox|webkit] [--out dir]");
  process.exit(2);
}
const out = path.resolve(opt("--out", path.join(root, "Logs", "pages-" + engine)));
mkdirSync(out, { recursive: true });
process.env.TMPDIR = path.join(root, "Logs", "tmp");
mkdirSync(process.env.TMPDIR, { recursive: true });

function loadPlaywright() {
  if (process.env.PLAYWRIGHT_CORE) return require(process.env.PLAYWRIGHT_CORE);
  try { return require("playwright-core"); } catch (e) { /* look further */ }
  const sites = path.join(os.homedir(), "Sites");
  const found = [];
  for (const d of existsSync(sites) ? readdirSync(sites) : []) {
    const p = path.join(sites, d, "node_modules", "playwright-core");
    try { found.push({ p, v: JSON.parse(readFileSync(path.join(p, "package.json"), "utf8")).version.split(".").map(Number) }); } catch (e) { }
  }
  found.sort((a, b) => b.v[0] - a.v[0] || b.v[1] - a.v[1] || b.v[2] - a.v[2]);
  if (!found.length) throw new Error("playwright-core not found: set PLAYWRIGHT_CORE");
  return require(found[0].p);
}

// the newest cached full Chromium (new headless mode), else the newest headless shell
function cachedChromium() {
  const cache = path.join(os.homedir(), ".cache", "ms-playwright");
  const dirs = existsSync(cache) ? readdirSync(cache) : [];
  const newest = (prefix) => dirs.filter((d) => d.startsWith(prefix)).sort((a, b) => Number(b.split("-")[1]) - Number(a.split("-")[1]));
  for (const d of newest("chromium-")) {
    const exe = path.join(cache, d, "chrome-linux64", "chrome");
    if (existsSync(exe)) return exe;
  }
  for (const d of newest("chromium_headless_shell-")) {
    const exe = path.join(cache, d, "chrome-headless-shell-linux64", "chrome-headless-shell");
    if (existsSync(exe)) return exe;
  }
  return undefined;
}

// Builds/Pages under /BorrowedSeconds/ as GitHub Pages serves it: static files, case-sensitive paths, no
// Content-Encoding header (the loader decompresses the Brotli files itself)
const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".png": "image/png", ".md": "text/markdown",
                ".txt": "text/plain", ".json": "application/json", ".wasm": "application/wasm" };
function serveLocal() {
  const site = path.join(root, "Builds", "Pages");
  if (!existsSync(path.join(site, "index.html"))) throw new Error("no build in Builds/Pages: run Tools/build-pages.sh");
  const server = createServer((req, res) => {
    let p = decodeURIComponent(new URL(req.url, "http://x").pathname);
    if (p === "/BorrowedSeconds") { res.writeHead(301, { Location: "/BorrowedSeconds/" }); return res.end(); }
    if (!p.startsWith("/BorrowedSeconds/")) { res.writeHead(404); return res.end("not found"); }
    p = p.slice("/BorrowedSeconds/".length);
    if (p === "" || p.endsWith("/")) p += "index.html";
    const file = path.join(site, p);
    if (!file.startsWith(site + path.sep) || !existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); return res.end("not found"); }
    // validators and max-age as Pages sends them, so a reload can revalidate the cached build
    const st = statSync(file);
    const etag = `"${st.size.toString(16)}-${Math.floor(st.mtimeMs).toString(16)}"`;
    const head = { "ETag": etag, "Last-Modified": st.mtime.toUTCString(), "Cache-Control": "max-age=600" };
    if (req.headers["if-none-match"] === etag ||
        (req.headers["if-modified-since"] && Date.parse(req.headers["if-modified-since"]) >= Math.floor(st.mtimeMs / 1000) * 1000)) {
      res.writeHead(304, head);
      return res.end();
    }
    const body = readFileSync(file);
    res.writeHead(200, { ...head, "Content-Type": types[path.extname(file)] || "application/octet-stream", "Content-Length": body.length });
    res.end(body);
  });
  return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(server)));
}

// an analyser on every AudioContext that reaches the speakers: is the game producing sound?
const audioProbe = () => {
  const analysers = [];
  const connect = AudioNode.prototype.connect;
  AudioNode.prototype.connect = function (dest, ...rest) {
    const r = connect.call(this, dest, ...rest);
    try {
      if (typeof AudioDestinationNode !== "undefined" && dest instanceof AudioDestinationNode) {
        const ctx = dest.context;
        if (!ctx.__probe) {
          ctx.__probe = ctx.createAnalyser();
          ctx.__probe.fftSize = 2048;
          const mute = ctx.createGain();
          mute.gain.value = 0;
          connect.call(ctx.__probe, mute);
          connect.call(mute, ctx.destination);
          analysers.push(ctx.__probe);
        }
        connect.call(this, ctx.__probe);
      }
    } catch (e) { /* never break the game's audio */ }
    return r;
  };
  window.__audio = () => {
    let peak = 0;
    const buf = new Float32Array(2048);
    for (const a of analysers) {
      a.getFloatTimeDomainData(buf);
      for (let i = 0; i < buf.length; i++) peak = Math.max(peak, Math.abs(buf[i]));
    }
    return { peak, contexts: analysers.length, states: analysers.map((a) => a.context.state) };
  };
};

// Headless browsers under automation let pages autoplay. --play puts the rule players' browsers apply back: an
// AudioContext starts suspended and neither it nor an <audio> element may start before the page has had a click or
// key press. Activation is tracked from trusted input here: under automation navigator.userActivation is already set.
const autoplayPolicy = () => {
  let activated = false;
  for (const type of ["pointerdown", "mousedown", "keydown", "touchend"])
    window.addEventListener(type, (e) => { if (e.isTrusted) activated = true; }, true);
  const active = () => activated;
  const AC = window.AudioContext;
  const resume = AC.prototype.resume;
  AC.prototype.resume = function () {
    return active() ? resume.call(this) : Promise.reject(new DOMException("autoplay blocked", "NotAllowedError"));
  };
  window.AudioContext = function (...args) {
    const ctx = new AC(...args);
    if (!active()) ctx.suspend();
    return ctx;
  };
  window.AudioContext.prototype = AC.prototype;
  if (window.webkitAudioContext) window.webkitAudioContext = window.AudioContext;
  const play = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function () {
    return active() || this.muted ? play.call(this) : Promise.reject(new DOMException("autoplay blocked", "NotAllowedError"));
  };
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
function check(ok, what) {
  results.push({ ok: !!ok, what });
  console.log(`[Pages] ${ok ? "PASS" : "FAIL"} ${what}`);
}

async function main() {
  const pw = loadPlaywright();
  let server;
  if (local) {
    server = await serveLocal();
    url = `http://127.0.0.1:${server.address().port}/BorrowedSeconds/`;
  }
  const launch = { headless: true };
  if (engine === "chromium") {
    launch.executablePath = process.env.CHROMIUM_PATH || cachedChromium();
    // the real GPU through ANGLE/Vulkan where there is one, instead of SwiftShader
    launch.args = ["--use-angle=vulkan", "--enable-features=Vulkan", "--ignore-gpu-blocklist", "--autoplay-policy=user-gesture-required"];
  }
  if (engine === "firefox") {
    launch.channel = "moz-firefox";
    launch.executablePath = process.env.FIREFOX_PATH || "/usr/bin/firefox";
  }
  const log = [];
  const errors = [];
  let browser;
  try {
    browser = await pw[engine].launch(launch);
    console.log(`[Pages] ${engine} ${browser.version()} -> ${url}`);
    const context = await browser.newContext({ viewport: { width: 1600, height: 900 } });
    await context.addInitScript(audioProbe);
    if (play) await context.addInitScript(autoplayPolicy);
    const page = await context.newPage();
    page.on("console", (m) => {
      log.push(`[${m.type()}] ${m.text()}`);
      if (m.type() === "error") errors.push(m.text());
    });
    page.on("pageerror", (e) => { log.push("[pageerror] " + e); errors.push(String(e)); });
    page.on("requestfailed", (r) => { log.push(`[requestfailed] ${r.url()} ${r.failure()?.errorText}`); });
    page.on("response", (r) => { if (r.status() >= 400) { log.push(`[http ${r.status()}] ${r.url()}`); errors.push(`HTTP ${r.status()} ${r.url()}`); } });
    const shot = (name) => page.screenshot({ path: path.join(out, name + ".png") }).catch(() => {});
    const seen = (text, from = 0) => log.slice(from).find((l) => l.includes(text));
    const waitLog = async (text, ms, from = 0) => {
      for (const end = Date.now() + ms; Date.now() < end; await sleep(200)) { const l = seen(text, from); if (l) return l; }
      return null;
    };
    const key = async (k, hold = 40) => { await page.keyboard.down(k); await sleep(hold); await page.keyboard.up(k); };

    // loads to the title screen
    const load = async (label) => {
      const from = log.length;
      const t0 = Date.now();
      await page.goto(url, { waitUntil: "load", timeout: 120000 });
      await page.waitForFunction(() => document.body.dataset.state !== "loading", null, { timeout: 600000, polling: 500 });
      const state = await page.evaluate(() => document.body.dataset.state);
      const title = state === "running" ? await waitLog("[Web] title screen", 120000, from) : null;
      const secs = (Date.now() - t0) / 1000;
      const bytes = await page.evaluate(() => performance.getEntriesByType("resource").concat(performance.getEntriesByType("navigation"))
        .reduce((s, e) => s + (e.transferSize || 0), 0));
      console.log(`[Pages] ${label}: page ${state}, ${title ? "title reached" : "no title"} in ${secs.toFixed(1)} s, ` +
                  `${(bytes / 1048576).toFixed(1)} MB transferred`);
      if (state === "error") console.log("[Pages] the page says: " + await page.evaluate(() => document.querySelector("#status").textContent));
      return { state, title, secs, bytes };
    };

    const first = await load("first load");
    console.log("[Pages] renderer: " + await page.evaluate(() => {
      const gl = document.createElement("canvas").getContext("webgl2");
      if (!gl) return "no WebGL2";
      const ext = gl.getExtension("WEBGL_debug_renderer_info");
      return ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
    }));
    await sleep(3000); // the title assembles; late errors show up here
    await shot("01_title");
    check(first.state === "running" && first.title, `reaches the title screen (${first.title ?? "no [Web] title screen line"})`);
    check(errors.length === 0, `no page or console errors${errors.length ? ": " + errors.slice(0, 5).join(" | ") : ""}`);

    if (play) {
      const park = async () => page.mouse.move(1590, 890); // the pointer off the menus, which select on hover

      // sound: browsers hold audio until a click or key press; after one the title music plays
      // the loudest sample over a few seconds (the music is a loop, effects are short)
      const listen = async (ms) => {
        let a = await page.evaluate(() => window.__audio());
        for (const end = Date.now() + ms; Date.now() < end; await sleep(100)) {
          const b = await page.evaluate(() => window.__audio());
          a = { ...b, peak: Math.max(a.peak, b.peak) };
        }
        return a;
      };
      const before = await listen(2000);
      await park();
      await page.mouse.click(1590, 890);
      const after = await listen(3000);
      console.log(`[Pages] audio before input: ${JSON.stringify(before)}; after a click: ${JSON.stringify(after)}`);
      check(before.peak === 0 && after.peak > 0.01 && after.states.includes("running"),
            `audio waits for input, then the title music plays (peak ${before.peak.toFixed(4)} -> ${after.peak.toFixed(4)})`);

      // settings: Graphics fidelity one step down and master volume one step down, then a reload
      const fidBefore = /Graphics fidelity (\w+)/.exec(first.title ?? "")?.[1];
      const volBefore = Number(/master volume (\d+)%/.exec(first.title ?? "")?.[1]);
      for (let i = 0; i < 3; i++) { await key("ArrowDown"); await sleep(150); } // Begin/Continue, Levels, How to play, Settings
      await key("Enter");
      await sleep(1800);
      await key("ArrowLeft"); // Master volume
      await sleep(300);
      for (let i = 0; i < 3; i++) { await key("ArrowDown"); await sleep(150); } // Music, Effects, Graphics fidelity
      await key("ArrowLeft");
      await sleep(1500);
      await shot("02_settings");
      await key("Escape");
      await sleep(2500); // PlayerPrefs reach IndexedDB
      const second = await load("reload");
      const fidAfter = /Graphics fidelity (\w+)/.exec(second.title ?? "")?.[1];
      const volAfter = Number(/master volume (\d+)%/.exec(second.title ?? "")?.[1]);
      check(second.title && fidAfter && fidAfter !== fidBefore && volAfter === volBefore - 10,
            `settings survive a reload (Graphics fidelity ${fidBefore} -> ${fidAfter}, master volume ${volBefore}% -> ${volAfter}%)`);
      console.log(`[Pages] cached reload: ${second.secs.toFixed(1)} s, ${(second.bytes / 1048576).toFixed(1)} MB transferred`);

      // play: Begin, then level 1-1 from the keyboard, following its solved run (Levels/solutions.json)
      await sleep(2000);
      await park();
      await page.mouse.click(1590, 890);
      await sleep(500);
      const from = log.length;
      await key("Enter"); // Begin
      const ready = await waitLog("[Web] level 1-1 ready", 30000, from);
      check(ready, "Begin starts level 1-1");
      if (ready) {
        await sleep(1500);
        const sol = JSON.parse(readFileSync(path.join(root, "Assets", "Resources", "Levels", "solutions.json"), "utf8"))
          .levels.find((l) => l.id === "1-1");
        const keys = { 1: "ArrowUp", 2: "ArrowRight", 3: "ArrowDown", 4: "ArrowLeft" };
        await key("Tab"); // aim at the slider (Borrow freezes the aimed obstacle); aiming doesn't start the clock
        await sleep(400);
        const t0 = Date.now();
        for (const [tick, act] of sol.actions) {
          // the game's clock starts a frame or so after the first press, so press a little late: a move
          // pressed during the last one is queued, but one pressed while still frozen is dropped
          const due = t0 + tick * 50 + 40;
          if (due > Date.now()) await sleep(due - Date.now());
          // the last step comes as the debt's freeze ends: hold it, as a player would, so it lands once thawed
          const last = tick === sol.actions[sol.actions.length - 1][0];
          await key(act >= 16 ? "Space" : keys[act], last ? 900 : 30);
          if (tick === 0) await shot("03_borrow");
        }
        const end = await Promise.race([waitLog("settled in", 20000, from), waitLog("defaulted", 20000, from)]);
        await sleep(2500);
        await shot("04_end");
        check(end && end.includes("settled"), `level 1-1 played from the keyboard to a win (${end ?? "no result in 20 s"})`);
        await sleep(2000);
        const third = await load("reload after the win");
        const cleared = Number(/(\d+) levels cleared/.exec(third.title ?? "")?.[1]);
        check(cleared >= 1, `the win is saved across a reload (${cleared} levels cleared)`);
      }
      check(errors.length === 0, `still no page or console errors${errors.length ? ": " + errors.slice(0, 5).join(" | ") : ""}`);
    }
    await context.close();
  } catch (e) {
    check(false, "the check ran: " + (e?.message ?? e));
  } finally {
    writeFileSync(path.join(out, "console.log"), log.join("\n") + "\n");
    writeFileSync(path.join(out, "result.json"), JSON.stringify({ engine, url, play, results }, null, 2) + "\n");
    if (browser) await browser.close().catch(() => {});
    if (server) server.close();
  }
  const ok = results.length > 0 && results.every((r) => r.ok);
  console.log(`[Pages] ${ok ? "OK" : "FAILED"}: ${results.filter((r) => r.ok).length}/${results.length} checks; log ${path.join(out, "console.log")}`);
  process.exit(ok ? 0 : 1);
}

main();
