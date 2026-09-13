import { test, expect } from '@playwright/test'

test.describe('Auth flow', () => {
  test('login page renders', async ({ page }) => {
    await page.goto('/login')
    await expect(page.locator('h1')).toContainText('Easy Xray')
    await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible()
  })

  test('first login creates admin and redirects to /users', async ({ page }) => {
    await page.goto('/login')
    await page.locator('input[type="text"]').fill('admin')
    await page.locator('input[type="password"]').fill('password123')
    await page.getByRole('button', { name: /sign in/i }).click()

    await expect(page).toHaveURL('/users')
    await expect(page.locator('h2')).toContainText('Users')
  })

  test('wrong password shows error and stays on login', async ({ page }) => {
    // Ensure admin exists via API
    await page.request.post('/api/v1/auth/login', {
      data: { username: 'admin', password: 'password123' },
    })

    await page.goto('/login')
    await page.locator('input[type="text"]').fill('admin')
    await page.locator('input[type="password"]').fill('wrongpassword')
    await page.getByRole('button', { name: /sign in/i }).click()

    await expect(page).toHaveURL('/login')
    await expect(page.locator('p')).toContainText(/invalid username or password/i)
  })

  test('logout clears session and redirects to login', async ({ page }) => {
    // Login via API to set tokens
    const loginRes = await page.request.post('/api/v1/auth/login', {
      data: { username: 'admin', password: 'password123' },
    })
    const { access_token } = await loginRes.json()

    await page.goto('/users')
    await page.evaluate((token) => {
      localStorage.setItem('access_token', token)
    }, access_token)
    await page.reload()

    await expect(page).toHaveURL('/users')
    await page.getByRole('button', { name: /logout/i }).click()

    await expect(page).toHaveURL('/login')
  })

  test('unauthenticated access to /users redirects to login', async ({ page }) => {
    await page.goto('/users')
    await expect(page).toHaveURL('/login')
  })
})
