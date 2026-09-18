import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Brush } from 'recharts'
import type { UsageRecord } from '@/types'
import { buildUsageData, formatBytes, type UsageGrouping } from './usageData'
import styles from '../pages/UsagePage.module.css'

interface AggregateUsageChartProps { records: UsageRecord[]; grouping: UsageGrouping; onGroupingChange: (grouping: UsageGrouping) => void }

export function AggregateUsageChart({ records, grouping, onGroupingChange }: AggregateUsageChartProps) {
  const usageData = buildUsageData(records, grouping)
  return (
  <section className={styles.chartWrap}>
    <h3 className={styles.chartTitle}>All users usage</h3>

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
      <LineChart data={usageData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
        <XAxis dataKey="label" tick={{ fill: '#8080a0', fontSize: 11 }} />
        <YAxis tick={{ fill: '#8080a0', fontSize: 12 }} tickFormatter={formatBytes} width={64} />
        <Tooltip
          contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
          formatter={(value) => [formatBytes(Number(value)), 'Used']} />
        <Line type="monotone" dataKey="all" name="All users" stroke="#7c7cff" strokeWidth={2} dot={false} />
        <Brush dataKey="label" height={24} stroke="#7c7cff" travellerWidth={10} />
      </LineChart>
    </ResponsiveContainer>}
  </section>
  )
}