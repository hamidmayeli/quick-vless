import { useState } from 'react'
import { useUsers } from '@/hooks/useUsers'
import { useUsage } from '@/hooks/useUsage'
import { UsagePerUserChart } from '@/components/UsagePerUserChart'
import { UsageShareChart } from '@/components/UsageShareChart'
import { AggregateUsageChart } from '@/components/AggregateUsageChart'
import { UserUsageChart } from '@/components/UserUsageChart'
import { WeeklyComparisonChart } from '@/components/WeeklyComparisonChart'
import { filterRecordsByPeriod, type UsageGrouping, type UsagePeriod } from '@/components/usageData'
import styles from './UsagePage.module.css'

type PeriodPreset = '24h' | '7d' | '30d' | 'custom'

const PERIOD_PRESETS: { value: Exclude<PeriodPreset, 'custom'>; label: string; milliseconds: number }[] = [
  { value: '24h', label: '24 hours', milliseconds: 24 * 60 * 60 * 1000 },
  { value: '7d', label: '7 days', milliseconds: 7 * 24 * 60 * 60 * 1000 },
  { value: '30d', label: '30 days', milliseconds: 30 * 24 * 60 * 60 * 1000 },
]

function toDateTimeLocal(timestamp: number) {
  const date = new Date(timestamp)
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export function UsagePage() {
  const { users, loading: usersLoading } = useUsers()
  const { summaries, loading: usageLoading, error } = useUsage(users)
  const [selectedUserIds, setSelectedUserIds] = useState<string[] | null>(null)
  const [grouping, setGrouping] = useState<UsageGrouping>('hourly')
  const [periodPreset, setPeriodPreset] = useState<PeriodPreset>('24h')
  const [customPeriod, setCustomPeriod] = useState<UsagePeriod | null>(null)

  if (usersLoading || usageLoading) return <p className={styles.state}>Loading usage data…</p>
  if (error) return <p className={styles.error}>{error}</p>

  const records = summaries.flatMap((summary) => summary.records)
  const anchor = records.reduce((latest, record) => Math.max(latest, new Date(record.date).getTime()), 0)
  const selectedPreset = PERIOD_PRESETS.find((preset) => preset.value === periodPreset)
  const period = periodPreset === 'custom' && customPeriod
    ? customPeriod
    : {
        from: selectedPreset ? toDateTimeLocal(anchor - selectedPreset.milliseconds) : '',
        to: selectedPreset ? toDateTimeLocal(anchor) : '',
      }
  const chartRecords = filterRecordsByPeriod(records, period)

  const selectPreset = (preset: PeriodPreset) => {
    if (preset === 'custom') setCustomPeriod(period)
    setPeriodPreset(preset)
  }

  return (
    <div className={styles.page}>
      <h2 className={styles.title}>Usage Report</h2>
      <UsageShareChart summaries={summaries} />
      <div className={styles.periodFilter}>
        <span className={styles.periodLabel}>Visible period</span>
        <div className={styles.periodPresets}>
          {PERIOD_PRESETS.map((preset) => (
            <button
              key={preset.value}
              type="button"
              className={`${styles.periodButton} ${periodPreset === preset.value ? styles.periodButtonActive : ''}`}
              onClick={() => selectPreset(preset.value)}
            >
              {preset.label}
            </button>
          ))}
          <button
            type="button"
            className={`${styles.periodButton} ${periodPreset === 'custom' ? styles.periodButtonActive : ''}`}
            onClick={() => selectPreset('custom')}
          >
            Custom
          </button>
        </div>
        {periodPreset === 'custom' && (
          <>
            <label className={styles.filterOption}>
              From
              <input
                type="datetime-local"
                value={period.from}
                max={period.to || undefined}
                onChange={(event) => {
                  setCustomPeriod((current) => ({ ...(current ?? period), from: event.target.value }))
                }}
              />
            </label>
            <label className={styles.filterOption}>
              To
              <input
                type="datetime-local"
                value={period.to}
                min={period.from || undefined}
                onChange={(event) => {
                  setCustomPeriod((current) => ({ ...(current ?? period), to: event.target.value }))
                }}
              />
            </label>
            <button type="button" className={styles.clearPeriod} onClick={() => setCustomPeriod({ from: '', to: '' })}>
              Clear
            </button>
          </>
        )}
      </div>
      <AggregateUsageChart records={chartRecords} grouping={grouping} onGroupingChange={setGrouping} />
      <UserUsageChart
        records={chartRecords}
        summaries={summaries}
        grouping={grouping}
        selectedUserIds={selectedUserIds}
        onSelectedUserIdsChange={setSelectedUserIds}
      />
      <UsagePerUserChart summaries={summaries} records={chartRecords} />
      <WeeklyComparisonChart records={records} anchor={anchor} />
    </div>
  )
}