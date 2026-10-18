import { request } from '@playwright/test'

export default async function globalSetup() {
  const ctx = await request.newContext({
    baseURL: process.env.BASE_URL ?? 'http://localhost:8080',
  })
  // On an empty backend this first login creates the super admin.
  const res = await ctx.post('/api/v1/auth/login', {
    data: { username: 'admin', password: 'password123' },
  })
  if (!res.ok()) {
    throw new Error(`e2e setup: login as admin failed (${res.status()}). Start the backend with an empty /data, or with admin/password123 as its super admin.`)
  }
  const { access_token } = await res.json()
  const payload = JSON.parse(Buffer.from(access_token.split('.')[1], 'base64url').toString()) as Record<string, unknown>
  const role = Object.entries(payload).find(([key]) => key === 'role' || key.endsWith('/role'))?.[1]
  if (role !== 'super_admin') {
    throw new Error(`e2e setup: "admin" must be the super admin but has role ${JSON.stringify(role)}.`)
  }
  await ctx.dispose()
}
