import type { Admin, AdminRole, AuthTokens, Session, User, UsageRecord, XrayProfilesState } from '@/types'

const BASE = '/api/v1'

/** Error body some endpoints return: `{ error }` or `{ errors: [...] }`. */
export interface ApiErrorBody {
  error?: string
  errors?: string[]
}

export class ApiError extends Error {
  readonly status: number
  readonly body: ApiErrorBody | null

  constructor(status: number, statusText: string, body: ApiErrorBody | null = null) {
    super(`${status} ${statusText}`)
    this.status = status
    this.body = body
  }
}

function authHeaders(): HeadersInit {
  const token = localStorage.getItem('access_token')
  return token ? { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } : { 'Content-Type': 'application/json' }
}

function accessTokenExpiresSoon(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))) as { exp?: number }
    return typeof payload.exp !== 'number' || payload.exp * 1000 - Date.now() < 30_000
  } catch {
    return true
  }
}

// The backend emits full claim-type URIs (e.g. ".../claims/name"); the mock API uses short names.
export function sessionFromAccessToken(token: string): Session | null {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))) as Record<string, unknown>
    const claim = (name: string) => Object.entries(payload).find(([key]) => key === name || key.endsWith(`/${name}`))?.[1]
    const username = claim('name')
    if (typeof username !== 'string') return null
    const role: AdminRole = claim('role') === 'super_admin' ? 'super_admin' : 'admin'
    return { username, role }
  } catch {
    return null
  }
}

let refreshPromise: Promise<void> | null = null

async function refreshAccessToken(): Promise<void> {
  const accessToken = localStorage.getItem('access_token')
  const username = accessToken ? sessionFromAccessToken(accessToken)?.username : null
  if (!username) throw new Error('Unable to refresh session')

  const res = await fetch(`${BASE}/auth/refresh`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username }),
  })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  const tokens = await res.json() as AuthTokens
  localStorage.setItem('access_token', tokens.access_token)
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = localStorage.getItem('access_token')
  if (token && accessTokenExpiresSoon(token) && !path.startsWith('/auth/')) {
    refreshPromise ??= refreshAccessToken().finally(() => { refreshPromise = null })
    try {
      await refreshPromise
    } catch {
      localStorage.removeItem('access_token')
      window.location.href = '/login'
      throw new Error('Unauthorized')
    }
  }
  const res = await fetch(`${BASE}${path}`, { ...init, headers: { ...authHeaders(), ...init?.headers } })
  if (res.status === 401) {
    if (localStorage.getItem('access_token')) {
      localStorage.removeItem('access_token')
      window.location.href = '/login'
    }
    throw new Error('Unauthorized')
  }
  if (!res.ok) {
    const body = await res.json().catch(() => null) as ApiErrorBody | null
    throw new ApiError(res.status, res.statusText, body)
  }
  if (res.status === 204) return undefined as T
  return res.json()
}

export const authApi = {
  login: (username: string, password: string): Promise<AuthTokens> =>
    request('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  refresh: (username: string): Promise<AuthTokens> =>
    request('/auth/refresh', {
      method: 'POST',
      credentials: 'same-origin',
      body: JSON.stringify({ username }),
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

export const adminsApi = {
  list: (): Promise<Admin[]> => request('/admins'),

  create: (username: string): Promise<Admin> =>
    request('/admins', { method: 'POST', body: JSON.stringify({ username }) }),

  remove: (username: string): Promise<void> =>
    request(`/admins/${encodeURIComponent(username)}`, { method: 'DELETE' }),
}

export const xrayApi = {
  profiles: (): Promise<XrayProfilesState> => request('/xray/profiles'),

  activate: (name: string): Promise<{ active: string }> =>
    request('/xray/active-profile', { method: 'PUT', body: JSON.stringify({ name }) }),

  reapply: (): Promise<{ active: string }> =>
    request('/xray/active-profile/reapply', { method: 'POST' }),
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
