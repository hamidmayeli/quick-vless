import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Rectangle } from 'recharts'
import type { UserUsageSummary } from '@/types'
import styles from '../pages/UsagePage.module.css'

export function UsagePerUserChart({ summaries }: { summaries: UserUsageSummary[] }) {
  const chartData = summaries.map((summary) => ({
    name: summary.name,
    used: summary.totalGb,
    pct: summary.quotaGb ? Math.min(100, Math.round((summary.totalGb / summary.quotaGb) * 100)) : null
  }))
  
  return <div className={styles.chartWrap}>
    <h3 className={styles.chartTitle}>Data used per user (GB)</h3>
    <ResponsiveContainer width="100%" height={280}>
      <BarChart data={chartData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
        <XAxis dataKey="name" tick={{ fill: '#8080a0', fontSize: 12 }} />
        <YAxis unit=" GB" tick={{ fill: '#8080a0', fontSize: 12 }} />
        <Tooltip contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }} labelStyle={{ color: '#e8e8f0' }} formatter={(value) => [`${Number(value ?? 0).toFixed(2)} GB`, 'Used']} />
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