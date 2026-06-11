import { test, expect } from '@playwright/test';

test.describe('Headed Validation — Core Features', () => {
  test.describe.configure({ mode: 'serial' });

  const baseUrl = 'http://127.0.0.1:7000';
  const screenshots = 'artifacts/headed-validation';

  test('01 — Health & Config endpoints alive', async ({ request }) => {
    const health = await request.get(`${baseUrl}/health`);
    expect(health.status()).toBe(200);
    const healthBody = await health.json();
    expect(healthBody.status).toBe('Healthy');

    const config = await request.get(`${baseUrl}/api/config`);
    expect(config.status()).toBe(200);
    const configBody = await config.json();
    expect(configBody.isDevelopment).toBe(true);
    console.log(`[CONFIG] provider=${configBody.provider}, useMockAI=${configBody.useMockAI}`);

    const ai = await request.get(`${baseUrl}/api/config/ai-model`);
    expect(ai.status()).toBe(200);
    const aiBody = await ai.json();
    console.log(`[AI MODEL] provider=${aiBody.provider}, active=${aiBody.browserLLMModel || 'N/A'}`);
  });

  test('02 — Guest login flow', async ({ request }) => {
    const guest = await request.post(`${baseUrl}/auth/guest`);
    expect(guest.status()).toBe(200);
    const body = await guest.json();
    expect(body.identityType).toBe('GUEST');
    expect(body.displayName).toMatch(/^GUEST\d{8}$/);
    console.log(`[GUEST LOGIN] ${body.displayName} (${body.identityId})`);
  });

  test('03 — Source Page loads with Matrix Green aesthetic', async ({ page }) => {
    await page.goto(baseUrl, { waitUntil: 'networkidle', timeout: 30_000 });
    await page.screenshot({ path: `${screenshots}/03-source-page.png`, fullPage: true });

    const bodyText = await page.locator('body').innerText();
    expect(bodyText.length).toBeGreaterThan(0);

    // Verify ASCII drop zone exists
    const dropZone = page.locator('.ascii-drop-zone');
    await expect(dropZone).toBeVisible({ timeout: 10_000 });
    const dzText = await dropZone.innerText();
    expect(dzText).toContain('DROP VIDEO FILE HERE');

    // Verify browse button
    await expect(page.getByText('[ BROWSE FILE ]')).toBeVisible();

    // Verify AI mode selector is present
    const modeSection = page.getByText('AI Mode');
    await expect(modeSection.first()).toBeVisible();
  });

  test('04 — NavBar has all expected links and status indicators', async ({ page }) => {
    await page.goto(baseUrl, { waitUntil: 'networkidle', timeout: 30_000 });

    const nav = page.locator('nav[aria-label="Site navigation"]');
    await expect(nav).toBeVisible();

    // All nav links
    await expect(nav).toContainText('PoMemeVideo');
    await expect(nav).toContainText('CREATE');
    await expect(nav).toContainText('SOUNDS');
    await expect(nav).toContainText('HISTORY');

    // LOG OUT button visible for anonymous (auto-authenticated) users
    await expect(nav).toContainText('LOG OUT');

    // Auth status should show anonymous identity
    await expect(nav).toContainText('anon');

    // AI status indicator
    await expect(nav).toContainText('AI:');

    await page.screenshot({ path: `${screenshots}/04-navbar.png`, fullPage: false });
  });

  test('05 — Navigate to Sound Admin page', async ({ page }) => {
    await page.goto(`${baseUrl}/admin/sounds`, { waitUntil: 'networkidle', timeout: 30_000 });
    await page.screenshot({ path: `${screenshots}/05-sound-admin.png`, fullPage: true });

    const heading = page.getByRole('heading', { name: /Sound Admin/i });
    await expect(heading).toBeVisible();

    // Maintenance controls
    await expect(page.getByText('[ Maintenance zone ]')).toBeVisible();
    await expect(page.getByText('[ CLEAR ALL DATA ]')).toBeVisible();

    // Filter controls
    const filterInput = page.getByPlaceholder(/Filter by tag/i);
    await expect(filterInput).toBeVisible();
  });

  test('06 — Navigate to History/Results page', async ({ page }) => {
    await page.goto(`${baseUrl}/results`, { waitUntil: 'networkidle', timeout: 30_000 });
    await page.screenshot({ path: `${screenshots}/06-history.png`, fullPage: true });

    const heading = page.getByRole('heading', { name: /Video History/i });
    await expect(heading).toBeVisible();

    // Should show new video button
    await expect(page.getByText('[ + New Video ]')).toBeVisible();
  });

  test('07 — Navigate to Login page', async ({ page }) => {
    await page.goto(`${baseUrl}/login`, { waitUntil: 'networkidle', timeout: 30_000 });
    await page.screenshot({ path: `${screenshots}/07-login.png`, fullPage: true });

    // ASCII banner
    await expect(page.getByText('IDENTITY').first()).toBeVisible();
    await expect(page.getByText('AUTHENTICATE TO CONTINUE')).toBeVisible();

    // Guest button in dev mode
    await expect(page.getByText('[ RANDOM GUEST ]')).toBeVisible();

    // Microsoft sign in link
    await expect(page.getByText('[ SIGN IN WITH MICROSOFT ]')).toBeVisible();
  });

  test('08 — Scalar API docs load', async ({ page }) => {
    await page.goto(`${baseUrl}/scalar/`, { waitUntil: 'networkidle', timeout: 30_000 });
    await page.screenshot({ path: `${screenshots}/08-scalar-api.png`, fullPage: true });

    // Scalar loads its sidebar
    await expect(page.getByText('Introduction').first()).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole('heading', { name: '/health' })).toBeVisible();
  });

  test('09 — SignalR hub negotiation works', async ({ request }) => {
    const negotiate = await request.post(`${baseUrl}/hubs/engine/negotiate`, {
      data: '',
      headers: { 'Content-Type': 'application/json' },
    });
    expect(negotiate.status()).toBe(200);
    const body = await negotiate.json();
    expect(body.connectionId).toBeDefined();
    expect(body.availableTransports).toBeDefined();
    console.log(`[SIGNALR] negotiate OK, connectionId=${body.connectionId?.substring(0, 8)}...`);
  });

  test('10 — NotFound page renders for unknown routes', async ({ page }) => {
    await page.goto(`${baseUrl}/this-route-does-not-exist`, { waitUntil: 'networkidle', timeout: 30_000 });
    await page.screenshot({ path: `${screenshots}/10-notfound.png`, fullPage: true });

    // Should show some content, not a blank page
    const bodyText = await page.locator('body').innerText();
    expect(bodyText.length).toBeGreaterThan(0);

    // NavBar should still be visible
    await expect(page.getByText('PoMemeVideo')).toBeVisible();
  });
});
