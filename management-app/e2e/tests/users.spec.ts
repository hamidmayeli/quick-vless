import { test, expect, type Page } from '@playwright/test'
import { SUPER_ADMIN, apiLogin, authHeader, createUser, openAs } from './helpers'

async function loginAndNavigate(page: Page) {
  await openAs(page, SUPER_ADMIN, '/users')
}

test.describe('User management', () => {
  test.beforeEach(async ({ request }) => {
    // The super admin sees every user, so this wipes the whole user list.
    const token = await apiLogin(request, SUPER_ADMIN)
    await request.put('/api/v1/settings', { headers: authHeader(token), data: { domain_override: '' } })
    const usersRes = await request.get('/api/v1/users', { headers: authHeader(token) })
    const users = await usersRes.json()
    for (const user of users) {
      await request.delete(`/api/v1/users/${user.id}`, { headers: authHeader(token) })
    }
  })

  test('super admin sees the Owner column', async ({ page, request }) => {
    await createUser(request, await apiLogin(request, SUPER_ADMIN), 'OwnedBySuper')
    await loginAndNavigate(page)

    await expect(page.getByRole('columnheader', { name: 'Owner' })).toBeVisible()
    await expect(page.getByRole('row', { name: /OwnedBySuper/ })).toContainText(SUPER_ADMIN.username)
  })

  test('users page shows empty state message', async ({ page }) => {
    await loginAndNavigate(page)
    await expect(page.locator('text=No users yet')).toBeVisible()
  })

  test('mobile navigation opens all menu items from the bottom bar', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await loginAndNavigate(page)

    const nav = page.locator('nav')
    await expect(nav).toHaveCSS('position', 'fixed')
    await expect(nav).toHaveCSS('bottom', '0px')
    await page.getByRole('button', { name: 'Open menu' }).click()
    await expect(nav.getByRole('button', { name: 'Close menu' })).toHaveAttribute('aria-expanded', 'true')
    for (const item of ['Users', 'Usage', 'Admins', 'Profiles']) {
      await expect(page.getByRole('link', { name: item })).toBeVisible()
    }
    await expect(page.getByRole('button', { name: 'Settings' })).toBeVisible()
    await expect(page.getByTestId('signed-in-as')).toHaveText('admin · Super admin')
    await expect(page.getByRole('button', { name: 'Logout' })).toBeVisible()

    await page.getByRole('link', { name: 'Usage' }).click()
    await expect(page).toHaveURL(/\/usage$/)
    await expect(page.getByRole('button', { name: 'Open menu' })).toHaveAttribute('aria-expanded', 'false')
  })

  test('copies config URLs using the saved domain override', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const token = await apiLogin(request, SUPER_ADMIN)
    const user = await createUser(request, token, 'DomainOverrideUser')
    await loginAndNavigate(page)

    await page.getByRole('button', { name: 'Settings' }).click()
    await page.getByLabel('Domain override').fill('links.example.com')
    await page.getByRole('button', { name: 'Save domain' }).click()
    await expect(page.getByLabel('Domain override')).toHaveValue('links.example.com')
    await page.getByRole('button', { name: 'Done' }).click()

    await page.getByRole('button', { name: 'Copy Config URL' }).click()

    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText()))
      .toBe(`https://links.example.com/api/v1/config/${user.id}`)
  })

  test('copies with the current domain and warns when the domain override cannot load', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const token = await apiLogin(request, SUPER_ADMIN)
    const user = await createUser(request, token, 'DomainOverrideFallbackUser')
    await page.route('**/api/v1/settings', (route) => route.abort())
    await loginAndNavigate(page)

    await page.getByRole('button', { name: 'Copy Config URL' }).click()

    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText()))
      .toBe(`${new URL(page.url()).origin}/api/v1/config/${user.id}`)
    await expect(page.getByRole('alert')).toContainText('copied using the current domain')
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
