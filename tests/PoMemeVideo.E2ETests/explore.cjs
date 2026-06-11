/* PORUN agentic visual exploration — screenshots, console capture, responsive checks,
 * guest-login persistence, SignalR negotiate. Standalone; run with: node explore.cjs */
const { chromium } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const BASE = process.env.PORUN_BASE || 'http://127.0.0.1:7000';
const OUT = path.join(__dirname, 'artifacts', process.env.PORUN_OUT || 'porun');
fs.mkdirSync(OUT, { recursive: true });

const consoleLog = [];
const networkErrors = [];

(async () => {
  const browser = await chromium.launch({
    headless: true,
    executablePath: 'C:\\Users\\punko\\AppData\\Local\\ms-playwright\\chromium-1223\\chrome-win64\\chrome.exe',
  });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();

  page.on('console', m => {
    if (m.type() === 'error' || m.type() === 'warning')
      consoleLog.push(`[${m.type()}] ${m.text().slice(0, 300)}`);
  });
  page.on('response', r => {
    if (r.status() >= 400) networkErrors.push(`${r.status()} ${r.request().method()} ${r.url()}`);
  });

  const shot = async (name) => page.screenshot({ path: path.join(OUT, name + '.png'), fullPage: false });

  // 1. Home (Blazor WASM boot)
  await page.goto(BASE + '/', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1500);
  await shot('01-home-desktop');

  // 2. Login page + GUEST flow
  await page.goto(BASE + '/login', { waitUntil: 'networkidle' });
  await page.waitForTimeout(800);
  await shot('02-login');
  const guestBtn = page.locator('.anon-btn');
  let guestResult = 'guest button not found';
  if (await guestBtn.count() > 0) {
    await guestBtn.first().click();
    await page.waitForTimeout(2500);
    const stored = await page.evaluate(() => localStorage.getItem('pmv.guestDisplayName'));
    guestResult = `localStorage pmv.guestDisplayName = ${stored}`;
    await shot('03-after-guest-login');
    // reload to test persistence
    await page.reload({ waitUntil: 'networkidle' });
    await page.waitForTimeout(1200);
    const after = await page.evaluate(() => localStorage.getItem('pmv.guestDisplayName'));
    guestResult += ` | after reload = ${after}`;
    await shot('04-home-after-reload');
  }

  // 3. Key pages
  for (const [route, name] of [['/source', '05-source'], ['/engine/00000000-0000-0000-0000-000000000001', '06-engine-unknown-session'], ['/library', '07-library'], ['/results', '08-results'], ['/admin/sounds', '09-admin-sounds'], ['/bogus-route', '12-notfound']]) {
    try {
      await page.goto(BASE + route, { waitUntil: 'networkidle', timeout: 15000 });
      await page.waitForTimeout(800);
      await shot(name);
    } catch (e) { consoleLog.push(`[nav-fail] ${route}: ${e.message.slice(0, 120)}`); }
  }

  // 4. Responsive: mobile + tablet on home
  for (const [w, h, name] of [[390, 844, '10-home-mobile'], [820, 1180, '11-home-tablet']]) {
    await page.setViewportSize({ width: w, height: h });
    await page.goto(BASE + '/', { waitUntil: 'networkidle' });
    await page.waitForTimeout(800);
    await shot(name);
  }

  // 5. SignalR negotiate (Zero-Waste server-validated pattern check)
  const negotiate = await page.request.post(BASE + '/hubs/engine/negotiate?negotiateVersion=1');
  const negBody = negotiate.ok() ? await negotiate.json() : { status: negotiate.status() };

  await browser.close();

  fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify({
    guestResult,
    signalrNegotiate: { status: negotiate.status(), transports: (negBody.availableTransports || []).map(t => t.transport) },
    consoleIssues: consoleLog,
    httpErrors: networkErrors,
  }, null, 2));
  console.log('DONE — report.json written');
})().catch(e => { console.error('EXPLORE FAILED:', e.message); process.exit(1); });
