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

  test('GET /api/memelibrary/sounds filters by tags', async ({ request }) => {
    // First get all sounds to find a real tag to filter by
    const allRes = await request.get('/api/memelibrary/sounds');
    const allBody = await allRes.json();
    if (allBody.totalCount === 0) {
      test.skip();
      return;
    }
    const firstTag: string = allBody.sounds[0]?.actionVectorTags?.[0];
    if (!firstTag) {
      test.skip();
      return;
    }
    const filtered = await request.get(`/api/memelibrary/sounds?tags=${encodeURIComponent(firstTag)}`);
    expect(filtered.status()).toBe(200);
    const filteredBody = await filtered.json();
    // Every returned sound must contain the requested tag
    for (const sound of filteredBody.sounds) {
      const tagsLower = (sound.actionVectorTags as string[]).map(t => t.toLowerCase());
      expect(tagsLower).toContain(firstTag.toLowerCase());
    }
  });
});
