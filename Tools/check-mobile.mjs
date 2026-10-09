// Checks the browser build as phones and tablets get it, in headless browsers with device profiles: WebKit as an
// iPhone 15 or an iPad Pro 11 (coarse pointer, touch, WebGL 2, no WebGPU) and Chromium as a Pixel 7. It loads the
// page to the title, measures what a phone pays for it (download, peak memory, frame rate), and with --play drives a
// short session with real touch events through the on-screen controls: sound after the first tap, a menu by tap,
// level 1-1 started, stepped with the d-pad (and walked by holding it), an obstacle aimed by a tap and frozen with
// Borrow, Focus and Rewind held, the hint toggled, Restart held and Pause tapped. Screenshots and the console go to
// --out. The desktop profiles (chromium, firefox) check the controls never show there, even after a mouse or keys.
//
//   node Tools/check-mobile.mjs --profile iphone|ipad|android|desktop-chromium|desktop-firefox [--play] [--out DIR] [--site DIR]
//   --site serves another build folder (default Builds/Pages), e.g. a copy of an older build to compare with
//
// Memory: the wasm heap (Module.HEAPU32), what the page asked the GPU for (textures, buffers and renderbuffers,
// counted from the WebGL calls with their mip chains), JS heap where the browser says (Chromium), and the peak
// resident memory of the browser's page process (WebKitWebProcess, or Chromium's renderer and GPU processes) from
// /proc. iOS isn't emulated: nothing here enforces its per-tab limit, so the numbers are compared with it instead.
// The iPhone profile hides the desktop-only S3TC/BPTC/RGTC texture formats, as an iPhone's WebKit has none, so the
// game takes the path a real iPhone does. WebKit is the WebKit build at ~/.cache/webkit-libs/webkit-2359 (WEBKIT_PATH).
// Playwright and the local server as in Tools/check-pages.mjs; temp profiles go under Logs/tmp.
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
const profileName = opt("--profile", "iphone");
const play = flag("--play");
const out = path.resolve(opt("--out", path.join(root, "Logs", "mobile-" + profileName)));
mkdirSync(out, { recursive: true });
process.env.TMPDIR = path.join(root, "Logs", "tmp");
mkdirSync(process.env.TMPDIR, { recursive: true });

function loadPlaywright() {
  if (process.env.PLAYWRIGHT_CORE) return require(process.env.PLAYWRIGHT_CORE);
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

function cachedChromium() {
  const cache = path.join(os.homedir(), ".cache", "ms-playwright");
  const dirs = existsSync(cache) ? readdirSync(cache) : [];
  for (const d of dirs.filter((d) => d.startsWith("chromium-")).sort((a, b) => Number(b.split("-")[1]) - Number(a.split("-")[1]))) {
    const exe = path.join(cache, d, "chrome-linux64", "chrome");
    if (existsSync(exe)) return exe;
  }
  return undefined;
}

// Builds/Pages under /BorrowedSeconds/, as Pages serves it (see Tools/check-pages.mjs)
const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".png": "image/png", ".md": "text/markdown",
                ".txt": "text/plain", ".json": "application/json", ".wasm": "application/wasm" };
function serveLocal() {
  const site = path.resolve(opt("--site", path.join(root, "Builds", "Pages")));
  if (!existsSync(path.join(site, "index.html"))) throw new Error(`no build in ${site}: run Tools/build-pages.sh`);
  const server = createServer((req, res) => {
    let p = decodeURIComponent(new URL(req.url, "http://x").pathname);
    if (!p.startsWith("/BorrowedSeconds/")) { res.writeHead(404); return res.end("not found"); }
    p = p.slice("/BorrowedSeconds/".length);
    if (p === "" || p.endsWith("/")) p += "index.html";
    const file = path.join(site, p);
    if (!file.startsWith(site + path.sep) || !existsSync(file) || !statSync(file).isFile()) { res.writeHead(404); return res.end("not found"); }
    const body = readFileSync(file);
    res.writeHead(200, { "Content-Type": types[path.extname(file)] || "application/octet-stream", "Content-Length": body.length,
                         "Cache-Control": "max-age=600" });
    res.end(body);
  });
  return new Promise((resolve) => server.listen(Number(opt("--port", 0)), "127.0.0.1", () => resolve(server)));
}

