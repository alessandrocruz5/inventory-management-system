import { defineConfig, devices } from '@playwright/test';

const password = process.env['E2E_PASSWORD'];

export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1, // the tests share the demo tenant; a rebuild must not interleave a posting
  retries: 0, // no retries hiding flakes
  forbidOnly: !!process.env['CI'],
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    // The same graph as `dotnet run --project src/AppHost`: sql → migrate → seed → api → web (:4200).
    command: 'dotnet run --project ../../src/AppHost --launch-profile http',
    url: 'http://localhost:4200/api/auth/me', // a 401 counts as ready: proxy and api are both up
    reuseExistingServer: !process.env['CI'],
    timeout: 15 * 60_000,
    stdout: 'pipe',
    stderr: 'pipe',
    env: {
      ASPIRE_ALLOW_UNSECURED_TRANSPORT: 'true',
      ...(password ? { 'Parameters__seed-password': password } : {}),
    },
  },
});
