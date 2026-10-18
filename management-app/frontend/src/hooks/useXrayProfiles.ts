import { useState, useEffect, useCallback } from 'react'
import { xrayApi } from '@/services/api'
import type { XrayProfilesState } from '@/types'
import { useRefreshOnReconnect } from './useRefreshOnReconnect'

export function useXrayProfiles() {
  const [state, setState] = useState<XrayProfilesState | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setState(await xrayApi.profiles())
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load profiles')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void load() }, [load])
  useRefreshOnReconnect(load)

  // The page shows the server's state after every attempt, including a rollback.
  const activate = useCallback(async (name: string) => {
    try {
      await xrayApi.activate(name)
    } finally {
      await load()
    }
  }, [load])

  const reapply = useCallback(async () => {
    try {
      await xrayApi.reapply()
    } finally {
      await load()
    }
  }, [load])

  return { state, loading, error, activate, reapply, reload: load }
}
