// Probe tổng hợp: 3 hướng × 4 màn × 3 bề rộng + hub. Đo trên DOM, không cần nhìn ảnh.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const PORT = 9333;
const CHROME = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
// gốc repo = thư mục cha của scripts/prototype-audit
const REPO = resolve(fileURLToPath(new URL('.', import.meta.url)), '..', '..');
const BASE = pathToFileURL(join(REPO, 'documentation', 'prototypes')).href;
const VARIANTS = ['a-marquee', 'b-signal', 'c-schedule'];
const SCREENS = ['home', 'movie', 'seatmap', 'checkout'];
const WIDTHS = [375, 768, 1440];

const profile = mkdtempSync(join(tmpdir(), 'keel_probe_'));
const chrome = spawn(CHROME, [
  '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
  '--allow-file-access-from-files', `--remote-debugging-port=${PORT}`,
  `--user-data-dir=${profile}`, 'about:blank',
], { stdio: 'ignore' });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function pageWs() {
  for (let i = 0; i < 80; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${PORT}/json/list`);
      const list = await r.json();
      const pg = list.find((t) => t.type === 'page' && t.webSocketDebuggerUrl);
      if (pg) return pg.webSocketDebuggerUrl;
    } catch { /* chưa lên */ }
    await sleep(150);
  }
  throw new Error('không kết nối được Chrome');
}

function connect(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    let id = 0;
    const pending = new Map();
    const listeners = [];
    ws.onmessage = (ev) => {
      const msg = JSON.parse(ev.data);
      for (const fn of listeners) fn(ev);
      if (msg.id && pending.has(msg.id)) {
        const { resolve: rs, reject: rj } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.error ? rj(new Error(JSON.stringify(msg.error))) : rs(msg.result);
      }
    };
    ws.onerror = reject;
    ws.onopen = () => resolve({
      send(method, params = {}) {
        return new Promise((rs, rj) => {
          const mid = ++id;
          pending.set(mid, { resolve: rs, reject: rj });
          ws.send(JSON.stringify({ id: mid, method, params }));
        });
      },
      on(fn) { listeners.push(fn); },
      off(fn) { const i = listeners.indexOf(fn); if (i >= 0) listeners.splice(i, 1); },
      close: () => ws.close(),
    });
  });
}

const AUDIT = `(() => {
  const de = document.documentElement;
  // Tràn ngang thật: trang có cuộn ngang được không
  const pageOverflow = de.scrollWidth - de.clientWidth;
  // Thành phần tràn ra ngoài khung nhìn mà KHÔNG nằm trong vùng cuộn
  const over = [];
  for (const el of document.querySelectorAll('body *')) {
    const r = el.getBoundingClientRect();
    if (r.width === 0) continue;
    if (r.right > de.clientWidth + 1 || r.left < -1) {
      let scrollable = false;
      for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
        const ov = getComputedStyle(p).overflowX;
        if (ov === 'auto' || ov === 'scroll') { scrollable = true; break; }
      }
      if (!scrollable) over.push((el.tagName + '.' + (el.className || '')).slice(0, 50));
    }
  }
  const unlabeled = [...document.querySelectorAll('input,select,textarea')].filter((el) => {
    if (el.type === 'hidden') return false;
    if (el.getAttribute('aria-label') || el.getAttribute('aria-labelledby')) return false;
    if (el.id && document.querySelector('label[for="' + CSS.escape(el.id) + '"]')) return false;
    return !(el.closest('label'));
  }).length;
  // Vùng chạm: input bị ẩn thì vùng chạm là label của nó
  const small = [];
  for (const el of document.querySelectorAll('a,button,input,select,textarea,[role=button],[role=tab]')) {
    const cs = getComputedStyle(el);
    if (cs.visibility === 'hidden' || cs.display === 'none' || +cs.opacity === 0) continue;
    let r = el.getBoundingClientRect();
    if ((el.tagName === 'INPUT' || el.tagName === 'SELECT') && (r.width <= 2 || r.height <= 2)) {
      const lb = el.id && document.querySelector('label[for="' + CSS.escape(el.id) + '"]');
      const target = lb || el.closest('label');
      if (!target) continue;
      r = target.getBoundingClientRect();
    }
    // input nằm trong <label>: bấm label là bật/tắt control → vùng chạm là label
    if (el.tagName === 'INPUT' && el.closest('label')) {
      const lr = el.closest('label').getBoundingClientRect();
      if (lr.height > 0) r = lr;
    }
    if (r.width === 0 || r.height === 0) continue;
    // WCAG 2.5.8: link nội tuyến trong một câu văn xuôi được miễn yêu cầu kích thước mục tiêu
    if (el.tagName === 'A' && cs.display.startsWith('inline')) {
      const p = el.parentElement;
      const hasOtherText = p && [...p.childNodes].some((n) => n !== el && n.nodeType === 3 && n.textContent.trim());
      if (hasOtherText) continue;
    }
    if (r.height < 40 || r.width < 24) small.push(el.tagName + '.' + (el.className || '') + ' ' + Math.round(r.width) + 'x' + Math.round(r.height));
  }
  const broken = [...document.images].filter((i) => i.complete && i.naturalWidth === 0).length;
  const h1s = [...document.querySelectorAll('h1')].filter((el) => el.getBoundingClientRect().height > 0);
  const links = [...new Set([...document.querySelectorAll('a[href]')]
    .map((a) => a.getAttribute('href'))
    .filter((h) => h && !/^(https?:|mailto:|#)/.test(h)))];
  return {
    vw: de.clientWidth, pageOverflow, over: [...new Set(over)].slice(0, 5),
    unlabeled, small: [...new Set(small)].slice(0, 6), broken,
    h1: h1s.length, main: !!document.querySelector('main'),
    lang: document.documentElement.lang, links,
  };
})()`;

const targets = [];
for (const v of VARIANTS) for (const s of SCREENS) for (const w of WIDTHS) targets.push({ name: `${v}/${s}-${w}`, url: `${BASE}/${v}/${s}.html`, w });
targets.push({ name: 'hub-1280', url: `${BASE}/index.html`, w: 1280 });

const wsUrl = await pageWs();
const cdp = await connect(wsUrl);
await cdp.send('Page.enable');
await cdp.send('Runtime.enable');
await cdp.send('Log.enable');

const results = [];
for (const t of targets) {
  const errs = [];
  const onEvent = (ev) => {
    const m = JSON.parse(ev.data);
    if (m.method === 'Runtime.exceptionThrown') errs.push(m.params.exceptionDetails?.text || 'exception');
    if (m.method === 'Log.entryAdded' && m.params.entry.level === 'error') errs.push(m.params.entry.text);
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push('console.error');
  };
  cdp.on(onEvent);
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: t.w, height: 900, deviceScaleFactor: 1, mobile: t.w < 768 });
  await cdp.send('Page.navigate', { url: t.url });
  await sleep(1400);
  const r = await cdp.send('Runtime.evaluate', { expression: AUDIT, returnByValue: true });
  const a = r.result.value;
  results.push({ name: t.name, w: t.w, url: t.url, ...a, errs: [...new Set(errs)] });
  cdp.off(onEvent);
}

cdp.close();
chrome.kill();
await sleep(400);
try { rmSync(profile, { recursive: true, force: true, maxRetries: 3 }); } catch { /* chrome còn giữ file */ }

let bad = 0;
for (const r of results) {
  const issues = [];
  if (r.pageOverflow > 1) issues.push('PAGE-OVERFLOW:' + r.pageOverflow + 'px');
  if (r.over.length) issues.push('OVERFLOW:' + r.over.join('/'));
  if (r.unlabeled) issues.push('unlabeled=' + r.unlabeled);
  if (r.small.length) issues.push('small=' + r.small.join('/'));
  if (r.broken) issues.push('brokenImg=' + r.broken);
  if (!r.main) issues.push('NO-MAIN');
  if (r.errs.length) issues.push('ERR:' + r.errs.join('|'));
  if (!r.h1) issues.push('NO-H1');
  if (issues.length) bad++;
  r.issues = issues;
  console.log(`${issues.length ? 'FAIL' : ' ok '} ${r.name.padEnd(26)} h1=${r.h1} ${issues.join(' ')}`);
}

// Kiểm tra mọi link nội bộ có trỏ tới file tồn tại
const { existsSync } = await import('node:fs');
const brokenLinks = new Set();
for (const r of results) {
  const dir = dirname(fileURLToPath(r.url));
  for (const href of r.links || []) {
    const target = resolve(dir, decodeURIComponent(href.split('#')[0].split('?')[0]));
    if (!existsSync(target)) brokenLinks.add(`${r.name} → ${href}`);
  }
}
if (brokenLinks.size) {
  console.log('\nLINK VỠ:');
  for (const b of brokenLinks) console.log('  ' + b);
} else {
  console.log('\nlink nội bộ: 0 vỡ');
}

console.log(`\n${results.length - bad}/${results.length} pass`);
process.exit(0);
