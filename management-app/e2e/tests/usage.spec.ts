import { test, expect } from '@playwright/test'
import { SUPER_ADMIN, apiLogin, authHeader, openAs } from './helpers'

test.describe('Usage page', () => {
  test('usage page renders with no data', async ({ page }) => {
    await openAs(page, SUPER_ADMIN, '/usage')
    await expect(page.locator('h2')).toContainText(/usage/i)
  })

  test('usage shows user summary cards after data is available', async ({ page }) => {
    const access_token = await apiLogin(page.request, SUPER_ADMIN)

    // Create a user
    const userRes = await page.request.post('/api/v1/users', {
      headers: authHeader(access_token),
      data: {
        name: 'UsageTestUser',
        quota: 100 * 1024 * 1024 * 1024,
        expiry: null,
        single_connection: false,
        enabled: false,
      },
    })
    await userRes.json()

    await openAs(page, SUPER_ADMIN, '/usage')
    await expect(page.locator('span').filter({ hasText: 'UsageTestUser' }).first()).toBeVisible()
  })
})
