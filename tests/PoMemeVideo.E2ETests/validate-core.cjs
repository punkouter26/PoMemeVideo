/* Ad-hoc core-feature validation driven by Claude Code. Safe to delete. */
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE = 'http://localhost:7000';
const SHOTS = path.join(__dirname, 'validation-shots');
fs.mkdirSync(SHOTS, { recursive: true });

const results = [];
const consoleErrors = [];
const failedRequests = [];

function record(name, ok, detail) {
  results.push({ name, ok, detail: detail || '' });
  console.log(`${ok ? 'PASS' : 'FAIL'} | ${name}${detail ? ' | ' + detail : ''}`);
}

(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  page.on('console', m => {
    if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 300));
  });
  page.on('requestfailed', r => {
    failedRequests.push(`${r.method()} ${r.url()} :: ${r.failure()?.errorText}`);
  });
  page.on('response', r => {
    if (r.status() >= 500) failedRequests.push(`${r.request().method()} ${r.url()} :: HTTP ${r.status()}`);
  });

  // ---- 1. API smoke checks via request context ----
  const api = ctx.request;
  try {
    const h = await api.get(`${BASE}/health`);
    const hj = await h.json();
    record('API /health', h.ok() && hj.status === 'Healthy', `status=${hj.status} checks=${JSON.stringify(hj.checks)}`);
  } catch (e) { record('API /health', false, e.message); }

  try {
    const c = await api.get(`${BASE}/api/config`);
    const cj = await c.json();
    record('API /api/config', c.ok(), JSON.stringify(cj).slice(0, 200));
  } catch (e) { record('API /api/config', false, e.message); }

  try {
    const s = await api.get(`${BASE}/api/memelibrary/sounds?limit=5`);
    const sj = await s.json();
    const count = Array.isArray(sj) ? sj.length : (sj.items?.length ?? 'n/a');
    record('API /api/memelibrary/sounds', s.ok(), `returned ${count} sounds`);
  } catch (e) { record('API /api/memelibrary/sounds', false, e.message); }

  // ---- 2. Load root page (Blazor WASM boot) ----
  try {
    const resp = await page.goto(BASE + '/', { waitUntil: 'networkidle', timeout: 60000 });
    record('GET / responds', resp.ok(), `HTTP ${resp.status()}`);
    // Blazor WASM needs a moment to boot
    await page.waitForTimeout(4000);
    const body = (await page.textContent('body')) || '';
    record('Blazor app rendered content', body.trim().length > 50, `bodyText length=${body.trim().length}`);
    await page.screenshot({ path: path.join(SHOTS, '01-root.png'), fullPage: true });
    console.log('PAGE URL after load:', page.url());
    console.log('BODY SNIPPET:', body.replace(/\s+/g, ' ').slice(0, 400));
  } catch (e) { record('GET / responds', false, e.message); }

  // ---- 3. Auth: guest login if login page shown ----
  try {
    const onLogin = page.url().includes('/login');
    if (onLogin) {
      const guestBtn = page.locator('button, a').filter({ hasText: /guest/i }).first();
      if (await guestBtn.count() > 0) {
        await guestBtn.click();
        await page.waitForTimeout(4000);
        record('Guest login via UI', !page.url().includes('/login'), `now at ${page.url()}`);
      } else {
        // fall back to API guest auth
        const g = await api.post(`${BASE}/auth/guest`);
        record('Guest login via API', g.ok(), `HTTP ${g.status()}`);
        await page.goto(BASE + '/', { waitUntil: 'networkidle' });
        await page.waitForTimeout(3000);
      }
      await page.screenshot({ path: path.join(SHOTS, '02-after-auth.png'), fullPage: true });
    } else {
      // ensure we have an auth session for API checks anyway
      const g = await api.post(`${BASE}/auth/guest`);
      record('Guest session (API)', g.ok(), `HTTP ${g.status()}`);
    }
    const me = await api.get(`${BASE}/api/auth/me`);
    record('API /api/auth/me after auth', me.ok(), (await me.text()).slice(0, 200));
  } catch (e) { record('Auth flow', false, e.message); }

  // ---- 4. Source page elements ----
  try {
    await page.goto(BASE + '/', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);
    const body = (await page.textContent('body')) || '';
    const hasDrop = /DROP VIDEO FILE/i.test(body) || /BROWSE FILE/i.test(body);
    record('Source page: drop zone / browse control', hasDrop);
    const nav = await page.locator('a, button').allTextContents();
    const navText = nav.join(' ');
    record('Nav: CREATE link', /CREATE/i.test(navText));
    record('Nav: SOUNDS link', /SOUNDS/i.test(navText));
    record('Nav: HISTORY link', /HISTORY/i.test(navText));
    await page.screenshot({ path: path.join(SHOTS, '03-source.png'), fullPage: true });
  } catch (e) { record('Source page', false, e.message); }

  // ---- 5. Library page ----
  try {
    await page.goto(BASE + '/library', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);
    const body = (await page.textContent('body')) || '';
    record('Library page renders', body.trim().length > 30, body.replace(/\s+/g, ' ').slice(0, 200));
    await page.screenshot({ path: path.join(SHOTS, '04-library.png'), fullPage: true });
  } catch (e) { record('Library page', false, e.message); }

  // ---- 6. Results page ----
  try {
    await page.goto(BASE + '/results', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);
    const body = (await page.textContent('body')) || '';
    record('Results page renders', body.trim().length > 30, body.replace(/\s+/g, ' ').slice(0, 200));
    await page.screenshot({ path: path.join(SHOTS, '05-results.png'), fullPage: true });
  } catch (e) { record('Results page', false, e.message); }

  // ---- 7. Ingestion: SAS token (authenticated, via browser fetch so cookies apply) ----
  try {
    const sas = await page.evaluate(async (base) => {
      const r = await fetch(base + '/api/ingestion/sas', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileName: 'claude-validation.mp4', fileSizeBytes: 1048576, contentType: 'video/mp4' })
      });
      return { status: r.status, text: (await r.text()).slice(0, 300) };
    }, BASE);
    record('Ingestion SAS token request', sas.status >= 200 && sas.status < 300, `HTTP ${sas.status} ${sas.text}`);
  } catch (e) { record('Ingestion SAS token request', false, e.message); }

  // ---- 8. SignalR negotiate ----
  try {
    const neg = await page.evaluate(async (base) => {
      const r = await fetch(base + '/hubs/engine/negotiate?negotiateVersion=1', { method: 'POST' });
      return { status: r.status, text: (await r.text()).slice(0, 150) };
    }, BASE);
    record('SignalR /hubs/engine negotiate', neg.status === 200, `HTTP ${neg.status}`);
  } catch (e) { record('SignalR negotiate', false, e.message); }

  // ---- Summary ----
  console.log('\n===== CONSOLE ERRORS (' + consoleErrors.length + ') =====');
  [...new Set(consoleErrors)].slice(0, 10).forEach(e => console.log(' -', e));
  console.log('===== FAILED REQUESTS (' + failedRequests.length + ') =====');
  [...new Set(failedRequests)].slice(0, 10).forEach(e => console.log(' -', e));
  const pass = results.filter(r => r.ok).length;
  console.log(`\n===== RESULT: ${pass}/${results.length} checks passed =====`);

  await browser.close();
  process.exit(0);
})().catch(e => { console.error('FATAL', e); process.exit(1); });