// In the page before anything else: what the page asks the GPU for, frames, sound and the iPhone's texture formats.
const pageProbe = (hideDesktopFormats) => {
  // --- GPU allocations, from the WebGL calls (estimates: the driver may pad or keep more)
  const bpp = { 0x8058: 4, 0x1908: 4, 0x8C43: 4, 0x8051: 3, 0x1907: 3, 0x881A: 8, 0x8814: 16, 0x822F: 8, 0x8230: 16, 0x822D: 2,
                0x822E: 4, 0x8229: 1, 0x822B: 2, 0x8D62: 2, 0x8056: 2, 0x8057: 2, 0x8C3A: 4, 0x8C3D: 4, 0x8059: 4, 0x81A5: 2,
                0x81A6: 4, 0x8CAC: 4, 0x88F0: 4, 0x8CAD: 8, 0x1902: 4, 0x1909: 1, 0x190A: 2, 0x1906: 1, 0x8F97: 4 };
  const gpu = { tex: 0, buf: 0, rb: 0, peak: 0, uploads: {}, compressed: 0, uncompressed: 0 };
  const sizes = new WeakMap(); // object -> bytes
  const state = new WeakMap(); // context -> bindings
  const st = (gl) => { let s = state.get(gl); if (!s) state.set(gl, s = { unit: 0, tex: {}, buf: {}, rb: null }); return s; };
  const note = () => { gpu.peak = Math.max(gpu.peak, gpu.tex + gpu.buf + gpu.rb); };
  const setSize = (obj, kind, bytes) => {
    if (!obj) return;
    const old = sizes.get(obj) || 0;
    sizes.set(obj, bytes);
    gpu[kind] += bytes - old;
    note();
  };
  const levelBytes = (w, h, fmt) => Math.max(1, w) * Math.max(1, h) * (bpp[fmt] || 4);
  const blockBytes = (w, h, bytes) => bytes; // compressed uploads say their size
  for (const proto of [window.WebGL2RenderingContext?.prototype, window.WebGLRenderingContext?.prototype].filter(Boolean)) {
    const wrap = (name, f) => { const orig = proto[name]; if (orig) proto[name] = function (...a) { try { f.call(this, ...a); } catch (e) { } return orig.apply(this, a); }; };
    wrap("activeTexture", function (u) { st(this).unit = u; });
    wrap("bindTexture", function (t, tex) { st(this).tex[st(this).unit + ":" + t] = tex; });
    wrap("bindBuffer", function (t, b) { st(this).buf[t] = b; });
    wrap("bindRenderbuffer", function (t, r) { st(this).rb = r; });
    const tex = (gl, target) => {
      const bindTarget = target >= 0x8515 && target <= 0x851A ? 0x8513 : target; // cube faces share the cube map
      return st(gl).tex[st(gl).unit + ":" + bindTarget];
    };
    wrap("texStorage2D", function (target, levels, fmt, w, h) {
      let b = 0;
      for (let l = 0; l < levels; l++) b += levelBytes(w >> l, h >> l, fmt);
      if (target === 0x8513) b *= 6;
      setSize(tex(this, target), "tex", b);
      gpu.uploads["storage:" + fmt.toString(16)] = (gpu.uploads["storage:" + fmt.toString(16)] || 0) + b;
    });
    wrap("texStorage3D", function (target, levels, fmt, w, h, d) {
      let b = 0;
      for (let l = 0; l < levels; l++) b += levelBytes(w >> l, h >> l, fmt) * (target === 0x8C1A ? d : Math.max(1, d >> l));
      setSize(tex(this, target), "tex", b);
    });
    // texImage2D per level: keep a per-texture sum by level
    const levelMap = new WeakMap();
    const addLevel = (gl, target, level, bytes, compressed) => {
      const t = tex(gl, target);
      if (!t) return;
      let m = levelMap.get(t);
      if (!m) levelMap.set(t, m = {});
      m[target + ":" + level] = bytes;
      setSize(t, "tex", Object.values(m).reduce((a, b) => a + b, 0));
      if (compressed) gpu.compressed += bytes; else gpu.uncompressed += bytes;
    };
    wrap("texImage2D", function (target, level, fmt, w, h, ...rest) {
      if (typeof w !== "number") { const src = w === undefined ? null : rest.length ? rest[rest.length - 1] : h; // (target, level, ifmt, fmt, type, source)
        const img = arguments[5]; if (img && img.width) addLevel(this, target, level, img.width * img.height * 4, false); return; }
      addLevel(this, target, level, levelBytes(w, h, fmt), false);
    });
    wrap("compressedTexImage2D", function (target, level, fmt, w, h, border, data, offset, len) {
      const bytes = typeof len === "number" ? len : data && data.byteLength !== undefined ? data.byteLength : (typeof data === "number" ? data : 0);
      addLevel(this, target, level, bytes, true);
      gpu.uploads["compressed:" + fmt.toString(16)] = (gpu.uploads["compressed:" + fmt.toString(16)] || 0) + bytes;
    });
    // bufferData(target, size | data, usage[, srcOffset, length]): WebGL 2 callers pass the whole wasm heap with an
    // offset and a length in elements
    wrap("bufferData", function (target, sizeOrData, usage, srcOffset, length) {
      let b = 0;
      if (typeof sizeOrData === "number") b = sizeOrData;
      else if (sizeOrData) {
        const el = sizeOrData.BYTES_PER_ELEMENT || 1;
        b = typeof length === "number" && length > 0 ? length * el : sizeOrData.byteLength - (srcOffset || 0) * el;
      }
      setSize(st(this).buf[target], "buf", b);
    });
    wrap("renderbufferStorage", function (t, fmt, w, h) { setSize(st(this).rb, "rb", levelBytes(w, h, fmt)); });
    wrap("renderbufferStorageMultisample", function (t, samples, fmt, w, h) { setSize(st(this).rb, "rb", levelBytes(w, h, fmt) * Math.max(1, samples)); });
    wrap("deleteTexture", function (t) { setSize(t, "tex", 0); });
    wrap("deleteBuffer", function (b) { setSize(b, "buf", 0); });
    wrap("deleteRenderbuffer", function (r) { setSize(r, "rb", 0); });
    if (hideDesktopFormats) {
      // an iPhone's WebKit offers ASTC, ETC and PVRTC, not the desktop block formats
      const hidden = /s3tc|bptc|rgtc/i;
      const ge = proto.getExtension, gs = proto.getSupportedExtensions;
      proto.getExtension = function (n) { return hidden.test(n) ? null : ge.call(this, n); };
      proto.getSupportedExtensions = function () { return (gs.call(this) || []).filter((n) => !hidden.test(n)); };
    }
  }
  window.__gpu = () => ({ ...gpu, now: gpu.tex + gpu.buf + gpu.rb });

  // --- the wasm heap: each growth, so the console shows what the game was doing when it grew
  if (window.WebAssembly && WebAssembly.Memory) {
    const grow = WebAssembly.Memory.prototype.grow;
    WebAssembly.Memory.prototype.grow = function (pages) {
      const r = grow.call(this, pages);
      console.log(`[probe] wasm heap grew to ${(this.buffer.byteLength / 1048576).toFixed(0)} MB`);
      return r;
    };
  }

  // --- frames
  let frames = [];
  const tick = (t) => { frames.push(t); if (frames.length > 2000) frames = frames.slice(-1000); requestAnimationFrame(tick); };
  requestAnimationFrame(tick);
  window.__fps = (ms) => { const end = performance.now(); const f = frames.filter((t) => t > end - ms); return f.length > 1 ? (f.length - 1) * 1000 / (f[f.length - 1] - f[0]) : 0; };

  // --- sound: an analyser on every AudioContext that reaches the speakers
  const analysers = [];
  const connect = AudioNode.prototype.connect;
  AudioNode.prototype.connect = function (dest, ...rest) {
    const r = connect.call(this, dest, ...rest);
    try {
      if (dest instanceof AudioDestinationNode) {
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
    } catch (e) { }
    return r;
  };
  window.__audio = () => {
    let peak = 0;
    const buf = new Float32Array(2048);
    for (const a of analysers) { a.getFloatTimeDomainData(buf); for (let i = 0; i < buf.length; i++) peak = Math.max(peak, Math.abs(buf[i])); }
    return { peak, states: analysers.map((a) => a.context.state) };
  };

  // --- the autoplay rule as iOS applies it: sound may start only from a touchend (or a key press or click), not from
  // a touchstart or a touch's pointerdown; headless browsers under automation would otherwise let it play at once
  let activated = false;
  const activate = (e) => { if (e.isTrusted) activated = true; };
  window.addEventListener("touchend", activate, true);
  window.addEventListener("keydown", activate, true);
  window.addEventListener("click", activate, true);
  window.addEventListener("pointerup", (e) => { if (e.pointerType !== "mouse") activate(e); }, true);
  window.addEventListener("mousedown", (e) => { if (!e.sourceCapabilities?.firesTouchEvents) activate(e); }, true);
  const AC = window.AudioContext || window.webkitAudioContext;
  if (AC) {
    const resume = AC.prototype.resume;
    AC.prototype.resume = function () {
      if (activated) return resume.call(this);
      return Promise.reject(new DOMException("autoplay blocked: no touchend yet", "NotAllowedError"));
    };
    const Wrapped = function (...args) { const ctx = new AC(...args); if (!activated) ctx.suspend(); return ctx; };
    Wrapped.prototype = AC.prototype;
    window.AudioContext = Wrapped;
    if (window.webkitAudioContext) window.webkitAudioContext = Wrapped;
  }
};

// resident memory of the browser's page and GPU processes, from /proc: the newest descendants of this node process
function procTree() {
  const kids = {};
  for (const d of readdirSync("/proc")) {
    if (!/^\d+$/.test(d)) continue;
    try {
      const s = readFileSync(`/proc/${d}/stat`, "utf8");
      const ppid = Number(s.slice(s.lastIndexOf(")") + 2).split(" ")[1]);
      (kids[ppid] ||= []).push(Number(d));
    } catch (e) { }
  }
  const all = [];
  const walk = (p) => { for (const k of kids[p] || []) { all.push(k); walk(k); } };
  walk(process.pid);
  return all.map((pid) => {
    try {
      const cmd = readFileSync(`/proc/${pid}/cmdline`, "utf8").split("\0");
      const status = readFileSync(`/proc/${pid}/status`, "utf8");
      const kb = (k) => Number((new RegExp(k + ":\\s+(\\d+)").exec(status) || [])[1] || 0);
      // Chromium rewrites its command line into one string, so look in all of it
      const all = cmd.join(" ");
      const name = (/(WPEWebProcess|WPEGPUProcess|WPENetworkProcess|WebKitWebProcess|WebKitGPUProcess)/.exec(all) || [])[1]
        || ((/--type=([\w-]+)/.exec(all) || [])[1] ? "chromium " + /--type=([\w-]+)/.exec(all)[1] : path.basename(cmd[0].split(" ")[0]));
      return { pid, cmd: name, rss: kb("VmRSS"), hwm: kb("VmHWM") };
    } catch (e) { return null; }
  }).filter(Boolean);
}
const pageProcs = (list) => list.filter((p) => /WPEWebProcess|WPEGPUProcess|WebKitWebProcess|WebKitGPUProcess|chromium (renderer|gpu-process)|^firefox|^contentproc/.test(p.cmd));

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
function check(ok, what) {
  results.push({ ok: !!ok, what });
  console.log(`[Mobile] ${ok ? "PASS" : "FAIL"} ${what}`);
}
const MB = (b) => (b / 1048576).toFixed(1) + " MB";

async function main() {
  const pw = loadPlaywright();
  const desktop = profileName.startsWith("desktop");
  const profiles = {
    iphone: { engine: "webkit", device: "iPhone 15", landscape: true },
    ipad: { engine: "webkit", device: "iPad Pro 11", landscape: true },
    android: { engine: "chromium", device: "Pixel 7", landscape: true },
    "desktop-chromium": { engine: "chromium" },
    "desktop-firefox": { engine: "firefox" },
    "desktop-webkit": { engine: "webkit" },
  };
  const prof = profiles[profileName];
  if (!prof) throw new Error("unknown profile " + profileName);
  const server = await serveLocal();
  const url = `http://127.0.0.1:${server.address().port}/BorrowedSeconds/`;
  const launch = { headless: true };
  if (prof.engine === "webkit") launch.executablePath = process.env.WEBKIT_PATH || path.join(os.homedir(), ".cache", "webkit-libs", "webkit-2359", "pw_run.sh");
  if (prof.engine === "chromium") {
    launch.executablePath = process.env.CHROMIUM_PATH || cachedChromium();
    launch.args = ["--use-angle=vulkan", "--enable-features=Vulkan", "--ignore-gpu-blocklist", "--enable-precise-memory-info"];
  }
  if (prof.engine === "firefox") { launch.channel = "moz-firefox"; launch.executablePath = process.env.FIREFOX_PATH || "/usr/bin/firefox"; }

  const log = [];
  const errors = [];
  const measures = { profile: profileName };
  let browser;
  let peakRss = 0, peakRssProcs = "";
  const sampler = setInterval(() => {
    const procs = pageProcs(procTree());
    const sum = procs.reduce((s, p) => s + p.rss, 0);
    if (sum > peakRss) { peakRss = sum; peakRssProcs = procs.map((p) => `${p.cmd.trim()} ${Math.round(p.rss / 1024)} MB`).join(", "); }
  }, 250);
  try {
    browser = await pw[prof.engine].launch(launch);
    console.log(`[Mobile] ${profileName}: ${prof.engine} ${browser.version()} -> ${url}`);
    let ctxOpts = { viewport: { width: 1600, height: 900 } };
    if (prof.device) {
      const d = pw.devices[prof.device];
      ctxOpts = { ...d };
      if (prof.landscape) ctxOpts.viewport = { width: d.viewport.height, height: d.viewport.width };
      if (prof.landscape && d.screen) ctxOpts.screen = { width: d.screen.height, height: d.screen.width };
    }
    const context = await browser.newContext(ctxOpts);
    if (!flag("--no-probe")) await context.addInitScript(pageProbe, profileName === "iphone" && !flag("--keep-s3tc"));
    // This WPE WebKit build can't decode the game's AAC sound (decodeAudioData fails: its GStreamer finds no decoder)
    // and its page process then aborts within half a minute, with or without this script's probes; a phone's WebKit
    // decodes AAC natively. So WebKit runs go without Web Audio (the game runs silent, as with no audio device) unless
    // --webaudio, and sound is checked in Chromium.
    const noWebAudio = prof.engine === "webkit" && !flag("--webaudio");
    if (noWebAudio) {
      await context.addInitScript(() => { delete window.AudioContext; delete window.webkitAudioContext; });
      console.log("[Mobile] Web Audio is off in this WebKit (it can't decode AAC and its page process aborts): sound is checked in Chromium");
    }
    measures.webAudio = !noWebAudio;
    const page = await context.newPage();
    page.on("dialog", (d) => { log.push("[dialog] " + d.message()); d.dismiss().catch(() => {}); });
    page.on("console", (m) => {
      log.push(`[${m.type()}] ${m.text()}`);
      // without Web Audio the game can't decode its sounds: expected here, and counted apart
      if (m.type() === "error" && noWebAudio && /^Loading FSB failed for audio clip/.test(m.text())) { measures.soundErrors = (measures.soundErrors || 0) + 1; return; }
      if (m.type() === "error") errors.push(m.text());
    });
    page.on("pageerror", (e) => { log.push("[pageerror] " + e); errors.push(String(e)); });
    page.on("crash", () => { log.push("[crash] the page crashed"); errors.push("page crashed"); });
    let downloaded = 0;
    page.on("response", async (r) => {
      if (r.status() >= 400) { log.push(`[http ${r.status()}] ${r.url()}`); errors.push(`HTTP ${r.status()} ${r.url()}`); }
      const len = Number((await r.allHeaders().catch(() => ({})))["content-length"] || 0);
      downloaded += len;
    });
    const shot = (name) => flag("--no-shots") ? Promise.resolve() : page.screenshot({ path: path.join(out, name + ".png") }).catch(() => {});
    const seen = (text, from = 0) => log.slice(from).find((l) => l.includes(text));
    const waitLog = async (text, ms, from = 0) => {
      for (const end = Date.now() + ms; Date.now() < end; await sleep(200)) { const l = seen(text, from); if (l) return l; }
      return null;
    };
    const memory = async (label) => {
      const m = await page.evaluate(() => ({
        heap: window.unityInstance?.Module?.HEAPU32?.buffer?.byteLength || 0,
        js: performance.memory ? performance.memory.usedJSHeapSize : 0,
        gpu: window.__gpu(),
      }));
      const procs = pageProcs(procTree());
      const rss = procs.reduce((s, p) => s + p.rss, 0) * 1024;
      const hwm = procs.reduce((s, p) => s + p.hwm, 0) * 1024;
      console.log(`[Mobile] memory at ${label}: wasm heap ${MB(m.heap)}, GPU now ${MB(m.gpu.now)} (textures ${MB(m.gpu.tex)}, buffers ${MB(m.gpu.buf)}, ` +
                  `renderbuffers ${MB(m.gpu.rb)}; peak ${MB(m.gpu.peak)}), JS heap ${m.js ? MB(m.js) : "n/a"}, page processes ${MB(rss)} resident ` +
                  `(peak ${MB(hwm)}: ${procs.map((p) => `${p.cmd.trim()} ${Math.round(p.hwm / 1024)} MB`).join(", ")})`);
      measures[label] = { heap: m.heap, gpuNow: m.gpu.now, gpuPeak: m.gpu.peak, tex: m.gpu.tex, js: m.js, rss, hwm,
                          uploads: m.gpu.uploads, compressed: m.gpu.compressed, uncompressed: m.gpu.uncompressed };
      return measures[label];
    };

    // load to the title
    const t0 = Date.now();
    await page.goto(url, { waitUntil: "load", timeout: 120000 });
    await page.waitForFunction(() => document.body.dataset.state !== "loading", null, { timeout: 600000, polling: 500 });
    const state = await page.evaluate(() => document.body.dataset.state);
    const title = state === "running" ? await waitLog("[Web] title screen", 120000) : null;
    measures.loadSeconds = (Date.now() - t0) / 1000;
    measures.download = downloaded;
    console.log(`[Mobile] page ${state}, ${title ? "title reached" : "no title"} in ${measures.loadSeconds.toFixed(1)} s, ${MB(downloaded)} downloaded`);
    if (state === "error") console.log("[Mobile] the page says: " + await page.evaluate(() => document.querySelector("#status").textContent));
    const env = await page.evaluate(() => {
      const gl = document.createElement("canvas").getContext("webgl2");
      const ext = gl && gl.getExtension("WEBGL_debug_renderer_info");
      return { coarse: matchMedia("(pointer: coarse)").matches, fine: matchMedia("(any-pointer: fine)").matches, webgl2: !!gl, webgpu: "gpu" in navigator,
               renderer: gl ? (ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER)) : "none",
               formats: gl ? gl.getSupportedExtensions().filter((n) => /compressed/.test(n)).join(" ") : "", w: innerWidth, h: innerHeight, dpr: devicePixelRatio,
               canvas: (() => { const c = document.querySelector("#unity-canvas"); return c.width + "x" + c.height; })() };
    });
    console.log(`[Mobile] ${JSON.stringify(env)}`);
    measures.env = env;
    await sleep(4000); // the title assembles
    await shot("01_title");
    check(state === "running" && title, `reaches the title screen (${title ?? "no [Web] title screen line"})`);
    await memory("title");
    measures.fpsTitle = await page.evaluate(() => window.__fps(3000));
    console.log(`[Mobile] frame rate on the title: ${measures.fpsTitle.toFixed(1)} fps (headless, software-paced; not a phone's)`);

    const touchUi = () => page.evaluate(() => {
      const t = document.querySelector("#touch");
      if (!t) return { present: false, shown: false };
      const r = t.getBoundingClientRect();
      return { present: true, shown: getComputedStyle(t).display !== "none" && getComputedStyle(t).visibility !== "hidden" && r.width > 0 };
    });

    if (desktop) {
      const ui = await touchUi();
      check(!ui.shown, `the on-screen controls are hidden on the desktop (${JSON.stringify(ui)})`);
      await page.mouse.move(800, 450);
      await page.mouse.move(820, 470);
      await page.keyboard.press("ArrowDown");
      await sleep(500);
      const ui2 = await touchUi();
      check(!ui2.shown, `and stay hidden after the mouse and keyboard are used (${JSON.stringify(ui2)})`);
    } else {
      measures.touchUiAtTitle = await touchUi();
      console.log(`[Mobile] on-screen controls at the title: ${JSON.stringify(measures.touchUiAtTitle)}`);
    }

    if (play && !desktop) await playByTouch({ page, context, log, errors, shot, waitLog, seen, memory, touchUi, measures });

    await memory("end");
    measures.peakRss = peakRss * 1024;
    measures.peakRssProcs = peakRssProcs;
    console.log(`[Mobile] peak resident memory of the page processes (sampled every 250 ms): ${MB(peakRss * 1024)} (${peakRssProcs})`);
    if (measures.soundErrors) console.log(`[Mobile] ${measures.soundErrors} "Loading FSB failed" errors not counted: no Web Audio in this WebKit, so no sound decodes`);
    check(errors.length === 0, `no page or console errors${errors.length ? ": " + errors.slice(0, 5).join(" | ") : ""}`);
    await context.close();
  } catch (e) {
    check(false, "the check ran: " + (e?.stack ?? e));
  } finally {
    clearInterval(sampler);
    writeFileSync(path.join(out, "console.log"), log.join("\n") + "\n");
    writeFileSync(path.join(out, "result.json"), JSON.stringify({ profile: profileName, play, results, measures }, null, 2) + "\n");
    if (browser) await browser.close().catch(() => {});
    server.close();
  }
  const ok = results.length > 0 && results.every((r) => r.ok);
  console.log(`[Mobile] ${ok ? "OK" : "FAILED"}: ${results.filter((r) => r.ok).length}/${results.length} checks; log ${path.join(out, "console.log")}`);
  process.exit(ok ? 0 : 1);
}

