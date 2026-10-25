// Đo thị giác bằng pixel thật (tự decode PNG) + kiểm tra dark-mode có repaint hay không.
// Không cần thư viện ngoài: PNG giải nén bằng zlib của Node.
// Mục đích: thay cho việc "xem bằng mắt" khi relay ảnh của phiên agent không hoạt động.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { inflateSync } from 'node:zlib';
import { fileURLToPath, pathToFileURL } from 'node:url';

const PORT = 9412;
const CHROME = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const REPO = resolve(fileURLToPath(new URL('.', import.meta.url)), '..', '..');
const BASE = pathToFileURL(join(REPO, 'documentation', 'prototypes')).href;

const VARIANTS = [
  { dir: 'a-marquee', bg: [0x0b, 0x0e, 0x13], accent: [[0xe3, 0xb2, 0x3c]], scheme: 'dark' },
  { dir: 'b-signal', bg: [0x0d, 0x0f, 0x14], accent: [[0x3b, 0x6b, 0xff], [0x2d, 0xd4, 0xa7]], scheme: 'dark' },
  { dir: 'c-schedule', bg: [0xf7, 0xf7, 0xf5], accent: [[0x1f, 0x3f, 0x8f]], scheme: 'light' },
];
const SCREENS = ['home', 'movie', 'seatmap', 'checkout'];

// ---------- decoder PNG ----------
function decodePng(buf) {
  let p = 8, w = 0, h = 0, depth = 0, ctype = 0, interlace = 0;
  const idat = [];
  while (p < buf.length) {
    const len = buf.readUInt32BE(p);
    const type = buf.toString('ascii', p + 4, p + 8);
    const data = buf.subarray(p + 8, p + 8 + len);
    if (type === 'IHDR') {
      w = data.readUInt32BE(0); h = data.readUInt32BE(4);
      depth = data[8]; ctype = data[9]; interlace = data[12];
    } else if (type === 'IDAT') idat.push(data);
    else if (type === 'IEND') break;
    p += 12 + len;
  }
  if (depth !== 8 || interlace !== 0) throw new Error(`PNG khong ho tro: depth=${depth} interlace=${interlace}`);
  const ch = ctype === 6 ? 4 : ctype === 2 ? 3 : (() => { throw new Error('ctype ' + ctype); })();
  const raw = inflateSync(Buffer.concat(idat));
  const stride = w * ch;
  const out = Buffer.alloc(h * stride);
  let rp = 0;
  for (let y = 0; y < h; y++) {
    const f = raw[rp++];
    const line = raw.subarray(rp, rp + stride); rp += stride;
    const cur = out.subarray(y * stride, (y + 1) * stride);
    const prev = y > 0 ? out.subarray((y - 1) * stride, y * stride) : null;
    for (let x = 0; x < stride; x++) {
      const a = x >= ch ? cur[x - ch] : 0;
      const b = prev ? prev[x] : 0;
      const c = prev && x >= ch ? prev[x - ch] : 0;
      let v = line[x];
      if (f === 1) v += a;
      else if (f === 2) v += b;
      else if (f === 3) v += (a + b) >> 1;
      else if (f === 4) {
        const pp = a + b - c, pa = Math.abs(pp - a), pb = Math.abs(pp - b), pc = Math.abs(pp - c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      cur[x] = v & 0xff;
    }
  }
  return { w, h, ch, data: out };
}

const near = (a, b, tol = 26) => Math.abs(a[0] - b[0]) <= tol && Math.abs(a[1] - b[1]) <= tol && Math.abs(a[2] - b[2]) <= tol;

// ---------- phan tich ----------
function analyze(png, variant) {
  const { w, h, ch, data } = png;
  const bands = 24;
  const bandH = Math.ceil(h / bands);
  const ink = new Array(bands).fill(0);
  const bandCount = new Array(bands).fill(0);
  let accent = 0, total = 0, bgPx = 0;
  for (let y = 0; y < h; y++) {
    const bi = Math.min(bands - 1, Math.floor(y / bandH));
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * ch;
      const c = [data[i], data[i + 1], data[i + 2]];
      total++;
      bandCount[bi]++;
      if (near(c, variant.bg, 30)) bgPx++;
      else ink[bi]++;
      for (const a of variant.accent) if (near(c, a, 30)) { accent++; break; }
    }
  }
  // vung tam thu trong: chi so "manh" cua tung dai pixel
  const strength = ink.map((v, i) => (bandCount[i] ? v / bandCount[i] : 0));
  let best = 0;
  for (let i = 1; i < bands; i++) if (strength[i] > strength[best]) best = i;
  const sorted = [...strength].sort((a, b) => b - a);
  const nonEmpty = sorted.filter((v) => v > 0.01);
  return {
    bgPct: +(100 * bgPx / total).toFixed(1),
    accentPct: +(100 * accent / total).toFixed(2),
    heroBand: `${Math.round((100 * best) / bands)}%-${Math.round((100 * (best + 1)) / bands)}%`,
    heroStrength: +(100 * strength[best]).toFixed(1),
    top2Gap: nonEmpty.length > 1 ? +(nonEmpty[0] / Math.max(nonEmpty[1], 0.01)).toFixed(1) : 0,
  };
}

// ---------- CDP ----------
async function cdpTargets() {
  const res = await fetch(`http://127.0.0.1:${PORT}/json/list`);
  return res.json();
}
let ws, msgId = 0;
const pending = new Map();
function send(method, params = {}, sessionId) {
  const id = ++msgId;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params, sessionId }));
  });
}
async function connect() {
  for (let i = 0; i < 80; i++) {
    try {
      const list = await cdpTargets();
      const page = list.find((t) => t.type === 'page' && t.webSocketDebuggerUrl);
      if (page) {
        ws = new WebSocket(page.webSocketDebuggerUrl);
        await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
        ws.onmessage = (ev) => {
          const m = JSON.parse(ev.data);
          if (m.id && pending.has(m.id)) {
            const { resolve, reject } = pending.get(m.id);
            pending.delete(m.id);
            m.error ? reject(new Error(JSON.stringify(m.error))) : resolve(m.result);
          }
        };
        return;
      }
    } catch {}
    await new Promise((r) => setTimeout(r, 250));
  }
  throw new Error('khong ket noi duoc CDP');
}

