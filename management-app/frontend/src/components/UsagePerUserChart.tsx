import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Rectangle } from 'recharts'
import type { UsageRecord, UserUsageSummary } from '@/types'
import { BYTES_PER_GB } from './usageData'
import styles from '../pages/UsagePage.module.css'

export function UsagePerUserChart({ summaries, records }: { summaries: UserUsageSummary[]; records: UsageRecord[] }) {
  const userTotals = new Map<string, number>()
  records.forEach((record) => userTotals.set(record.user_id, (userTotals.get(record.user_id) ?? 0) + record.total))
  const chartData = summaries.map((summary) => ({
    name: summary.name,
    used: Math.round(((userTotals.get(summary.userId) ?? 0) / BYTES_PER_GB) * 100) / 100,
    pct: summary.quotaGb ? Math.min(100, Math.round((((userTotals.get(summary.userId) ?? 0) / BYTES_PER_GB) / summary.quotaGb) * 100)) : null
  }))
  
  return <div className={styles.chartWrap}>
    <h3 className={styles.chartTitle}>Data used per user (GB)</h3>
    <ResponsiveContainer width="100%" height={280}>
      <BarChart data={chartData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
        <XAxis dataKey="name" tick={{ fill: '#8080a0', fontSize: 12 }} />
        <YAxis unit=" GB" tick={{ fill: '#8080a0', fontSize: 12 }} />
        <Tooltip
          contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
          labelStyle={{ display: 'none' }}
          itemStyle={{ color: '#e8e8f0' }}
          formatter={(value, _, item) => [`${item.payload.name}: ${Number(value ?? 0).toFixed(2)} GB`]}
          />
        <Bar
          dataKey="used"
          radius={[4, 4, 0, 0]}
          shape={(props) => {
            // props.payload contains your original data entry (including your `pct` value)
            const fill = props.payload.pct != null && props.payload.pct >= 90 ? '#ff6b6b' : '#7c7cff';
            
            // Return Recharts' standard Rectangle, but with our conditionally overridden fill
            return <Rectangle {...props} fill={fill} />;
          }}
          >
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  </div>
}