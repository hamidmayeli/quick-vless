import { useState, useEffect, useCallback } from 'react'
import { usersApi } from '@/services/api'
import type { User } from '@/types'

export function useUsers() {
  const [users, setUsers] = useState<User[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setUsers(await usersApi.list())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load users')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void load() }, [load])

  const create = useCallback(async (data: Omit<User, 'id'>) => {
    await usersApi.create(data)
    await load()
  }, [load])

  const update = useCallback(async (id: string, data: Partial<User>) => {
    await usersApi.update(id, data)
    await load()
  }, [load])

  const remove = useCallback(async (id: string) => {
    await usersApi.remove(id)
    await load()
  }, [load])

  const toggle = useCallback(async (user: User) => {
    await usersApi.update(user.id, { enabled: !user.enabled })
    await load()
  }, [load])

  return { users, loading, error, create, update, remove, toggle, reload: load }
}
