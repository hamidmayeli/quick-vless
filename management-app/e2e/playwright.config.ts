import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup',
  timeout: 30_000,
  retries: process.env.CI ? 2 : 0,
  // All specs share one stateful backend (users.spec wipes every user), so run them one at a time.
  workers: 1,
  fullyParallel: false,
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:8080',
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } },
  ],
})
