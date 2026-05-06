import { test, expect } from '@playwright/test';

test.describe('Ingestion API', () => {
  test('POST /api/ingestion/sas rejects unsupported file extensions', async ({ request }) => {
    const res = await request.post('/api/ingestion/sas', {
      data: { fileName: 'malware.exe', fileSizeBytes: 1024 },
    });
    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.error).toBe('INVALID_EXTENSION');
    expect(body).toHaveProperty('allowedExtensions');
  });

  test('POST /api/ingestion/sas rejects files over size limit', async ({ request }) => {
    const fiveGB = 5 * 1024 * 1024 * 1024;
    const res = await request.post('/api/ingestion/sas', {
      data: { fileName: 'huge.mp4', fileSizeBytes: fiveGB },
    });
    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.error).toBe('FILE_TOO_LARGE');
    expect(body).toHaveProperty('maxBytes');
  });

  test('POST /api/ingestion/sas returns sessionId and sasUrl for valid request', async ({ request }) => {
    const res = await request.post('/api/ingestion/sas', {
      data: { fileName: 'test-video.mp4', fileSizeBytes: 50 * 1024 * 1024 },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body).toHaveProperty('sessionId');
    expect(body).toHaveProperty('sasUrl');
    // sessionId must be a valid GUID
    expect(body.sessionId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
    );
  });

  test('GET /api/ingestion/sessions/{id} returns 404 for unknown session', async ({ request }) => {
    const fakeId = '00000000-0000-0000-0000-000000000001';
    const res = await request.get(`/api/ingestion/sessions/${fakeId}`);
    expect([404, 400]).toContain(res.status());
  });
});
