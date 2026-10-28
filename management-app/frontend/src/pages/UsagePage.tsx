import { useState, useMemo } from 'react'
import { useUsers } from '@/hooks/useUsers'
import { useUsage } from '@/hooks/useUsage'
import { useAuth } from '@/hooks/useAuth'
import { UsagePerUserChart } from '@/components/UsagePerUserChart'
import { UsageShareChart } from '@/components/UsageShareChart'
import { AggregateUsageChart } from '@/components/AggregateUsageChart'
import { UserUsageChart } from '@/components/UserUsageChart'
import { WeeklyComparisonChart } from '@/components/WeeklyComparisonChart'
import { formatDateTimeLocal, getTimeZone, useTimeZone } from '@/settings/timezone'
import { filterRecordsByPeriod, type UsageGrouping, type UsagePeriod } from '@/components/usageData'
import styles from './UsagePage.module.css'

type PeriodPreset = '24h' | '7d' | '30d' | 'custom'

const PERIOD_PRESETS: { value: Exclude<PeriodPreset, 'custom'>; label: string; milliseconds: number }[] = [
  { value: '24h', label: '24 hours', milliseconds: 24 * 60 * 60 * 1000 },
  { value: '7d', label: '7 days', milliseconds: 7 * 24 * 60 * 60 * 1000 },
  { value: '30d', label: '30 days', milliseconds: 30 * 24 * 60 * 60 * 1000 },
]

export function UsagePage() {
  const { users, loading: usersLoading } = useUsers()
  const { summaries, loading: usageLoading, error } = useUsage(users)
  const { isSuperAdmin } = useAuth()
  const [selectedUserIds, setSelectedUserIds] = useState<string[] | null>(null)
  const [grouping, setGrouping] = useState<UsageGrouping>('hourly')
  const [periodPreset, setPeriodPreset] = useState<PeriodPreset>('24h')
  const [customPeriod, setCustomPeriod] = useState<UsagePeriod | null>(null)
  const [timeZoneSetting] = useTimeZone()
  const timeZone = useMemo(() => getTimeZone(timeZoneSetting), [timeZoneSetting])

  const records = useMemo(() => summaries.flatMap((summary) => summary.records), [summaries])
  const anchor = useMemo(() => records.reduce((latest, record) => Math.max(latest, Date.parse(record.date)), 0), [records])

  const selectedPreset = PERIOD_PRESETS.find((preset) => preset.value === periodPreset)
  const period = useMemo<UsagePeriod>(() => {
    if (periodPreset === 'custom' && customPeriod) {
      return customPeriod
    }
    return {
      from: selectedPreset && anchor ? formatDateTimeLocal(anchor - selectedPreset.milliseconds, timeZone) : '',
      to: selectedPreset && anchor ? formatDateTimeLocal(anchor, timeZone) : '',
    }
  }, [periodPreset, customPeriod, selectedPreset, anchor, timeZone])

  const chartRecords = useMemo(() => filterRecordsByPeriod(records, period, timeZone), [records, period, timeZone])

  if (usersLoading || usageLoading) return <p className={styles.state}>Loading usage data…</p>
  if (error) return <p className={styles.error}>{error}</p>

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
      <AggregateUsageChart records={chartRecords} users={users} isSuperAdmin={isSuperAdmin} grouping={grouping} timeZone={timeZone} onGroupingChange={setGrouping} />
      <UserUsageChart
        records={chartRecords}
        summaries={summaries}
        users={users}
        isSuperAdmin={isSuperAdmin}
        grouping={grouping}
        timeZone={timeZone}
        selectedUserIds={selectedUserIds}
        onSelectedUserIdsChange={setSelectedUserIds}
      />
      <UsagePerUserChart summaries={summaries} records={chartRecords} />
      <WeeklyComparisonChart records={records} anchor={anchor} timeZone={timeZone} />
    </div>
  )
}