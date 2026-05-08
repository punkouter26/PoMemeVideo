import { test, expect } from '@playwright/test';

test.describe('UI contracts and accessibility', () => {
  test('navigation exposes an explicit landmark label', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });
    await expect(page.getByRole('navigation', { name: 'Site navigation' })).toBeVisible();
  });

  test('maintenance controls live in Sound Admin page', async ({ page }) => {
    await page.goto('/admin/sounds', { waitUntil: 'networkidle' });
    const clearBtn = page.locator('button.clear-btn');
    await expect(clearBtn).toBeVisible();
    await expect(clearBtn).toContainText('CLEAR ALL DATA');
  });

  test('maintenance confirmation appears before destructive action', async ({ page }) => {
    await page.goto('/admin/sounds', { waitUntil: 'networkidle' });
    await page.locator('button.clear-btn').click();
    await expect(page.locator('button.clear-confirm-btn')).toBeVisible();
    await expect(page.locator('.clear-warning')).toContainText('DELETE ALL SESSION BLOBS');
  });
});
