import { useState, useCallback } from 'react'
import { authApi, sessionFromAccessToken } from '@/services/api'
import type { Session } from '@/types'

function storedSession(): Session | null {
  const token = localStorage.getItem('access_token')
  return token ? sessionFromAccessToken(token) : null
}

export function useAuth() {
  const [isAuthenticated, setIsAuthenticated] = useState(
    () => !!localStorage.getItem('access_token'),
  )
  const [session, setSession] = useState<Session | null>(storedSession)

  const login = useCallback(async (username: string, password: string) => {
    const tokens = await authApi.login(username, password)
    localStorage.setItem('access_token', tokens.access_token)
    setSession(sessionFromAccessToken(tokens.access_token))
    setIsAuthenticated(true)
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem('access_token')
    setSession(null)
    setIsAuthenticated(false)
  }, [])

  const isSuperAdmin = session?.role === 'super_admin'

  return { isAuthenticated, session, isSuperAdmin, login, logout }
}
