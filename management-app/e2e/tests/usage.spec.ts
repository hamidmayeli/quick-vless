import { test, expect, type Page } from '@playwright/test'

async function loginAndNavigate(page: Page, path: string) {
  const res = await page.request.post('/api/v1/auth/login', {
    data: { username: 'admin', password: 'password123' },
  })
  const { access_token, refresh_token } = await res.json()
  await page.goto(path)
  await page.evaluate(
    ({ at, rt }) => {
      localStorage.setItem('access_token', at)
      localStorage.setItem('refresh_token', rt)
    },
    { at: access_token, rt: refresh_token },
  )
  await page.reload()
  await expect(page).toHaveURL(path)
}

test.describe('Usage page', () => {
  test('usage page renders with no data', async ({ page }) => {
    await loginAndNavigate(page, '/usage')
    await expect(page.locator('h2')).toContainText(/usage/i)
  })

  test('usage shows user summary cards after data is available', async ({ page }) => {
    const loginRes = await page.request.post('/api/v1/auth/login', {
      data: { username: 'admin', password: 'password123' },
    })
    const { access_token } = await loginRes.json()

    // Create a user
    const userRes = await page.request.post('/api/v1/users', {
      headers: { Authorization: `Bearer ${access_token}` },
      data: {
        name: 'UsageTestUser',
        quota: 100,
        expiry: null,
        single_connection: false,
        enabled: false,
      },
    })
    await userRes.json()

    await loginAndNavigate(page, '/usage')
    await expect(page.locator('text=UsageTestUser')).toBeVisible()
  })
})