// --- touch: real touch events, one finger or several, through the browser's own input pipeline
function touchDriver(page) {
  const impl = page._connection?.toImpl?.(page);
  const delegate = impl && (impl.delegate || impl._delegate);
  let nextId = 1;
  if (delegate && delegate._pageProxySession) {
    // WebKit: Input.dispatchTouchEvent, each touch point by its own id
    const s = delegate._pageProxySession;
    return {
      down: async (x, y) => { const id = nextId++; await s.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x: Math.round(x), y: Math.round(y), id }] }); return { id, x, y }; },
      up: async (t) => { await s.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [{ x: Math.round(t.x), y: Math.round(t.y), id: t.id }] }); },
    };
  }
  // Chromium: CDP Input.dispatchTouchEvent; a touchStart lists every finger down, a touchEnd the one lifted
  let cdp;
  const fingers = new Map();
  const point = (f) => ({ x: f.x, y: f.y, id: f.id, radiusX: 8, radiusY: 8, force: 1 });
  const send = async (type, list) => {
    cdp ||= await page.context().newCDPSession(page);
    await cdp.send("Input.dispatchTouchEvent", { type, touchPoints: list.map(point) });
  };
  return {
    down: async (x, y) => { const f = { id: nextId++, x, y }; fingers.set(f.id, f); await send("touchStart", [...fingers.values()]); return f; },
    up: async (f) => { fingers.delete(f.id); await send("touchEnd", [f]); },
  };
}

