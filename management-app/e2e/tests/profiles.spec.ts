import { test, expect } from '@playwright/test'
import {
  SUPER_ADMIN,
  apiLogin,
  authHeader,
  createActiveAdmin,
  deleteAdmin,
  deleteUser,
  openAs,
  uniqueName,
} from './helpers'

// The backend runs with e2e/fixtures/xray-profiles and fixture-reality as its default profile.
const DEFAULT_PROFILE = 'fixture-reality'

test.describe('Xray profiles', () => {
  let superToken: string

  test.beforeEach(async ({ request }) => {
    superToken = await apiLogin(request, SUPER_ADMIN)
  })

  // The backend is shared between specs: always leave the default profile active.
  test.afterEach(async ({ request }) => {
    const res = await request.put('/api/v1/xray/active-profile', {
      headers: authHeader(superToken),
      data: { name: DEFAULT_PROFILE },
    })
    expect(res.status()).toBe(200)
  })

  test('super admin sees every profile, and why a broken one cannot be activated', async ({ page }) => {
    await openAs(page, SUPER_ADMIN, '/users')
    await page.getByRole('link', { name: 'Profiles' }).click()
    await expect(page).toHaveURL('/profiles')

    const reality = page.getByRole('region', { name: DEFAULT_PROFILE, exact: true })
    const ws = page.getByRole('region', { name: 'fixture-ws', exact: true })
    const broken = page.getByRole('region', { name: 'fixture-broken', exact: true })
    await expect(reality).toContainText('Active')
    await expect(reality).toContainText('vless-in: tcp + reality · xtls-rprx-vision')
    await expect(ws.getByRole('button', { name: 'Activate' })).toBeEnabled()
    await expect(broken).toContainText('Needs attention')
    await expect(broken).toContainText('config.json still contains YOUR_PRIVATE_KEY')
    await expect(broken.getByRole('button', { name: 'Activate' })).toBeDisabled()
  })

  test('activating a profile switches every user\'s config link', async ({ page, request }) => {
    const created = await request.post('/api/v1/users', {
      headers: authHeader(superToken),
      data: { name: uniqueName('profile-user'), quota: null, expiry: null, single_connection: false, enabled: true },
    })
    const { id } = await created.json()
    expect(await (await request.get(`/api/v1/config/${id}`)).text()).toContain('type=tcp&security=reality')

    await openAs(page, SUPER_ADMIN, '/profiles')
    page.on('dialog', (dialog) => dialog.accept())
    const ws = page.getByRole('region', { name: 'fixture-ws', exact: true })
    await ws.getByRole('button', { name: 'Activate' }).click()

    await expect(page.getByText('fixture-ws is now active.')).toBeVisible()
    await expect(ws).toContainText('Active')
    await expect(page.getByRole('region', { name: DEFAULT_PROFILE, exact: true })).not.toContainText('Active')
    const link = await (await request.get(`/api/v1/config/${id}`)).text()
    expect(link).toContain('@cdn.e2e.test:443')
    expect(link).toContain('type=ws')

    await deleteUser(request, superToken, id)
  })

  test('a normal admin has no Profiles page', async ({ page, request }) => {
    const credentials = await createActiveAdmin(request, superToken, uniqueName('profiles-admin'))
    const token = await openAs(page, credentials, '/users')

    await expect(page.getByRole('link', { name: 'Profiles' })).toHaveCount(0)
    await page.goto('/profiles')
    await expect(page).toHaveURL('/users')
    expect((await request.get('/api/v1/xray/profiles', { headers: authHeader(token) })).status()).toBe(403)

    await deleteAdmin(request, superToken, credentials.username)
  })
})
