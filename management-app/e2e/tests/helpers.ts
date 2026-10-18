import { expect, type APIRequestContext, type Page } from '@playwright/test'

export interface Credentials {
  username: string
  password: string
}

// Bootstrapped by global-setup: the first login on an empty backend creates the super admin.
export const SUPER_ADMIN: Credentials = { username: 'admin', password: 'password123' }

// The backend persists between runs, so anything a test creates needs a unique name.
export function uniqueName(prefix: string): string {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`
}

export function authHeader(token: string) {
  return { Authorization: `Bearer ${token}` }
}

export async function apiLogin(request: APIRequestContext, { username, password }: Credentials): Promise<string> {
  const res = await request.post('/api/v1/auth/login', { data: { username, password } })
  expect(res.status(), `login as ${username}`).toBe(200)
  const { access_token } = await res.json()
  return access_token
}

/** Logs in through the API, stores the token like the app does, and opens `path`. */
export async function openAs(page: Page, credentials: Credentials, path: string): Promise<string> {
  const token = await apiLogin(page.request, credentials)
  await page.goto(path)
  await page.evaluate((at) => localStorage.setItem('access_token', at), token)
  await page.goto(path)
  await expect(page).toHaveURL(path)
  return token
}

export async function signInThroughForm(page: Page, { username, password }: Credentials) {
  await page.getByLabel('Username').fill(username)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: /sign in/i }).click()
}

export async function createAdmin(request: APIRequestContext, superToken: string, username: string) {
  const res = await request.post('/api/v1/admins', { headers: authHeader(superToken), data: { username } })
  expect(res.status(), `create admin ${username}`).toBe(201)
}

/** Creates a pending admin, then signs in once as them — which sets their password. */
export async function createActiveAdmin(
  request: APIRequestContext,
  superToken: string,
  username: string,
  password = 'admin-pass-123',
): Promise<Credentials> {
  await createAdmin(request, superToken, username)
  const credentials = { username, password }
  await apiLogin(request, credentials)
  return credentials
}

export async function deleteAdmin(request: APIRequestContext, superToken: string, username: string) {
  await request.delete(`/api/v1/admins/${encodeURIComponent(username)}`, { headers: authHeader(superToken) })
}

export async function createUser(request: APIRequestContext, token: string, name: string): Promise<{ id: string }> {
  const res = await request.post('/api/v1/users', {
    headers: authHeader(token),
    data: { name, quota: null, expiry: null, single_connection: false, enabled: false },
  })
  expect(res.status(), `create user ${name}`).toBe(201)
  return res.json()
}

export async function deleteUser(request: APIRequestContext, token: string, id: string) {
  await request.delete(`/api/v1/users/${id}`, { headers: authHeader(token) })
}
