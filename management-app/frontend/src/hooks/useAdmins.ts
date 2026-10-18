import { useState, useEffect, useCallback } from 'react'
import { adminsApi } from '@/services/api'
import type { Admin } from '@/types'
import { useRefreshOnReconnect } from './useRefreshOnReconnect'

export function useAdmins() {
  const [admins, setAdmins] = useState<Admin[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const data = await adminsApi.list()
      setAdmins(data)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load admins')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void load() }, [load])
  useRefreshOnReconnect(load)

  const create = useCallback(async (username: string) => {
    await adminsApi.create(username)
    await load()
  }, [load])

  const remove = useCallback(async (username: string) => {
    await adminsApi.remove(username)
    await load()
  }, [load])

  return { admins, loading, error, create, remove, reload: load }
}