async function playByTouch({ page, log, errors, shot, waitLog, seen, memory, touchUi, measures }) {
  const touch = touchDriver(page);
  const tap = async (x, y, hold = 60) => { const f = await touch.down(x, y); await sleep(hold); await touch.up(f); };
  const center = async (sel) => page.evaluate((s) => {
    const e = document.querySelector(s);
    if (!e) return null;
    const r = e.getBoundingClientRect();
    return r.width > 0 ? { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height } : null;
  }, sel);
  const press = async (sel, hold = 80) => { const c = await center(sel); if (!c) throw new Error("no button " + sel); await tap(c.x, c.y, hold); return c; };
  const holdOn = async (sel) => { const c = await center(sel); if (!c) throw new Error("no button " + sel); return touch.down(c.x, c.y); };
  const listen = async (ms) => {
    let a = await page.evaluate(() => window.__audio());
    for (const end = Date.now() + ms; Date.now() < end; await sleep(100)) { const b = await page.evaluate(() => window.__audio()); a = { ...b, peak: Math.max(a.peak, b.peak) }; }
    return a;
  };
  // where the game says its menu rows are ([Web] targets lines: fractions of the page from the top left)
  const target = (label) => {
    const lines = log.filter((l) => l.includes("[Web] targets "));
    const last = lines[lines.length - 1] || "";
    const m = new RegExp("(?:^|\\| )" + label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + " @ ([\\d.]+),([\\d.]+)").exec(last.slice(last.indexOf(":") + 1).trim());
    return m ? { fx: Number(m[1]), fy: Number(m[2]) } : null;
  };
  const tapTarget = async (label) => {
    const t = target(label);
    if (!t) throw new Error(`no ${label} in the game's targets (${log.filter((l) => l.includes("[Web] targets ")).slice(-1)[0] ?? "none"})`);
    const vp = page.viewportSize();
    await tap(t.fx * vp.width, t.fy * vp.height);
  };

  // sound waits for the first tap, then the title music plays
  const before = await listen(1500);
  const vp = page.viewportSize();
  await tap(vp.width * 0.85, vp.height * 0.15); // an empty part of the title
  const after = await listen(3000);
  console.log(`[Mobile] audio before a tap: ${JSON.stringify(before)}; after one: ${JSON.stringify(after)}`);
  if (measures.webAudio) check(before.peak === 0 && after.peak > 0.01, `sound starts with the first tap (peak ${before.peak.toFixed(4)} -> ${after.peak.toFixed(4)})`);
  else console.log("[Mobile] sound not checked here (no Web Audio in this WebKit)");
  const ui = await touchUi();
  check(ui.shown, `the on-screen controls show after a touch (${JSON.stringify(ui)})`);
  const sizes = await page.evaluate(() => [...document.querySelectorAll("#touch [data-key]")].filter((b) => b.getBoundingClientRect().width > 0)
    .map((b) => { const r = b.getBoundingClientRect(); return { key: b.dataset.key, w: Math.round(r.width), h: Math.round(r.height) }; }));
  const small = sizes.filter((s) => s.w < 44 || s.h < 44);
  console.log(`[Mobile] buttons on the title: ${sizes.map((s) => `${s.key} ${s.w}x${s.h}`).join(", ")}`);
  check(small.length === 0, `every on-screen button is at least 44 pt${small.length ? ": " + JSON.stringify(small) : ""}`);

  // a menu by tap: Settings opens, a slider steps by a tap on its right, and the Back button closes it
  let from = log.length;
  await waitLog("[Web] targets Title", 20000);
  await tapTarget("Settings");
  const settingsOpen = await waitLog("[Web] targets Settings", 20000, from);
  await sleep(800);
  await shot("02_settings");
  check(settingsOpen, "a tap on Settings opens it");
  from = log.length;
  await press("#touch [data-key=back]");
  check(await waitLog("[Web] targets Title", 20000, from), "the on-screen Back closes Settings");

  // level 1-1 from the title by tap
  from = log.length;
  await sleep(600);
  const first = target("Continue") ? "Continue" : "Begin";
  await tapTarget(first);
  const ready = await waitLog("[Web] level ", 30000, from);
  check(ready, `a tap on ${first} starts a level (${ready ?? "no [Web] level ready"})`);
  if (!ready) return;
  await sleep(1500);
  await shot("03_level");
  const playUi = await page.evaluate(() => [...document.querySelectorAll("#touch [data-key]")].filter((b) => b.getBoundingClientRect().width > 0).map((b) => b.dataset.key));
  console.log(`[Mobile] buttons in a level: ${playUi.join(" ")}`);
  await memory("level");
  measures.fpsLevel = await page.evaluate(() => window.__fps(3000));
  console.log(`[Mobile] frame rate in the level: ${measures.fpsLevel.toFixed(1)} fps`);

  // the level as the game sees it (GameRoot.WebReport)
  const where = async () => {
    const before = log.length;
    await page.evaluate(() => window.unityInstance.SendMessage("Game", "WebReport", ""));
    const l = await waitLog("[Web] state ", 5000, before);
    return l ? JSON.parse(l.slice(l.indexOf("{"))) : null;
  };
  const dist = (a, b) => a && b ? Math.abs(a.x - b.x) + Math.abs(a.y - b.y) : -1;
  const settle = async () => { for (let i = 0; i < 40; i++) { const s = await where(); if (s && s.state === "Playing") return s; await sleep(250); } return where(); };

  // a tap on the d-pad steps one tile (the first way that's open); holding it keeps walking
  let s0 = await settle(), s1 = s0, stepKey = null;
  for (const k of ["right", "left", "up", "down"]) {
    await press(`#touch [data-key=${k}]`, 60);
    await sleep(900);
    s1 = await where();
    if (dist(s0, s1) === 1) { stepKey = k; break; }
    s0 = await settle();
  }
  check(stepKey, `a tap on the d-pad steps one tile (${stepKey ?? "no direction moved"}: ${JSON.stringify(s0?.p)} -> ${JSON.stringify(s1?.p)})`);
  let walked = null, s2 = s1;
  for (const k of ["left", "right", "up", "down"]) {
    const from = await settle();
    const held = await holdOn(`#touch [data-key=${k}]`);
    await sleep(1000);
    await touch.up(held);
    await sleep(500);
    s2 = await where();
    if (dist(from, s2) >= 2) { walked = `${k}: ${JSON.stringify(from.p)} -> ${JSON.stringify(s2.p)}`; break; }
  }
  check(walked, `holding the d-pad keeps walking (${walked ?? "no direction walked two tiles"})`);
  await shot("04_walked");

  // a tap on an obstacle aims at it (one the game isn't aiming at already, if there are two); Borrow freezes it
  s2 = await settle();
  const pick = s2 && s2.obstacles.length > 1 && s2.aim === 0 ? 1 : 0;
  const obstacle = s2?.obstacles?.[pick];
  if (obstacle) {
    await tap(obstacle.fx * vp.width, obstacle.fy * vp.height);
    await sleep(400);
    const s3 = await where();
    check(s3 && s3.aim === pick, `a tap on an obstacle aims at it (obstacle ${pick} at ${obstacle.fx},${obstacle.fy}; aim ${s2?.aim} -> ${s3?.aim})`);
    await shot("05_aimed");
    await press("#touch [data-key=borrow]");
    await sleep(300);
    const s4 = await where();
    check(s4 && s4.frozen && s4.frozen.length > 0, `Borrow freezes it (frozen ${JSON.stringify(s4?.frozen)})`);
    await shot("06_borrowed");
  } else check(false, "the level reports an obstacle to aim at");

  // Focus held with one thumb while the other steps (multi-touch); then Rewind held
  const focus = await holdOn("#touch [data-key=focus]");
  await sleep(250);
  await press("#touch [data-key=up]", 60);
  await sleep(250);
  const s5 = await where();
  await shot("07_focus");
  await touch.up(focus);
  check(s5 && s5.focus, `Focus holds while the other thumb uses the d-pad (focus ${s5?.focus})`);
  await sleep(1500);
  const tBefore = (await settle())?.tick;
  // the board scrubs back while Rewind is held; the clock lands where it was let go
  const rewind = await holdOn("#touch [data-key=rewind]");
  await sleep(1200);
  await shot("08_rewind");
  await touch.up(rewind);
  await sleep(500);
  const s6 = await where();
  check(s6 && s6.tick < tBefore, `holding Rewind turns the clock back (tick ${tBefore} -> ${s6?.tick})`);
  await sleep(600);

  // the hint toggle, then Restart held, then Pause and Resume by tap
  from = log.length;
  await press("#touch [data-key=hint]");
  check(await waitLog("[Web] tip ", 3000, from), "the hint button folds or unfolds the tip");
  await sleep(400);
  await shot("09_hint");
  from = log.length;
  const restart = await holdOn("#touch [data-key=restart]");
  await sleep(1100);
  await touch.up(restart);
  check(await waitLog("[Web] level ", 5000, from), "holding Restart restarts the level");
  await sleep(1200);
  from = log.length;
  await press("#touch [data-key=pause]");
  const paused = await waitLog("[Web] targets Pause", 20000, from);
  await sleep(800);
  await shot("10_pause");
  check(paused, "the pause button opens the pause menu");
  from = log.length;
  from = log.length;
  await tapTarget("Resume");
  check(await waitLog("[Web] controls: Level", 10000, from), "a tap on Resume goes back to the level");

  // portrait asks to turn the phone
  await page.setViewportSize({ width: vp.height, height: vp.width });
  await sleep(1200);
  const rotate = await page.evaluate(() => { const r = document.querySelector("#rotate"); return !!r && getComputedStyle(r).display !== "none"; });
  await shot("11_portrait");
  check(rotate, "in portrait the page asks to turn the device");
  await page.setViewportSize(vp);
  await sleep(1200);
  await shot("12_back_to_landscape");

  // a key press hides the controls (a keyboard attached), a touch brings them back
  await page.keyboard.press("ArrowLeft");
  await sleep(300);
  const afterKey = await touchUi();
  check(!afterKey.shown, "a key press hides the on-screen controls");
  await tap(vp.width * 0.5, vp.height * 0.1);
  await sleep(300);
  check((await touchUi()).shown, "a touch brings them back");
}

main();
