// audit.mjs — quét hai danh sách kiểm tra mà skill yêu cầu:
//   1) anti-ai-tells.md  → các mục HIGH (mẫu hình bắt được bằng máy)
//   2) accessibility-checklist.md → phần tự động hóa được (ngôn ngữ, tiêu đề, label, tương phản, vùng chạm…)
// Chạy từ gốc repo: node scripts/prototype-audit/audit.mjs
// Ghi đè: --json để in kết quả dạng máy đọc.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..', '..');
const PROTOS = join(ROOT, 'documentation', 'prototypes');
const PORT = 9334;
const CHROME = [
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].find((p) => { try { return readFileSync(p).length >= 0; } catch { return false; } }) || 'chrome';

const VARIANTS = ['a-marquee', 'b-signal', 'c-schedule'];
const SCREENS = ['home', 'movie', 'seatmap', 'checkout'];

/* ============================ 1. QUÉT TĨNH (anti-tells) ============================ */

const tell = (id, level, file, detail) => ({ id, level, file, detail });

function scanStatic() {
  const findings = [];
  const cssOf = new Map();
  const files = [];
  for (const v of VARIANTS) {
    for (const f of readdirSync(join(PROTOS, v))) {
      if (/\.(css|html|js)$/.test(f)) files.push(join(PROTOS, v, f));
    }
  }
  const allCss = files.filter((f) => f.endsWith('.css')).map((f) => [f, readFileSync(f, 'utf8')]);

  for (const [file, css] of allCss) {
    const rel = relative(ROOT, file).replace(/\\/g, '/');
    // A1 — gradient tím/hồng hoặc xanh dương→cyan làm màu thương hiệu
    const grads = css.match(/linear-gradient\([^)]*\)/gi) || [];
    for (const g of grads) {
      if (/(#8b5cf6|#a855f7|#ec4899|#d946ef|violet|magenta|#06b6d4|#22d3ee|#3b82f6.*#06b6d4)/i.test(g))
        findings.push(tell('A1', 'HIGH', rel, `gradient tím/hồng-cyan: ${g.slice(0, 80)}`));
    }
    // A2 — chữ tô gradient
    if (/background-clip\s*:\s*text|-webkit-background-clip\s*:\s*text/i.test(css))
      findings.push(tell('A2', 'HIGH', rel, 'background-clip:text'));
    // A4 — nền tối kiểu #0f172a
    if (/#0f172a/i.test(css)) findings.push(tell('A4', 'HIGH', rel, 'nền tối #0f172a (mặc định Tailwind)'));
    // B3 — glassmorphism
    if (/backdrop-filter\s*:\s*blur|backdrop-blur/i.test(css))
      findings.push(tell('B3', 'HIGH', rel, 'backdrop-filter: blur'));
    // B1 — chỉ một bán kính cho mọi thứ
    const radii = new Set((css.match(/border-radius\s*:\s*([^;]+);/gi) || []).map((s) => s.split(':')[1].trim()));
    const px = [...radii].filter((r) => /^\d+px$/.test(r));
    if (px.length > 0 && px.length <= 2 && px.length < 3)
      findings.push(tell('B1', 'MEDIUM', rel, `chỉ ${px.length} giá trị border-radius: ${px.join(', ')}`));
    // B2 — viền + nền + đổ bóng + blur cùng trên một phần tử
    const blocks = css.split(/\n\s*\n/);
    for (const b of blocks) {
      const has = (re) => re.test(b);
      if (has(/border\s*:/i) && has(/box-shadow/i) && has(/backdrop-filter/i))
        findings.push(tell('B2', 'HIGH', rel, 'border + box-shadow + backdrop-filter trên cùng một khối'));
    }
    // C3 — không chỉnh tracking ở cỡ hiển thị
    if (!/letter-spacing/.test(css)) findings.push(tell('C3', 'MEDIUM', rel, 'không có letter-spacing nào'));
    // C6 — số chưa set tabular
    if (!/font-variant-numeric/.test(css) && /price|total|timer|giá/i.test(css))
      findings.push(tell('C6', 'MEDIUM', rel, 'có giá/đồng hồ nhưng không set tabular-nums'));
    // F1 — pulse/bounce/scale
    if (/animation[^;]*\b(pulse|bounce)\b|hover\s*:\s*\{[^}]*transform\s*:\s*scale/i.test(css))
      findings.push(tell('F1', 'HIGH', rel, 'animation pulse/bounce hoặc hover scale'));
    // F3 — reduced motion
    if (!/prefers-reduced-motion/.test(css))
      findings.push(tell('F3', 'HIGH', rel, 'thiếu prefers-reduced-motion'));
  }

  // C1 — một họ chữ trung tính duy nhất
  for (const [file, css] of allCss) {
    const rel = relative(ROOT, file).replace(/\\/g, '/');
    const fams = new Set((css.match(/font-family\s*:\s*([^;]+);/gi) || []).map((s) => s.split(':')[1].trim()));
    if (fams.size <= 1) findings.push(tell('C1', 'HIGH', rel, `chỉ ${fams.size} họ chữ`));
  }
  // E1 — nội dung mẫu tròn xoe
  for (const f of files.filter((x) => x.endsWith('.html'))) {
    const rel = relative(ROOT, f).replace(/\\/g, '/');
    const html = readFileSync(f, 'utf8');
    if (/lorem ipsum/i.test(html)) findings.push(tell('E1', 'HIGH', rel, 'lorem ipsum'));
    if (/\b\d{1,3},\d{3}\b/.test(html)) findings.push(tell('E1', 'HIGH', rel, 'số tròn xoe kiểu 1,234'));
  }
  // E2 — emoji làm biểu tượng giao diện (trong thẻ điều hướng/nhãn, không phải trong nội dung)
  for (const f of files.filter((x) => x.endsWith('.html'))) {
    const rel = relative(ROOT, f).replace(/\\/g, '/');
    const html = readFileSync(f, 'utf8');
    const navish = html.match(/<(nav|a|button|h1|h2)[^>]*>([^<]{0,40})/g) || [];
    for (const m of navish) {
      if (/[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}]/u.test(m))
        findings.push(tell('E2', 'HIGH', rel, `emoji trong thành phần điều hướng/nhãn: ${m.slice(0, 60)}`));
    }
  }
  return findings;
}

/* ============================ 2. QUÉT DOM (a11y) ============================ */

const DOM_AUDIT = `(() => {
  const de = document.documentElement;
  const parse = (c) => {
    const m = c && c.match(/[\\d.]+/g);
    if (!m) return null;
    const [r, g, b, a = 1] = m.map(Number);
    return { r, g, b, a };
  };
  const over = (fg, bg) => {
    const L = (c) => {
      const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
      return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
    };
    const a = fg.a, b = bg.a;
    const mix = { r: fg.r * a + bg.r * (1 - a), g: fg.g * a + bg.g * (1 - a), b: fg.b * a + bg.b * (1 - a), a: 1 };
    const l1 = L(mix), l2 = L(bg);
    return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
  };
  const effBg = (el) => {
    let node = el;
    while (node && node !== document.documentElement.parentNode) {
      const c = parse(getComputedStyle(node).backgroundColor);
      if (c && c.a > 0.05) return c;
      node = node.parentElement;
    }
    return { r: 255, g: 255, b: 255, a: 1 };
  };
  const out = { lowContrast: [], noLabel: [], smallTap: [], headings: [], imgNoAlt: [], btnNoName: [], focusVisible: 0, outlineNone: 0, inputFont: [], lang: de.lang, title: document.title, main: document.querySelectorAll('main').length, h1: document.querySelectorAll('h1').length, ariaLive: 0, liveRegions: 0, landmarks: { nav: 0, main: 0, header: 0, footer: 0 }, skipLink: 0, dupTitle: 0 };
  const seen = new Set();
  for (const el of document.querySelectorAll('*')) {
    const cs = getComputedStyle(el);
    if (el.matches('nav')) out.landmarks.nav++;
    if (el.matches('header')) out.landmarks.header++;
    if (el.matches('footer')) out.landmarks.footer++;
    if (el.matches('[aria-live]')) { out.ariaLive++; out.liveRegions++; }
  }
  for (const el of document.querySelectorAll('h1,h2,h3,h4,h5,h6')) {
    const r = el.getBoundingClientRect();
    if (r.height > 0) out.headings.push(+el.tagName[1]);
  }
  for (const img of document.querySelectorAll('img')) {
    if (img.getAttribute('alt') === null) out.imgNoAlt.push(img.getAttribute('src') || '?');
  }
  for (const b of document.querySelectorAll('button,[role=button]')) {
    const name = (b.textContent || '').trim() || b.getAttribute('aria-label') || b.getAttribute('title');
    if (!name) out.btnNoName.push(b.className || '?');
  }
  for (const inp of document.querySelectorAll('input:not([type=hidden]),select,textarea')) {
    const id = inp.id;
    const lab = (id && document.querySelector('label[for="' + CSS.escape(id) + '"]')) || inp.closest('label');
    const aria = inp.getAttribute('aria-label') || inp.getAttribute('aria-labelledby');
    if (!lab && !aria) out.noLabel.push(inp.name || inp.type || inp.className);
    const fs = parseFloat(cs.fontSize);
    if (fs < 16 && inp.type !== 'checkbox' && inp.type !== 'radio') out.inputFont.push(inp.name || inp.type || '? ' + fs + 'px');
    const r = inp.getBoundingClientRect();
    if (r.width > 0 && r.height > 0 && (r.width < 44 || r.height < 44) && !inp.closest('label')) {
      out.smallTap.push((inp.name || inp.type) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height));
    }
  }
  for (const a of document.querySelectorAll('a[href],button')) {
    const cs2 = getComputedStyle(a);
    const r = a.getBoundingClientRect();
    if (r.width === 0 || r.height === 0) continue;
    if (cs2.display.startsWith('inline') && a.parentElement && a.parentElement.textContent.trim() !== a.textContent.trim()) continue;
    if (r.height < 44 || r.width < 44) out.smallTap.push((a.className || a.tagName) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height));
  }
  // tương phản chữ
  for (const el of document.querySelectorAll('p,span,a,h1,h2,h3,h4,li,button,label,td,th,strong,small,div')) {
    if (!el.childNodes.length) continue;
    const hasText = [...el.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim().length > 1);
    if (!hasText) continue;
    const r = el.getBoundingClientRect();
    if (r.width === 0 || r.height === 0) continue;
    const fg = parse(cs.color); if (!fg) continue;
    const bg = effBg(el);
    const ratio = over(fg, bg);
    const size = parseFloat(cs.fontSize);
    const bold = parseInt(cs.fontWeight, 10) >= 700;
    const large = size >= 24 || (size >= 19 && bold);
    const need = large ? 3 : 4.5;
    if (ratio < need) {
      const key = el.className + '|' + Math.round(ratio * 10);
      if (!seen.has(key)) { seen.add(key); out.lowContrast.push({ cls: (el.className || el.tagName).toString().slice(0, 40), ratio: +ratio.toFixed(2), need, size }); }
    }
  }
  return out;
})()`;

async function connect(url) {
  return new Promise((res, rej) => {
    const ws = new WebSocket(url);
    let id = 0; const pending = new Map(); const listeners = [];
    ws.onmessage = (ev) => {
      const msg = JSON.parse(ev.data);
      for (const fn of listeners) fn(ev);
      if (msg.id && pending.has(msg.id)) {
        const { resolve: rs, reject: rj } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.error ? rj(new Error(JSON.stringify(msg.error))) : rs(msg.result);
      }
    };
    ws.onerror = rej;
    ws.onopen = () => res({
      send: (method, params = {}) => new Promise((rs, rj) => {
        const mid = ++id;
        pending.set(mid, { resolve: rs, reject: rj });
        ws.send(JSON.stringify({ id: mid, method, params }));
      }),
      on: (fn) => listeners.push(fn),
      off: (fn) => { const i = listeners.indexOf(fn); if (i >= 0) listeners.splice(i, 1); },
      close: () => ws.close(),
    });
  });
}

async function run() {
  const staticFindings = scanStatic();
  const profile = mkdtempSync(join(tmpdir(), 'keel_audit_'));
  const chrome = spawn(CHROME, [`--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, '--headless=new', '--disable-gpu', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  const results = [];
  try {
    let wsUrl = null;
    for (let i = 0; i < 60 && !wsUrl; i++) {
      await new Promise((r) => setTimeout(r, 250));
      try {
        const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
        const page = list.find((t) => t.type === 'page' && t.webSocketDebuggerUrl);
        if (page) wsUrl = page.webSocketDebuggerUrl;
      } catch { /* chưa sẵn sàng */ }
    }
    const cdp = await connect(wsUrl);
    for (const v of VARIANTS) {
      for (const s of [...SCREENS, 'index']) {
        const url = pathToFileURL(join(PROTOS, v, `${s}.html`)).href;
        await cdp.send('Runtime.evaluate', { expression: `location.href = ${JSON.stringify(url)}` });
        await new Promise((r) => setTimeout(r, 900));
        const r = await cdp.send('Runtime.evaluate', { expression: DOM_AUDIT, returnByValue: true });
        const a = r?.result?.value || {};
        const issues = [];
        if (a.lang !== 'vi') issues.push('lang=' + a.lang);
        if (!a.title) issues.push('no-title');
        if (a.main !== 1) issues.push('main=' + a.main);
        if (a.h1 !== 1) issues.push('h1=' + a.h1);
        // thang tiêu đề không bỏ bậc
        const hs = a.headings || [];
        for (let i = 1; i < hs.length; i++) if (hs[i] - hs[i - 1] > 1) { issues.push('heading-jump h' + hs[i - 1] + '→h' + hs[i]); break; }
        if ((a.noLabel || []).length) issues.push('no-label:' + a.noLabel.length);
        if ((a.smallTap || []).length) issues.push('small-tap:' + a.smallTap.slice(0, 3).join(','));
        if ((a.lowContrast || []).length) issues.push('contrast:' + a.lowContrast.slice(0, 3).map((c) => c.cls + '=' + c.ratio).join(','));
        if ((a.imgNoAlt || []).length) issues.push('img-no-alt:' + a.imgNoAlt.length);
        if ((a.btnNoName || []).length) issues.push('btn-no-name:' + a.btnNoName.length);
        if ((a.inputFont || []).length) issues.push('input-font<16:' + a.inputFont.slice(0, 3).join(','));
        if (!a.ariaLive) issues.push('no-aria-live');
        results.push({ page: `${v}/${s}`, issues, detail: a });
      }
    }
    cdp.close();
  } finally {
    chrome.kill();
    try { rmSync(profile, { recursive: true, force: true, maxRetries: 3 }); } catch { /* profile còn khoá */ }
  }
  return { staticFindings, results };
}

const { staticFindings, results } = await run();
const high = staticFindings.filter((f) => f.level === 'HIGH');
const domIssues = results.filter((r) => r.issues.length);

if (process.argv.includes('--json')) {
  console.log(JSON.stringify({ staticFindings, results }, null, 1));
} else {
  console.log('=== ANTI-TELLS (tĩnh) ===');
  if (!staticFindings.length) console.log('  không có phát hiện');
  for (const f of staticFindings) console.log(`  [${f.level}] ${f.id} ${f.file}: ${f.detail}`);
  console.log(`  → HIGH: ${high.length}, MEDIUM: ${staticFindings.length - high.length}`);
  console.log('\n=== A11Y + THỊ GIÁC (DOM) ===');
  if (!domIssues.length) console.log('  15/15 trang sạch');
  for (const r of domIssues) console.log(`  FAIL ${r.page}: ${r.issues.join(' | ')}`);
  console.log(`\n  ${results.length - domIssues.length}/${results.length} trang sạch`);
}
process.exit(high.length || domIssues.length ? 1 : 0);
