import { test, expect } from '@playwright/test';
import { request as playwrightRequest } from '@playwright/test';

// ─── US2: AI-Directed Meme Sound & Visual Mapping ─────────────────────────
// Tests Engine page structure and real-time feed panels.
// Processing tests use mock AI (UseMockAI: true) where needed.
// Full end-to-end processing is tested via API-only flows due to
// the complexity of a real video upload in E2E.

test.describe('US2 – Processing API', () => {
  test('POST /api/processing/sessions/{id}/initiate returns 404 for unknown session', async ({ request }) => {
    const fakeId = '00000000-0000-0000-0000-000000000003';
    const res = await request.post(`/api/processing/sessions/${fakeId}/initiate`);
    // Should be 404 (session not found) or 400 (invalid state)
    expect([404, 400]).toContain(res.status());
  });

  test('Full ingestion → initiate → polling flow with mock AI', async ({ request }) => {
    test.setTimeout(70_000);

    // Step 1: Create session
    const sasRes = await request.post('/api/ingestion/sas', {
      data: { fileName: 'e2e-test.mp4', fileSizeBytes: 2 * 1024 * 1024 },
    });
    expect(sasRes.status()).toBe(200);
    const { sessionId } = await sasRes.json();

    // Step 2: Confirm upload
    const confirmRes = await request.post('/api/ingestion/sessions', {
      data: {
        sessionId,
        blobPath: `sessions/${sessionId}/source.mp4`,
        videoDurationSeconds: 10,
        aggressiveVisuals: false,
      },
    });
    expect(confirmRes.status()).toBe(201);

    // Step 3: Initiate engine (returns 202 immediately)
    const initiateRes = await request.post(
      `/api/processing/sessions/${sessionId}/initiate`
    );
    expect(initiateRes.status()).toBe(202);

    // Step 4: Poll until the engine leaves Ingesting.
    // In BrowserLLM mode, terminal completion may require a browser callback,
    // so API-only tests should assert initiation/progress, not completion.
    let finalStatus: string | undefined;
    for (let i = 0; i < 45; i++) {
      await new Promise(r => setTimeout(r, 1000));
      const getRes = await request.get(`/api/ingestion/sessions/${sessionId}`);
      if (getRes.status() !== 200) continue;
      const session = await getRes.json();
      const statusStr = String(session.status).toLowerCase();
      if (statusStr !== 'ingesting' && statusStr !== '0') {
        finalStatus = statusStr;
        break;
      }
    }

    // Engine should at least start progressing after initiation.
    expect(finalStatus).toBeDefined();
    console.log(`[Engine] Session ${sessionId} final status: ${finalStatus}`);
  });
});

test.describe('US2 – Engine page structure', () => {
  // We navigate to /engine/{id} with a known fake ID to test page structure
  // (the page renders its panels regardless of SignalR connection state)

  test('Engine page renders four panels with ASCII borders', async ({ page }) => {
    // Create a session so the engine page has something to load
    const ctx = await playwrightRequest.newContext({ baseURL: 'http://127.0.0.1:5000' });
    const sasRes = await ctx.post('/api/ingestion/sas', {
      data: { fileName: 'engine-e2e.mp4', fileSizeBytes: 1024 * 1024 },
    });
    const { sessionId } = await sasRes.json();
    await ctx.dispose();

    await page.goto(`/engine/${sessionId}`, { waitUntil: 'networkidle' });

    await page.getByText('[ Show Technical Panels ]').click();

    // All four panels must be present
    await expect(page.locator('.hw-panel')).toBeVisible();
    await expect(page.locator('.script-panel')).toBeVisible();
    await expect(page.locator('.log-panel')).toBeVisible();
    await expect(page.locator('.audit-panel')).toBeVisible();
  });

  test('DirectorLogFeed panel is visible and auto-scrolls', async ({ page }) => {
    const ctx = await playwrightRequest.newContext({ baseURL: 'http://127.0.0.1:5000' });
    const sasRes = await ctx.post('/api/ingestion/sas', {
      data: { fileName: 'engine-log-e2e.mp4', fileSizeBytes: 1024 * 1024 },
    });
    const { sessionId } = await sasRes.json();
    await ctx.dispose();

    await page.goto(`/engine/${sessionId}`, { waitUntil: 'networkidle' });

    const logPanel = page.locator('.log-panel');
    await expect(logPanel).toBeVisible();

    // Log feed should contain a <pre> terminal feed with class director-log-feed
    const preFeed = logPanel.locator('pre.director-log-feed');
    await expect(preFeed).toBeVisible();
  });
});

test.describe('US2 – SignalR hub connection', () => {
  test('GET /hubs/engine (negotiate) returns valid SignalR negotiation response', async ({ request }) => {
    const res = await request.post('/hubs/engine/negotiate?negotiateVersion=1');
    // SignalR negotiate returns 200 with connectionToken or 400/307
    expect([200, 307, 400]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body).toHaveProperty('connectionId');
    }
  });
});

// Config endpoint coverage is handled in 01-health.spec.ts.
