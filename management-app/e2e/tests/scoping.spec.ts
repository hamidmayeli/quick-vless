import { test, expect } from '@playwright/test'
import {
  SUPER_ADMIN,
  apiLogin,
  authHeader,
  createActiveAdmin,
  createUser,
  deleteAdmin,
  deleteUser,
  openAs,
  uniqueName,
  type Credentials,
} from './helpers'

test.describe('Per-admin scoping', () => {
  let superToken: string
  let adminA: Credentials
  let adminB: Credentials
  let userA: { id: string; name: string }
  let userB: { id: string; name: string }

  test.beforeEach(async ({ request }) => {
    superToken = await apiLogin(request, SUPER_ADMIN)
    adminA = await createActiveAdmin(request, superToken, uniqueName('scope-a'))
    adminB = await createActiveAdmin(request, superToken, uniqueName('scope-b'))
    const nameA = uniqueName('A-user')
    const nameB = uniqueName('B-user')
    userA = { name: nameA, ...(await createUser(request, await apiLogin(request, adminA), nameA)) }
    userB = { name: nameB, ...(await createUser(request, await apiLogin(request, adminB), nameB)) }
  })

  test.afterEach(async ({ request }) => {
    await deleteUser(request, superToken, userA.id)
    await deleteUser(request, superToken, userB.id)
    await deleteAdmin(request, superToken, adminA.username)
    await deleteAdmin(request, superToken, adminB.username)
  })

  test('each admin sees only the users they created', async ({ page }) => {
    await openAs(page, adminA, '/users')
    await expect(page.getByRole('cell', { name: userA.name })).toBeVisible()
    await expect(page.getByRole('cell', { name: userB.name })).toHaveCount(0)

    await openAs(page, adminB, '/users')
    await expect(page.getByRole('cell', { name: userB.name })).toBeVisible()
    await expect(page.getByRole('cell', { name: userA.name })).toHaveCount(0)
  })

  test('super admin sees every admin\'s users with an Owner column', async ({ page }) => {
    await openAs(page, SUPER_ADMIN, '/users')

    await expect(page.getByRole('columnheader', { name: 'Owner' })).toBeVisible()
    await expect(page.getByRole('row', { name: new RegExp(userA.name) })).toContainText(adminA.username)
    await expect(page.getByRole('row', { name: new RegExp(userB.name) })).toContainText(adminB.username)
  })

  test('normal admins do not get an Owner column', async ({ page }) => {
    await openAs(page, adminA, '/users')

    await expect(page.getByRole('cell', { name: userA.name })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Owner' })).toHaveCount(0)
  })

  test('usage report only covers the admin\'s own users', async ({ page }) => {
    await openAs(page, adminA, '/usage')
    await expect(page.getByRole('checkbox', { name: userA.name })).toBeVisible()
    await expect(page.getByRole('checkbox', { name: userB.name })).toHaveCount(0)

    await openAs(page, SUPER_ADMIN, '/usage')
    await expect(page.getByRole('checkbox', { name: userA.name })).toBeVisible()
    await expect(page.getByRole('checkbox', { name: userB.name })).toBeVisible()
  })

  test('an admin cannot change or delete another admin\'s user', async ({ request }) => {
    const tokenA = await apiLogin(request, adminA)

    const update = await request.put(`/api/v1/users/${userB.id}`, {
      headers: authHeader(tokenA),
      data: { name: 'hijacked' },
    })
    const remove = await request.delete(`/api/v1/users/${userB.id}`, { headers: authHeader(tokenA) })

    expect(update.status()).toBe(404)
    expect(remove.status()).toBe(404)
    const users = await (await request.get('/api/v1/users', { headers: authHeader(superToken) })).json()
    expect(users.find((u: { id: string }) => u.id === userB.id)).toMatchObject({ name: userB.name })
  })
})
