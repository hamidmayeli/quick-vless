import { useState, useEffect, useCallback } from 'react'
import { usageApi } from '@/services/api'
import type { UsageRecord, User, UserUsageSummary } from '@/types'
import { useRefreshOnReconnect } from './useRefreshOnReconnect'

const BYTES_PER_GB = 1_073_741_824

function bytesToGb(bytes: number): number {
  return Math.round((bytes / BYTES_PER_GB) * 100) / 100
}

export function useUsage(users: User[]) {
  const [records, setRecords] = useState<UsageRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setRecords(await usageApi.list())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load usage')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])
  useRefreshOnReconnect(load)

  const summaries: UserUsageSummary[] = users.map((user) => {
    const userRecords = records.filter((r) => r.user_id === user.id)
    const totalBytes = userRecords.reduce((sum, r) => sum + r.total, 0)
    return {
      userId: user.id,
      name: user.name,
      totalGb: bytesToGb(totalBytes),
      quotaGb: user.quota == null ? null : bytesToGb(user.quota),
      totalBytes,
      records: userRecords,
    }
  })

  return { summaries, loading, error }
}