const profile = mkdtempSync(join(tmpdir(), 'keel_vis_'));
const chrome = spawn(CHROME, [
  `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`,
  '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
  '--window-size=1440,1400', 'about:blank',
], { stdio: 'ignore' });

const out = [];
try {
  await connect();
  await send('Page.enable');
  await send('Runtime.enable');
  const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });

  for (const v of VARIANTS) {
    for (const s of SCREENS) {
      const url = `${BASE}/${v.dir}/${s}.html`;
      await send('Page.navigate', { url }, sessionId);
      await new Promise((r) => setTimeout(r, 1400));

      const evalJs = async (expr) => {
        const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true }, sessionId);
        return r.result?.value;
      };
      const light = await evalJs(`(()=>{const cs=getComputedStyle(document.body);const hs=getComputedStyle(document.documentElement);return {bg:cs.backgroundColor,color:cs.color,scheme:hs.colorScheme}})()`);
      const shot1 = await send('Page.captureScreenshot', { format: 'png' }, sessionId);

      // phong do thuoc: chu co, nhieu, vung hero, va ti le chu tieu de/chu than
      const typo = await evalJs(`(()=>{
        const px = (el) => parseFloat(getComputedStyle(el).fontSize);
        const h1 = document.querySelector('h1');
        const body = px(document.body);
        const texts = [...document.querySelectorAll('h1,h2,h3,strong,b,p,span,button,a,label,td,th,li')]
          .filter(el => el.textContent.trim().length > 0 && el.offsetParent !== null);
        const maxSize = Math.max(...texts.map(px), 0);
        const biggest = texts.find(el => px(el) === maxSize);
        const head = h1 ? { text: h1.textContent.trim().slice(0,40), size: px(h1), ratio: +(px(h1)/body).toFixed(2) } : null;
        return {
          bodyPx: body,
          h1: head,
          maxTextPx: maxSize,
          maxTextRatio: +(maxSize/body).toFixed(2),
          biggestTag: biggest ? biggest.tagName + '.' + (biggest.className||'').toString().split(' ')[0] : null,
          h1Color: h1 ? getComputedStyle(h1).color : null,
          bodyColor: getComputedStyle(document.body).color,
        };
      })()`);

      // anh doi chieu: chup hai lan KHONG doi che do (de tach dong ho dem nguoc khoi dark mode)
      await new Promise((r) => setTimeout(r, 900));
      const shotCtrl = await send('Page.captureScreenshot', { format: 'png' }, sessionId);
      const pngCtl = decodePng(Buffer.from(shotCtrl.data, 'base64'));
      const png1 = decodePng(Buffer.from(shot1.data, 'base64'));
      let diffCtl = 0;
      for (let i = 0; i < Math.min(png1.data.length, pngCtl.data.length); i += 97) if (png1.data[i] !== pngCtl.data[i]) diffCtl++;

      await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'dark' }] }, sessionId);
      await new Promise((r) => setTimeout(r, 500));
      const dark = await evalJs(`(()=>{const cs=getComputedStyle(document.body);const hs=getComputedStyle(document.documentElement);return {bg:cs.backgroundColor,color:cs.color,scheme:hs.colorScheme}})()`);
      const shot2 = await send('Page.captureScreenshot', { format: 'png' }, sessionId);
      await send('Emulation.setEmulatedMedia', { features: [] }, sessionId);

      const png = decodePng(Buffer.from(shot1.data, 'base64'));
      const a = analyze(png, v);
      const png2 = decodePng(Buffer.from(shot2.data, 'base64'));
      let diff = 0;
      for (let i = 0; i < Math.min(png.data.length, png2.data.length); i += 97) if (png.data[i] !== png2.data[i]) diff++;

      out.push({
        page: `${v.dir}/${s}`,
        scheme: light.scheme,
        declaredSchemeOk: light.scheme === v.scheme,
        repaintUnderDark: light.bg !== dark.bg || light.color !== dark.color,
        pixelDiff: diff,
        pixelDiffNoModeChange: diffCtl,
        ...typo,
        ...a,
      });
    }
  }
} finally {
  try { ws?.close(); } catch {}
  chrome.kill();
  await new Promise((r) => setTimeout(r, 1200));
  try { rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 300 }); } catch {}
}

console.log(JSON.stringify(out, null, 1));
