import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Brush } from 'recharts'
import type { UsageRecord, User, UserUsageSummary } from '@/types'
import { buildUsageData, CHART_COLORS, formatBytes, type UsageGrouping } from './usageData'
import styles from '../pages/UsagePage.module.css'

interface UserUsageChartProps { records: UsageRecord[]; summaries: UserUsageSummary[]; users: User[]; isSuperAdmin: boolean; grouping: UsageGrouping; timeZone?: string; selectedUserIds: string[] | null; onSelectedUserIdsChange: (userIds: string[] | null) => void }

export function UserUsageChart({ records, summaries, users, isSuperAdmin, grouping, timeZone, selectedUserIds, onSelectedUserIdsChange }: UserUsageChartProps) {
  const usageData = buildUsageData(records, grouping, timeZone)
  const selectedIds = selectedUserIds ?? summaries.map((summary) => summary.userId)
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
  const adminUsageByLabel = new Map(adminUsageData.map((point) => [String(point.label), point]))
  const chartData = usageData.map((point) => {
    const adminPoint = adminUsageByLabel.get(String(point.label))
    return {
      ...point,
      ...Object.fromEntries(adminSeries.map(({ key }) => [key, Number(adminPoint?.[key] ?? 0)])),
    }
  })

  return (
    <section className={styles.chartWrap}>
      <h3 className={styles.chartTitle}>{isSuperAdmin ? 'Usage by user and admin' : 'Usage by user'}</h3>
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
      {chartData.length === 0 || (selectedIds.length === 0 && adminSeries.length === 0) ? (
        <p className={styles.empty}>No usage data available</p>
      ) : (
        <ResponsiveContainer width="100%" height={280}>
          <LineChart data={chartData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
            <XAxis dataKey="label" tick={{ fill: '#8080a0', fontSize: 11 }} interval={3} />
            <YAxis tick={{ fill: '#8080a0', fontSize: 12 }} tickFormatter={formatBytes} width={64} />
            <Tooltip
              contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
              formatter={(value, name) => [
                formatBytes(Number(value)),
                summaries.find((summary) => summary.userId === name)?.name
                  ?? adminSeries.find((series) => series.key === name)?.owner
                  ?? name,
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
            {adminSeries.map(({ key }, index) => (
              <Line
                key={key}
                type="monotone"
                dataKey={key}
                name={key}
                stroke={CHART_COLORS[(summaries.length + index) % CHART_COLORS.length]}
                strokeWidth={2}
                strokeDasharray="6 3"
                dot={false}
              />
            ))}
            <Brush dataKey="label" height={24} stroke="#7c7cff" travellerWidth={10} />
          </LineChart>
        </ResponsiveContainer>
      )}
    </section>
  )
}