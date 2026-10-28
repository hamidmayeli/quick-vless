import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Brush } from 'recharts'
import type { UsageRecord, User } from '@/types'
import { buildUsageData, BYTES_PER_GB, CHART_COLORS, formatBytes, type UsageGrouping } from './usageData'
import styles from '../pages/UsagePage.module.css'

interface AggregateUsageChartProps { records: UsageRecord[]; users: User[]; isSuperAdmin: boolean; grouping: UsageGrouping; timeZone?: string; onGroupingChange: (grouping: UsageGrouping) => void }

export function AggregateUsageChart({ records, users, isSuperAdmin, grouping, timeZone, onGroupingChange }: AggregateUsageChartProps) {
  const usageData = buildUsageData(records, grouping, timeZone)
  const ownerByUserId = new Map(users.map((user) => [user.id, user.created_by ?? 'Unknown admin']))
  const adminSeries = isSuperAdmin
    ? [...new Set(ownerByUserId.values())].sort((a, b) => a.localeCompare(b)).map((owner) => ({
        owner,
        key: `admin:${owner}`,
      }))
    : []
  const adminUsageData = adminSeries.length > 0
    ? buildUsageData(records.flatMap((record) => {
        const owner = ownerByUserId.get(record.user_id)
        return owner ? [{ ...record, user_id: `admin:${owner}` }] : []
      }), grouping, timeZone)
    : []
  const adminUsageByTimestamp = new Map(adminUsageData.map((point) => [String(point.timestamp), point]))
  const chartData = usageData.map((point) => {
    const adminPoint = adminUsageByTimestamp.get(String(point.timestamp))
    return {
      ...point,
      ...Object.fromEntries(adminSeries.map(({ key }) => [key, Number(adminPoint?.[key] ?? 0)])),
    }
  })

  return (
  <section className={styles.chartWrap}>
    <h3 className={styles.chartTitle}>All users usage {(records.reduce((acc, rec) => acc + rec.total, 0) / BYTES_PER_GB).toFixed(2)} GB</h3>

    <div className={styles.userFilters}>
      {(['none', 'hourly', 'daily'] as const).map((option) => (
        <label key={option} className={styles.filterOption}>
          <input
            type="radio"
            name="usage-grouping"
            value={option}
            checked={grouping === option}
            onChange={() => onGroupingChange(option)}
            />
          {option === 'none' ? 'No grouping' : option[0].toUpperCase() + option.slice(1)}
        </label>))}
    </div>
    {usageData.length === 0 ? <p className={styles.empty}>No usage data available</p> :
    <ResponsiveContainer width="100%" height={280}>
      <LineChart data={chartData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
        <XAxis dataKey="label" tick={{ fill: 'var(--text-subtle)', fontSize: 11 }} />
        <YAxis tick={{ fill: 'var(--text-subtle)', fontSize: 12 }} tickFormatter={formatBytes} width={64} />
        <Tooltip
          contentStyle={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8, color: 'var(--text)' }}
          formatter={(value, name) => [
            formatBytes(Number(value)),
            adminSeries.find((series) => series.key === name)?.owner ?? 'All users',
          ]} />
        <Line type="monotone" dataKey="all" name="All users" stroke="var(--accent)" strokeWidth={2} dot={false} />
        {adminSeries.map(({ key }, index) => (
          <Line
            key={key}
            type="monotone"
            dataKey={key}
            name={key}
            stroke={CHART_COLORS[index % CHART_COLORS.length]}
            strokeWidth={2}
            strokeDasharray="6 3"
            dot={false}
          />
        ))}
        <Brush dataKey="label" height={24} stroke="var(--accent)" travellerWidth={10} />
      </LineChart>
    </ResponsiveContainer>}
  </section>
  )
}