import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  outputDir: './artifacts/test-results',
  timeout: 30_000,
  expect: { timeout: 8_000 },
  fullyParallel: false,        // serial — serial mutation tests share state
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,                  // single worker keeps cost/CI runtime low
  reporter: [['list'], ['html', { outputFolder: 'artifacts/playwright-report', open: 'never' }]],

  use: {
    baseURL: 'http://127.0.0.1:7000',
    headless: !!process.env.CI,
    viewport: { width: 1280, height: 800 },
    // Capture JS console errors for reporting
    ignoreHTTPSErrors: true,
    screenshot: 'only-on-failure',
    video: 'off',
  },

  webServer: {
    command: 'dotnet run --project ../../src/PoMemeVideo.Api/PoMemeVideo.Api.csproj --no-build',
    url: 'http://127.0.0.1:7000/health',
    reuseExistingServer: true,
    timeout: 60_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: 'https://127.0.0.1:5001;http://127.0.0.1:7000',
    },
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
