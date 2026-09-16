import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend } from 'recharts'
import type { UsageRecord } from '@/types'
import { buildWeeklyData, CHART_COLORS, formatBytes } from './usageData'
import styles from '../pages/UsagePage.module.css'

export function WeeklyComparisonChart({ records, anchor }: { records: UsageRecord[]; anchor: number }) {
  const weeklyData = anchor ? buildWeeklyData(records, anchor) : []
  return (
    <div className={styles.chartWrap}>
      <h3 className={styles.chartTitle}>Weekly comparison</h3>

      {weeklyData.length === 0 ? <p className={styles.empty}>No usage data available</p> :
      <ResponsiveContainer width="100%" height={300}>
        <BarChart data={weeklyData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
          <XAxis dataKey="label" tick={{ fill: '#8080a0', fontSize: 12 }} />
          <YAxis tick={{ fill: '#8080a0', fontSize: 12 }} tickFormatter={formatBytes} width={64} />
          <Tooltip
            contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
            formatter={(value, name) => [formatBytes(Number(value)), name]} />
          <Legend />
          {['Current week', 'Last week', '2 weeks ago', '3 weeks ago'].map((name, index) =>
            <Bar key={name} dataKey={`week${index}`} name={name} fill={CHART_COLORS[index]} radius={[3, 3, 0, 0]} />)}
        </BarChart>
      </ResponsiveContainer>}
    </div>
  )
}