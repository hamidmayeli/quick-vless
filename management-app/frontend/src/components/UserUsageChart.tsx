import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts'
import type { UsageRecord, UserUsageSummary } from '@/types'
import { buildUsageData, CHART_COLORS, formatBytes, type UsageGrouping } from './usageData'
import styles from '../pages/UsagePage.module.css'

interface UserUsageChartProps { records: UsageRecord[]; summaries: UserUsageSummary[]; grouping: UsageGrouping; selectedUserIds: string[] | null; onSelectedUserIdsChange: (userIds: string[] | null) => void }

export function UserUsageChart({ records, summaries, grouping, selectedUserIds, onSelectedUserIdsChange }: UserUsageChartProps) {
  const usageData = buildUsageData(records, grouping)
  const selectedIds = selectedUserIds ?? summaries.map((summary) => summary.userId)

  return (
    <section className={styles.chartWrap}>
      <h3 className={styles.chartTitle}>Usage by user</h3>
      <div className={styles.userFilters}>
        <label className={styles.filterOption}>
          <input
            type="checkbox"
            checked={selectedUserIds === null}
            onChange={(event) => onSelectedUserIdsChange(event.target.checked ? null : [])}
          />
          All users
        </label>
        {summaries.map((summary, index) => {
          const color = CHART_COLORS[index % CHART_COLORS.length]
          return (
            <label key={summary.userId} className={styles.filterOption} style={{ color }}>
              <input
                type="checkbox"
                checked={selectedUserIds === null || selectedUserIds.includes(summary.userId)}
                onChange={(event) => {
                  const current = selectedUserIds ?? summaries.map((item) => item.userId)
                  const next = event.target.checked
                    ? [...current, summary.userId]
                    : current.filter((id) => id !== summary.userId)
                  onSelectedUserIdsChange(next.length === summaries.length ? null : next)
                }}
              />
              {summary.name}
            </label>
          )
        })}
      </div>
      {usageData.length === 0 || selectedIds.length === 0 ? (
        <p className={styles.empty}>No usage data available</p>
      ) : (
        <ResponsiveContainer width="100%" height={280}>
          <LineChart data={usageData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
            <XAxis dataKey="label" tick={{ fill: '#8080a0', fontSize: 11 }} interval={3} />
            <YAxis tick={{ fill: '#8080a0', fontSize: 12 }} tickFormatter={formatBytes} width={64} />
            <Tooltip
              contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
              formatter={(value, name) => [
                formatBytes(Number(value)),
                summaries.find((summary) => summary.userId === name)?.name ?? name,
              ]}
            />
            {summaries
              .map((summary, index) => ({ summary, color: CHART_COLORS[index % CHART_COLORS.length] }))
              .filter(({ summary }) => selectedIds.includes(summary.userId))
              .map(({ summary, color }) => (
                <Line
                  key={summary.userId}
                  type="monotone"
                  dataKey={summary.userId}
                  name={summary.userId}
                  stroke={color}
                  strokeWidth={2}
                  dot={false}
                />
              ))}
          </LineChart>
        </ResponsiveContainer>
      )}
    </section>
  )
}