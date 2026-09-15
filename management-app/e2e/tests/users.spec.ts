import { test, expect, type Page } from '@playwright/test'

async function loginAndNavigate(page: Page) {
  const res = await page.request.post('/api/v1/auth/login', {
    data: { username: 'admin', password: 'password123' },
  })
  const { access_token } = await res.json()
  await page.goto('/users')
  await page.evaluate(
    ({ at }) => {
      localStorage.setItem('access_token', at)
    },
    { at: access_token },
  )
  await page.reload()
  await expect(page).toHaveURL('/users')
}

test.describe('User management', () => {
  test.beforeEach(async ({ request }) => {
    const res = await request.post('/api/v1/auth/login', {
      data: { username: 'admin', password: 'password123' },
    })
    const { access_token } = await res.json()
    const usersRes = await request.get('/api/v1/users', {
      headers: { Authorization: `Bearer ${access_token}` },
    })
    const users = await usersRes.json()
    for (const user of users) {
      await request.delete(`/api/v1/users/${user.id}`, {
        headers: { Authorization: `Bearer ${access_token}` },
      })
    }
  })

  test('users page shows empty state message', async ({ page }) => {
    await loginAndNavigate(page)
    await expect(page.locator('text=No users yet')).toBeVisible()
  })

  test('refreshes users when the page becomes visible again', async ({ page }) => {
    let userRequests = 0
    page.on('request', (request) => {
      if (request.url().endsWith('/api/v1/users') && request.method() === 'GET') userRequests += 1
    })

    await loginAndNavigate(page)
    await expect(page.locator('text=No users yet')).toBeVisible()
    const requestsBeforeReconnect = userRequests

    await page.evaluate(() => {
      Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
      document.dispatchEvent(new Event('visibilitychange'))
      Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
      document.dispatchEvent(new Event('visibilitychange'))
    })

    await expect.poll(() => userRequests).toBeGreaterThan(requestsBeforeReconnect)
  })

  test('add user opens form and creates user', async ({ page }) => {
    await loginAndNavigate(page)

    await page.getByRole('button', { name: /add user/i }).click()
    await expect(page.locator('h3')).toContainText('New User')

    await page.locator('input').first().fill('E2E TestUser')
    await page.getByRole('button', { name: /^save$/i }).click()

    await expect(page.locator('text=E2E TestUser')).toBeVisible()
  })

  test('accepts a human-readable quota and stores bytes', async ({ page }) => {
    await loginAndNavigate(page)

    await page.getByRole('button', { name: /add user/i }).click()
    await page.locator('input').nth(1).fill('10MG')
    await page.locator('input').first().fill('ByteQuotaUser')
    await page.getByRole('button', { name: /^save$/i }).click()

    await expect(page.locator('text=ByteQuotaUser')).toBeVisible()
    await expect(page.locator('text=10 MB')).toBeVisible()
  })

  test('rejects an invalid quota value', async ({ page }) => {
    await loginAndNavigate(page)

    await page.getByRole('button', { name: /add user/i }).click()
    await page.locator('input').nth(1).fill('ten gigabytes')
    await page.getByRole('button', { name: /^save$/i }).click()

    await expect(page.getByText(/enter a quota such as/i)).toBeVisible()
    await expect(page.getByText('New User')).toBeVisible()
  })

  test('edit user updates name', async ({ page }) => {
    await loginAndNavigate(page)

    // Create user via API
    const token = await page.evaluate(() => localStorage.getItem('access_token'))
    await page.request.post('/api/v1/users', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'BeforeEdit',
        quota: null,
        expiry: null,
        single_connection: false,
        enabled: false,
      },
    })
    await page.reload()

    await page.getByRole('button', { name: /^edit$/i }).first().click()
    const nameInput = page.locator('input').first()
    await nameInput.clear()
    await nameInput.fill('AfterEdit')
    await page.getByRole('button', { name: /^save$/i }).click()

    await expect(page.locator('text=AfterEdit')).toBeVisible()
  })

  test('delete user with confirmation removes it from list', async ({ page }) => {
    await loginAndNavigate(page)

    // Create user via API
    const token = await page.evaluate(() => localStorage.getItem('access_token'))
    await page.request.post('/api/v1/users', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'ToDelete',
        quota: null,
        expiry: null,
        single_connection: false,
        enabled: false,
      },
    })
    await page.reload()

    page.on('dialog', (d) => d.accept())
    await page.getByRole('button', { name: /delete/i }).first().click()

    await expect(page.locator('text=ToDelete')).not.toBeVisible()
  })
})
