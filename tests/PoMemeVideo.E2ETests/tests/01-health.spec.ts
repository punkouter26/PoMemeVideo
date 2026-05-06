import { test, expect } from '@playwright/test';

test.describe('Health endpoint', () => {
  test('GET /health returns 200 with Healthy status', async ({ request }) => {
    const res = await request.get('/health');
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.status).toBe('Healthy');
  });

  test('GET /api/config returns feature-flag configuration', async ({ request }) => {
    const res = await request.get('/api/config');
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body).toHaveProperty('useMockAI');
    expect(body.useMockAI).toBe(true);
  });

  test('GET /api/config/ai-model returns provider info', async ({ request }) => {
    const res = await request.get('/api/config/ai-model');
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body).toHaveProperty('provider');
  });
});
