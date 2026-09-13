import { request } from '@playwright/test'

export default async function globalSetup() {
  const ctx = await request.newContext({
    baseURL: process.env.BASE_URL ?? 'http://localhost:8080',
  })
  await ctx.post('/api/v1/auth/login', {
    data: { username: 'admin', password: 'password123' },
  })
  await ctx.dispose()
}
