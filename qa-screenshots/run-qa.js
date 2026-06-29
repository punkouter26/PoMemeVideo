// PoMemeVideo QA audit — Playwright scripted traversal
// Captures: console errors, network 4xx/5xx, responsive screenshots, all client routes,
// API endpoint health, dev-auth /auth/guest handshake, and core feature clicks.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');

const BASE = 'http://localhost:7000';
const OUT = path.resolve('qa-screenshots');
fs.mkdirSync(OUT, { recursive: true });

const findings = [];
const consoleLog = [];
const networkLog = [];
const pageErrors = [];

function note(severity, area, msg) {
  const entry = { ts: new Date().toISOString(), severity, area, msg };
  findings.push(entry);
  console.log(`[${severity}] ${area}: ${msg}`);
}

const ROUTES = [
  { path: '/', name: '01-create' },
  { path: '/login', name: '02-login' },
  { path: '/source', name: '03-source' },
  { path: '/engine', name: '04-engine' },
  { path: '/results', name: '05-results' },
  { path: '/reveal', name: '06-reveal' },
  { path: '/admin/sounds', name: '07-admin-sounds' },
  { path: '/meme-library', name: '08-meme-library' },
  { path: '/this-route-does-not-exist', name: '09-404' },
];

const VIEWPORTS = [
  { name: 'desktop-1440', width: 1440, height: 900 },
  { name: 'tablet-768', width: 768, height: 1024 },
  { name: 'mobile-375', width: 375, height: 812 },
];

const API_PROBES = [
  '/health',
  '/api/auth/me',
  '/api/config',
  '/scalar/',
  '/openapi/v1.json',
];

async function capturePage(page, name) {
  // Wait for Blazor to finish loading (best-effort)
  await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});
  await page.waitForTimeout(800);
  const file = path.join(OUT, `${name}.png`);
  await page.screenshot({ path: file, fullPage: true });
  return file;
}

async function probeApis(page) {
  for (const p of API_PROBES) {
    try {
      const r = await page.request.get(BASE + p);
      note(r.ok() ? 'info' : 'warn', 'api',
        `${p} -> ${r.status()} ${r.ok() ? 'OK' : 'FAIL'} ct=${r.headers()['content-type'] ?? 'n/a'}`);
    } catch (e) {
      note('error', 'api', `${p} threw: ${e.message}`);
    }
  }
}

async function probeAuthHandshake(page) {
  // 1. Pre-auth /api/auth/me
  const pre = await page.request.get(BASE + '/api/auth/me');
  note('info', 'auth', `pre /api/auth/me = ${pre.status()} ${await pre.text()}`);

  // 2. POST /auth/guest
  const guest = await page.request.post(BASE + '/auth/guest');
  const body = await guest.text();
  note('info', 'auth', `POST /auth/guest = ${guest.status()} body=${body}`);

  // 3. Post-auth /api/auth/me with cookie
  const post = await page.request.get(BASE + '/api/auth/me');
  note('info', 'auth', `post /api/auth/me = ${post.status()} ${await post.text()}`);

  // 4. Verify auth cookie is HttpOnly, has session/pmv-session-id
  const cookies = await page.context().cookies();
  for (const c of cookies) {
    note('info', 'cookie',
      `${c.name} httpOnly=${c.httpOnly} secure=${c.secure} sameSite=${c.sameSite} path=${c.path}`);
  }
}

async function traverseRoutes(browser, viewport) {
  const ctx = await browser.newContext({ viewport });
  const page = await ctx.newPage();
  page.on('console', m => consoleLog.push({ route: page.url(), type: m.type(), text: m.text() }));
  page.on('pageerror', e => pageErrors.push({ route: page.url(), msg: e.message }));
  page.on('response', r => {
    if (r.status() >= 400) {
      networkLog.push({ url: r.url(), status: r.status(), method: r.request().method() });
    }
  });

  // Visit every route
  for (const r of ROUTES) {
    try {
      const resp = await page.goto(BASE + r.path, { waitUntil: 'domcontentloaded', timeout: 12000 });
      note('info', 'route',
        `${viewport.name} ${r.path} -> ${resp ? resp.status() : 'no-resp'} title="${await page.title()}"`);
      await capturePage(page, `${viewport.name}-${r.name}`);
    } catch (e) {
      note('error', 'route', `${viewport.name} ${r.path} -> ${e.message}`);
    }
  }

  // Visit / for the rest of the inspection (already authed via EnsureDev middleware)
  await page.goto(BASE + '/', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1500);
  await capturePage(page, `${viewport.name}-00-home-final`);

  // Get the navigation/header HTML
  const headerText = await page.locator('header, nav').first().innerText().catch(() => '(no header)');
  note('info', 'layout', `${viewport.name} header text: ${headerText.replace(/\n/g, ' | ').slice(0, 200)}`);

  // Look for the broken "AI Mode" badge
  const aiMode = await page.getByText(/AI Mode/i).first().locator('..').innerText().catch(() => '(no AI Mode block)');
  note('info', 'layout', `${viewport.name} AI-Mode block: ${aiMode.replace(/\n/g, ' | ').slice(0, 200)}`);

  // Click a couple of core CTAs
  const browseBtn = page.getByText(/BROWSE FILE/i).first();
  if (await browseBtn.isVisible().catch(() => false)) {
    await browseBtn.click().catch(e => note('warn', 'cta', `browse click: ${e.message}`));
    await page.waitForTimeout(500);
    await capturePage(page, `${viewport.name}-10-browse-clicked`);
  }

  const soundAdmin = page.getByRole('button', { name: /Sound Admin/i }).first();
  if (await soundAdmin.isVisible().catch(() => false)) {
    await soundAdmin.click().catch(e => note('warn', 'cta', `sound admin click: ${e.message}`));
    await page.waitForTimeout(800);
    await capturePage(page, `${viewport.name}-11-sound-admin`);
  }

  // Resize to capture layout reflow
  if (viewport.name === 'desktop-1440') {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.waitForTimeout(500);
    await capturePage(page, `${viewport.name}-12-1920`);
  }

  await ctx.close();
}

