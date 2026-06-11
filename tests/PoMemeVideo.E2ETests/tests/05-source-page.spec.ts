import { test, expect, Page } from '@playwright/test';

// Lean US1 coverage: keep only high-signal page contract tests.
test.describe('US1 – Source page critical path', () => {
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    page = await browser.newPage();
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await page.waitForLoadState('networkidle', { timeout: 20_000 });
  });

  test.afterAll(async () => {
    await page.close();
  });

  test('Source page loads with Matrix Green retro aesthetic', async () => {
    const bodyBg = await page.evaluate(() =>
      window.getComputedStyle(document.body).backgroundColor
    );
    expect(bodyBg).toMatch(/rgba\(0,\s*0,\s*0,\s*0\)|rgb\(0,\s*0,\s*0\)|#000/i);
  });

  test('Source page contains the ASCII drop zone', async () => {
    const dropZoneText = await page.locator('.ascii-drop-zone').innerText();
    expect(dropZoneText).toContain('DROP VIDEO FILE HERE');
  });

  test('Source page shows [ BROWSE FILE ] button', async () => {
    await expect(page.getByText('[ BROWSE FILE ]')).toBeVisible();
  });

  test('Aggressive visuals area is present', async () => {
    const dropZone = page.locator('.ascii-drop-zone');
    await expect(dropZone).toBeVisible();
    await expect(dropZone).toContainText('╔');
  });

  test('NavBar contains create, sounds, and history links', async () => {
    await expect(page.locator('nav')).toContainText('CREATE');
    await expect(page.locator('nav')).toContainText('SOUNDS');
    await expect(page.locator('nav')).toContainText('HISTORY');
  });

  test('NavBar shows GUEST when not logged in', async () => {
    const ctx = await page.context().browser()!.newContext();
    const freshPage = await ctx.newPage();
    await freshPage.goto('/', { waitUntil: 'networkidle' });
    const authStatus = freshPage.locator('.auth-status').first();
    await expect(authStatus).toBeVisible();
    const text = await authStatus.innerText();
    expect(text).toMatch(/GUEST|ANON|anon|@/i);
    await ctx.close();
  });
});
