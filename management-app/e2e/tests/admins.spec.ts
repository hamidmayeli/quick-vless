import { test, expect } from '@playwright/test'
import {
  SUPER_ADMIN,
  apiLogin,
  authHeader,
  createActiveAdmin,
  createAdmin,
  createUser,
  deleteAdmin,
  deleteUser,
  openAs,
  signInThroughForm,
  uniqueName,
} from './helpers'

test.describe('Admin management', () => {
  let superToken: string
  const createdAdmins: string[] = []

  function newAdminName(prefix = 'e2e-admin') {
    const username = uniqueName(prefix)
    createdAdmins.push(username)
    return username
  }

  test.beforeEach(async ({ request }) => {
    superToken = await apiLogin(request, SUPER_ADMIN)
  })

  test.afterEach(async ({ request }) => {
    for (const username of createdAdmins.splice(0)) await deleteAdmin(request, superToken, username)
  })

  test('super admin sees the Admins page and is labelled as super admin', async ({ page }) => {
    await openAs(page, SUPER_ADMIN, '/users')

    await expect(page.getByTestId('signed-in-as')).toHaveText(`${SUPER_ADMIN.username} · Super admin`)
    await page.getByRole('link', { name: 'Admins' }).click()

    await expect(page).toHaveURL('/admins')
    await expect(page.locator('h2')).toHaveText('Admins')
    await expect(page.getByRole('row', { name: new RegExp(`^${SUPER_ADMIN.username} Super admin`) })).toBeVisible()
  })

  test('creating an admin asks for no password and shows them as pending', async ({ page }) => {
    const username = newAdminName()
    await openAs(page, SUPER_ADMIN, '/admins')

    await page.getByRole('button', { name: /add admin/i }).click()
    await expect(page.locator('h3')).toHaveText('New Admin')
    await expect(page.locator('form input[type="password"]')).toHaveCount(0)
    await page.getByLabel('Username').fill(username)
    await page.getByRole('button', { name: /^save$/i }).click()

    await expect(page.getByRole('row', { name: new RegExp(username) })).toContainText('Pending first login')
  })

  test('creating a duplicate admin shows an error', async ({ page, request }) => {
    const username = newAdminName()
    await createAdmin(request, superToken, username)
    await openAs(page, SUPER_ADMIN, '/admins')

    await page.getByRole('button', { name: /add admin/i }).click()
    await page.getByLabel('Username').fill(username)
    await page.getByRole('button', { name: /^save$/i }).click()

    await expect(page.getByText(/already exists/i)).toBeVisible()
  })

  test('a new admin sets their password on first sign-in', async ({ page, request }) => {
    const username = newAdminName()
    await createAdmin(request, superToken, username)

    await page.goto('/login')
    await expect(page.getByText(/first time signing in/i)).toBeVisible()
    await signInThroughForm(page, { username, password: 'chosen-at-first-login' })

    await expect(page).toHaveURL('/users')
    await expect(page.locator('text=No users yet')).toBeVisible()
    await expect(page.getByTestId('signed-in-as')).toHaveText(username)
    await expect(page.getByRole('link', { name: 'Admins' })).toHaveCount(0)
  })

  test('the first password sticks and a different one is rejected', async ({ page, request }) => {
    const username = newAdminName()
    await createAdmin(request, superToken, username)
    await apiLogin(request, { username, password: 'first-password' })

    await page.goto('/login')
    await signInThroughForm(page, { username, password: 'second-password' })
    await expect(page.getByText(/invalid username or password/i)).toBeVisible()
    await expect(page).toHaveURL('/login')

    await signInThroughForm(page, { username, password: 'first-password' })
    await expect(page).toHaveURL('/users')

    const admins = await (await request.get('/api/v1/admins', { headers: authHeader(superToken) })).json()
    expect(admins.find((a: { username: string }) => a.username === username)).toMatchObject({ status: 'active' })
  })

  test('a normal admin cannot open the Admins page', async ({ page, request }) => {
    const credentials = await createActiveAdmin(request, superToken, newAdminName())
    const token = await openAs(page, credentials, '/users')

    await page.goto('/admins')

    await expect(page).toHaveURL('/users')
    const res = await request.get('/api/v1/admins', { headers: authHeader(token) })
    expect(res.status()).toBe(403)
  })

  test('deleting an admin revokes their access and hands their users to the super admin', async ({ page, request }) => {
    const credentials = await createActiveAdmin(request, superToken, newAdminName())
    const adminToken = await apiLogin(request, credentials)
    const userName = uniqueName('handover')
    const { id } = await createUser(request, adminToken, userName)
    await openAs(page, SUPER_ADMIN, '/admins')

    page.on('dialog', (dialog) => dialog.accept())
    const row = page.getByRole('row', { name: new RegExp(credentials.username) })
    await expect(row.locator('td[data-label="Users"]')).toHaveText('1')
    await row.getByRole('button', { name: /delete/i }).click()

    await expect(row).toHaveCount(0)
    const login = await request.post('/api/v1/auth/login', { data: credentials })
    expect(login.status()).toBe(401)
    const users = await (await request.get('/api/v1/users', { headers: authHeader(superToken) })).json()
    expect(users.find((u: { id: string }) => u.id === id)).toMatchObject({ created_by: SUPER_ADMIN.username })

    await deleteUser(request, superToken, id)
  })
})
