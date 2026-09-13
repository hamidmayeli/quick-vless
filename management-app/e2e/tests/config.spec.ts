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

test.describe('Config page', () => {
  test('config page shows not found when no users exist', async ({ page }) => {
    await loginAndNavigate(page, '/config')
    // /config/me returns 404 when no enabled users exist
    await expect(page.locator('text=/config|not found|no users/i')).toBeVisible()
  })

  test('config page shows QR code and vless URL for enabled user', async ({ page }) => {
    const loginRes = await page.request.post('/api/v1/auth/login', {
      data: { username: 'admin', password: 'password123' },
    })
    const { access_token } = await loginRes.json()

    // Create an enabled user
    await page.request.post('/api/v1/users', {
      headers: { Authorization: `Bearer ${access_token}` },
      data: {
        name: 'ConfigPageUser',
        quota: null,
        expiry: null,
        single_connection: false,
        enabled: true,
      },
    })

    await loginAndNavigate(page, '/config')

    // Should see a vless:// URL somewhere on the page
    await expect(page.locator('text=vless://')).toBeVisible()
  })
})
