import { test, expect } from '@playwright/test';

test.describe('Meme Library API', () => {
  test('GET /api/memelibrary/sounds returns sound list', async ({ request }) => {
    const res = await request.get('/api/memelibrary/sounds');
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body).toHaveProperty('totalCount');
    expect(body).toHaveProperty('sounds');
    expect(Array.isArray(body.sounds)).toBe(true);
  });

  test('GET /api/memelibrary/sounds respects limit parameter', async ({ request }) => {
    const res = await request.get('/api/memelibrary/sounds?limit=2');
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.sounds.length).toBeLessThanOrEqual(2);
  });
});
