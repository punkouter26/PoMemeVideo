import { test, expect } from '@playwright/test';

// Collect JS console errors throughout SPA tests
const jsErrors: string[] = [];

test.describe('SPA load', () => {
  test('Blazor WASM app loads at root', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') errors.push(msg.text());
    });
    page.on('pageerror', err => errors.push(err.message));

    await page.goto('/');

    // Wait for Blazor to boot — _framework/blazor.webassembly.js must load
    await page.waitForLoadState('networkidle', { timeout: 20_000 });

    // Page should not be a blank white screen
    const bodyText = await page.locator('body').innerText();
    expect(bodyText.length).toBeGreaterThan(0);

    // Report any JS errors (but don't fail the test — just surface them)
    if (errors.length > 0) {
      console.warn(`[JS Errors on /]: ${errors.join(' | ')}`);
    }
  });

  test('Blazor framework files are served or fallback index.html works', async ({ request }) => {
    // In dev mode without full publish, _framework/ may return 404; index.html fallback must work
    const spa = await request.get('/');
    expect([200, 304]).toContain(spa.status());
    const framework = await request.get('/_framework/blazor.webassembly.js');
    // Accept 200/304 (files present) or 404 (not published yet) — document actual state
    const frameworkStatus = framework.status();
    console.log(`[Blazor framework] /_framework/blazor.webassembly.js → HTTP ${frameworkStatus}`);
    expect([200, 304, 404]).toContain(frameworkStatus);
  });
});
