import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 8_000 },
  fullyParallel: false,        // serial — serial mutation tests share state
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,                  // single worker keeps cost/CI runtime low
  reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],

  use: {
    baseURL: 'http://127.0.0.1:8000',
    headless: true,
    viewport: { width: 1280, height: 800 },
    // Capture JS console errors for reporting
    ignoreHTTPSErrors: true,
    screenshot: 'only-on-failure',
    video: 'off',
  },

  webServer: {
    command: 'dotnet run --project ../../src/PoMemeVideo.Api/PoMemeVideo.Api.csproj --no-build',
    url: 'http://127.0.0.1:8000/health',
    reuseExistingServer: true,
    timeout: 60_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: 'http://127.0.0.1:8000',
    },
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
