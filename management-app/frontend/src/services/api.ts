import type { AuthTokens, User, UsageRecord } from '@/types'

const BASE = '/api/v1'

function authHeaders(): HeadersInit {
  const token = localStorage.getItem('access_token')
  return token ? { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } : { 'Content-Type': 'application/json' }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, { ...init, headers: { ...authHeaders(), ...init?.headers } })
  if (res.status === 401) {
    if (localStorage.getItem('access_token')) {
      localStorage.removeItem('access_token')
      localStorage.removeItem('refresh_token')
      window.location.href = '/login'
    }
    throw new Error('Unauthorized')
  }
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  if (res.status === 204) return undefined as T
  return res.json()
}

export const authApi = {
  login: (username: string, password: string): Promise<AuthTokens> =>
    request('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  refresh: (username: string, refreshToken: string): Promise<AuthTokens> =>
    request('/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ username, refresh_token: refreshToken }),
    }),
}

export const usersApi = {
  list: (): Promise<User[]> => request('/users'),

  create: (data: Omit<User, 'id'>): Promise<User> =>
    request('/users', { method: 'POST', body: JSON.stringify(data) }),

  update: (id: string, data: Partial<User>): Promise<User> =>
    request(`/users/${id}`, { method: 'PUT', body: JSON.stringify({ id, ...data }) }),

  remove: (id: string): Promise<void> => request(`/users/${id}`, { method: 'DELETE' }),
}

export const usageApi = {
  list: (): Promise<UsageRecord[]> => request('/usage'),
}

export const configApi = {
  getByUser: async (userId: string): Promise<string> => {
    const res = await fetch(`${BASE}/config/${userId}`)
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
    const text = await res.text()
    try {
      const parsed = JSON.parse(text) as { url?: string }
      return parsed.url ?? text
    } catch {
      return text
    }
  },
}
