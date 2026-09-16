import { useState } from 'react'
import { useUsers } from '@/hooks/useUsers'
import { useUsage } from '@/hooks/useUsage'
import { UsagePerUserChart } from '@/components/UsagePerUserChart'
import { UsageShareChart } from '@/components/UsageShareChart'
import { AggregateUsageChart } from '@/components/AggregateUsageChart'
import { UserUsageChart } from '@/components/UserUsageChart'
import { WeeklyComparisonChart } from '@/components/WeeklyComparisonChart'
import { type UsageGrouping } from '@/components/usageData'
import styles from './UsagePage.module.css'

export function UsagePage() {
  const { users, loading: usersLoading } = useUsers()
  const { summaries, loading: usageLoading, error } = useUsage(users)
  const [selectedUserIds, setSelectedUserIds] = useState<string[] | null>(null)
  const [grouping, setGrouping] = useState<UsageGrouping>('hourly')

  if (usersLoading || usageLoading) return <p className={styles.state}>Loading usage data…</p>
  if (error) return <p className={styles.error}>{error}</p>

  const records = summaries.flatMap((summary) => summary.records)
  const anchor = records.reduce((latest, record) => Math.max(latest, new Date(record.date).getTime()), 0)

  return (
    <div className={styles.page}>
      <h2 className={styles.title}>Usage Report</h2>
      <UsageShareChart summaries={summaries} />
      <AggregateUsageChart records={records} grouping={grouping} onGroupingChange={setGrouping} />
      <UserUsageChart
        records={records}
        summaries={summaries}
        grouping={grouping}
        selectedUserIds={selectedUserIds}
        onSelectedUserIdsChange={setSelectedUserIds}
      />
      <WeeklyComparisonChart records={records} anchor={anchor} />
      <UsagePerUserChart summaries={summaries} />
    </div>
  )
}