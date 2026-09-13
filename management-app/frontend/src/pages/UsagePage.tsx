import { useUsers } from '@/hooks/useUsers'
import { useUsage } from '@/hooks/useUsage'
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Cell
} from 'recharts'
import styles from './UsagePage.module.css'

export function UsagePage() {
  const { users, loading: usersLoading } = useUsers()
  const { summaries, loading: usageLoading, error } = useUsage(users)

  if (usersLoading || usageLoading) return <p className={styles.state}>Loading usage data…</p>
  if (error) return <p className={styles.error}>{error}</p>

  const chartData = summaries.map((s) => ({
    name: s.name,
    used: s.totalGb,
    quota: s.quotaGb,
    pct: s.quotaGb ? Math.min(100, Math.round((s.totalGb / s.quotaGb) * 100)) : null,
  }))

  return (
    <div className={styles.page}>
      <h2 className={styles.title}>Usage Report</h2>

      <div className={styles.cards}>
        {summaries.map((s) => {
          const pct = s.quotaGb ? Math.min(100, Math.round((s.totalGb / s.quotaGb) * 100)) : null
          return (
            <div key={s.userId} className={styles.card}>
              <span className={styles.userName}>{s.name}</span>
              <span className={styles.usedLabel}>{s.totalGb.toFixed(2)} GB used</span>
              {s.quotaGb != null && (
                <>
                  <div className={styles.progressTrack}>
                    <div
                      className={styles.progressBar}
                      style={{ width: `${pct}%`, background: pct! >= 90 ? '#ff6b6b' : '#7c7cff' }}
                    />
                  </div>
                  <span className={styles.quota}>{pct}% of {s.quotaGb} GB</span>
                </>
              )}
              {s.quotaGb == null && <span className={styles.quota}>Unlimited</span>}
            </div>
          )
        })}
      </div>

      <div className={styles.chartWrap}>
        <h3 className={styles.chartTitle}>Data used per user (GB)</h3>
        <ResponsiveContainer width="100%" height={280}>
          <BarChart data={chartData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
            <XAxis dataKey="name" tick={{ fill: '#8080a0', fontSize: 12 }} />
            <YAxis unit=" GB" tick={{ fill: '#8080a0', fontSize: 12 }} />
            <Tooltip
              contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
              labelStyle={{ color: '#e8e8f0' }}
              formatter={(val) => [`${Number(val ?? 0).toFixed(2)} GB`, 'Used']}
            />
            <Bar dataKey="used" radius={[4, 4, 0, 0]}>
              {chartData.map((entry, i) => (
                <Cell key={i} fill={entry.pct != null && entry.pct >= 90 ? '#ff6b6b' : '#7c7cff'} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}
