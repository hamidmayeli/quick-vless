import { PieChart, Pie, Tooltip, ResponsiveContainer, Cell, Legend } from 'recharts'
import type { UserUsageSummary } from '@/types'
import { CHART_COLORS } from './usageData'
import styles from '../pages/UsagePage.module.css'
import { UsageSummaryCards } from './UsageSummaryCards'

export function UsageShareChart({ summaries }: { summaries: UserUsageSummary[] }) {
  const pieData = summaries.filter((summary) => summary.totalGb > 0).map((summary) => ({ name: summary.name, value: summary.totalGb }))

  return (
    <section className={styles.chartWrap}>
      <h3 className={styles.chartTitle}>Usage share by user</h3>
      <div className={styles.shareLayout}>
        <div className={styles.pieWrap}>
          {pieData.length === 0 ? (
            <p className={styles.empty}>No usage data available</p>
          ) : (
            <ResponsiveContainer width="100%" height={260}>
              <PieChart>
                <Pie
                  data={pieData}
                  dataKey="value"
                  nameKey="name"
                  innerRadius={55}
                  outerRadius={85}
                  paddingAngle={3}
                >
                  {pieData.map((entry, index) => (
                    <Cell key={entry.name} fill={CHART_COLORS[index % CHART_COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
                  formatter={(value, name) => [
                    `${Number(value).toFixed(2)} GB`,
                    summaries.find((summary) => summary.userId === name)?.name ?? name,
                  ]}
                />
                <Legend />
              </PieChart>
            </ResponsiveContainer>
          )}
        </div>
        <div className={styles.cardsWrap}>
          <UsageSummaryCards summaries={summaries} />
        </div>
      </div>
    </section>
  )
}