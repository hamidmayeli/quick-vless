import { test, expect, type Page } from '@playwright/test'

async function loginAndNavigate(page: Page) {
  const res = await page.request.post('/api/v1/auth/login', {
    data: { username: 'admin', password: 'password123' },
  })
  const { access_token, refresh_token } = await res.json()
  await page.goto('/users')
  await page.evaluate(
    ({ at, rt }) => {
      localStorage.setItem('access_token', at)
      localStorage.setItem('refresh_token', rt)
    },
    { at: access_token, rt: refresh_token },
  )
  await page.reload()
  await expect(page).toHaveURL('/users')
}

test.describe('User management', () => {
  test('users page shows empty state message', async ({ page }) => {
    await loginAndNavigate(page)
    await expect(page.locator('text=No users yet')).toBeVisible()
  })

  test('add user opens form and creates user', async ({ page }) => {
    await loginAndNavigate(page)

    await page.getByRole('button', { name: /add user/i }).click()
    await expect(page.locator('h3')).toContainText('New User')

    await page.locator('input').first().fill('E2E TestUser')
    await page.getByRole('button', { name: /^save$/i }).click()

    await expect(page.locator('text=E2E TestUser')).toBeVisible()
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