async function multiSessionTest(browser) {
  // Spin up 2 independent sessions, verify they get distinct identity claims + cookies
  const contexts = await Promise.all([
    browser.newContext({ viewport: { width: 1280, height: 800 } }),
    browser.newContext({ viewport: { width: 1280, height: 800 } }),
  ]);

  for (let i = 0; i < contexts.length; i++) {
    const ctx = contexts[i];
    const page = await ctx.newPage();
    await page.goto(BASE + '/', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1200);
    const me = await page.request.get(BASE + '/api/auth/me').then(r => r.json());
    note('info', 'multisess', `session ${i + 1} /api/auth/me = ${JSON.stringify(me)}`);
    const sessionCookie = (await ctx.cookies()).find(c => c.name === 'pmv-session-id');
    note('info', 'multisess', `session ${i + 1} pmv-session-id = ${sessionCookie?.value?.slice(0, 8)}…`);
    await ctx.close();
  }
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const page = await ctx.newPage();
    page.on('console', m => consoleLog.push({ route: page.url(), type: m.type(), text: m.text() }));
    page.on('pageerror', e => pageErrors.push({ route: page.url(), msg: e.message }));
    page.on('response', r => {
      if (r.status() >= 400) {
        networkLog.push({ url: r.url(), status: r.status(), method: r.request().method() });
      }
    });

    await probeApis(page);
    await probeAuthHandshake(page);

    // Fetch /api/diag to see masked config / runtime flags
    try {
      const r = await page.request.get(BASE + '/diag');
      const body = await r.text();
      note(r.ok() ? 'info' : 'warn', 'diag', `/diag ${r.status()} len=${body.length}`);
    } catch (e) { note('error', 'diag', e.message); }

    // Probe /api/processing/ai-model and /api/processing/providers
    for (const p of ['/api/processing/ai-model', '/api/processing/providers', '/api/sessions']) {
      const r = await page.request.get(BASE + p);
      const ct = r.headers()['content-type'] ?? '';
      const txt = (await r.text()).slice(0, 80);
      note('info', 'api', `${p} -> ${r.status()} ct=${ct} first=${txt.replace(/\n/g, ' ')}`);
    }

    await ctx.close();

    for (const v of VIEWPORTS) {
      await traverseRoutes(browser, v);
    }

    await multiSessionTest(browser);
  } finally {
    await browser.close();
  }

  // ── Telemetry correlation check: see if logs got enriched ─────────────
  let logTail = '';
  try {
    logTail = fs.readFileSync('../qa-api.out.log', 'utf8').split('\n').slice(-60).join('\n');
  } catch (e) { logTail = '(no log file)'; }
  const userIds = [...new Set((logTail.match(/UserId[":= ]+([A-Za-z0-9-]+)/g) || []))];
  const sessionIds = [...new Set((logTail.match(/SessionId[":= ]+([A-Za-z0-9-]+)/g) || []))];
  const correlationIds = [...new Set((logTail.match(/CorrelationId[":= ]+([A-Za-z0-9-]+)/g) || []))];
  const hasUserId = userIds.length > 0;
  const hasSessionId = sessionIds.length > 0;
  const hasCorrelationId = correlationIds.length > 0;
  note('info', 'telemetry',
    `log enrichment check: UserId matches=${userIds.length}, SessionId=${sessionIds.length}, CorrelationId=${correlationIds.length}`);

  // Console / page error summary
  const errs = consoleLog.filter(c => c.type === 'error');
  const warns = consoleLog.filter(c => c.type === 'warning');
  note('info', 'console', `total messages: ${consoleLog.length}, errors: ${errs.length}, warnings: ${warns.length}`);
  for (const e of errs) note('error', 'console', `${e.route} ${e.text.slice(0, 240)}`);
  for (const e of pageErrors) note('error', 'pageerror', `${e.route} ${e.msg}`);
  for (const n of networkLog) note('warn', 'network', `${n.method} ${n.url} -> ${n.status}`);

  // Write the report
  fs.writeFileSync(path.join(OUT, 'report.json'),
    JSON.stringify({
      findings,
      consoleLog,
      networkLog,
      pageErrors,
      telemetry: { hasUserId, hasSessionId, hasCorrelationId, userIds, sessionIds, correlationIds },
    }, null, 2));
  console.log('---REPORT-DONE---');
  console.log(`Findings: ${findings.length}; console errors: ${errs.length}; network 4xx/5xx: ${networkLog.length}; page errors: ${pageErrors.length}`);
})().catch(e => { console.error('FATAL', e); process.exit(1); });
