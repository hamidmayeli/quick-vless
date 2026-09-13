import { useState } from 'react'
import { useUsers } from '@/hooks/useUsers'
import { useUsage } from '@/hooks/useUsage'
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Cell,
  PieChart, Pie, Legend, LineChart, Line
} from 'recharts'
import type { UsageRecord } from '@/types'
import styles from './UsagePage.module.css'

const CHART_COLORS = ['#7c7cff', '#43d9ad', '#ffb86b', '#ff6b8a', '#65a8ff', '#c792ea']
const BYTES_PER_GB = 1_073_741_824

function toGb(bytes: number) {
  return bytes / BYTES_PER_GB
}

function formatBytes(value: number) {
  if (value < BYTES_PER_GB) return `${(value / (1024 * 1024)).toFixed(0)} MB`
  return `${toGb(value).toFixed(2)} GB`
}

function startOfUtcDay(timestamp: number) {
  const date = new Date(timestamp)
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate())
}

function startOfUtcWeek(timestamp: number) {
  const day = new Date(timestamp).getUTCDay()
  return startOfUtcDay(timestamp) - ((day + 6) % 7) * 24 * 60 * 60 * 1000
}

type UsageGrouping = 'none' | 'hourly' | 'daily'

function buildUsageData(records: UsageRecord[], grouping: UsageGrouping) {
  const sortedRecords = [...records].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
  const points = new Map<number, Record<string, string | number>>()

  sortedRecords.forEach((record) => {
    const timestamp = new Date(record.date).getTime()
    const bucket = grouping === 'hourly'
      ? Math.floor(timestamp / 3_600_000) * 3_600_000
      : grouping === 'daily'
        ? startOfUtcDay(timestamp)
        : timestamp
    const point = points.get(bucket) ?? {
      timestamp: bucket,
      label: grouping === 'daily'
        ? new Date(bucket).toISOString().slice(0, 10)
        : grouping === 'hourly'
          ? new Date(bucket).toISOString().slice(0, 16).replace('T', ' ')
          : new Date(timestamp).toISOString().slice(0, 16).replace('T', ' '),
      all: 0,
    }
    point.all = Number(point.all) + record.total
    point[record.user_id] = Number(point[record.user_id] ?? 0) + record.total
    points.set(bucket, point)
  })

  return [...points.values()]
}

function buildWeeklyData(records: UsageRecord[], anchor: number) {
  const currentWeek = startOfUtcWeek(anchor)
  return Array.from({ length: 7 }, (_, dayIndex) => {
    const point: Record<string, string | number> = {
      label: new Intl.DateTimeFormat(undefined, { weekday: 'short', timeZone: 'UTC' }).format(
        new Date(currentWeek + dayIndex * 86_400_000),
      ),
    }
    for (let weekIndex = 0; weekIndex < 4; weekIndex += 1) {
      const dayStart = currentWeek - weekIndex * 7 * 86_400_000 + dayIndex * 86_400_000
      point[`week${weekIndex}`] = records
        .filter((record) => startOfUtcDay(new Date(record.date).getTime()) === dayStart)
        .reduce((sum, record) => sum + record.total, 0)
    }
    return point
  })
}

export function UsagePage() {
  const { users, loading: usersLoading } = useUsers()
  const { summaries, loading: usageLoading, error } = useUsage(users)
  const [selectedUserIds, setSelectedUserIds] = useState<string[] | null>(null)
  const [grouping, setGrouping] = useState<UsageGrouping>('hourly')

  if (usersLoading || usageLoading) return <p className={styles.state}>Loading usage data…</p>
  if (error) return <p className={styles.error}>{error}</p>

  const chartData = summaries.map((s) => ({
    name: s.name,
    used: s.totalGb,
    quota: s.quotaGb,
    pct: s.quotaGb ? Math.min(100, Math.round((s.totalGb / s.quotaGb) * 100)) : null,
  }))
  const records = summaries.flatMap((summary) => summary.records)
  const anchor = records.reduce((latest, record) => Math.max(latest, new Date(record.date).getTime()), 0)
  const pieData = summaries
    .filter((summary) => summary.totalGb > 0)
    .map((summary) => ({ name: summary.name, value: summary.totalGb }))
  const selectedUserIdsForChart = selectedUserIds ?? summaries.map((summary) => summary.userId)
  const usageData = buildUsageData(records, grouping)
  const weeklyData = anchor ? buildWeeklyData(records, anchor) : []

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

      <div className={styles.reportGrid}>
        <section className={styles.chartWrap}>
          <h3 className={styles.chartTitle}>Usage share by user</h3>
          {pieData.length === 0 ? <p className={styles.empty}>No usage data available</p> : (
            <ResponsiveContainer width="100%" height={280}>
              <PieChart>
                <Pie data={pieData} dataKey="value" nameKey="name" innerRadius={62} outerRadius={96} paddingAngle={3}>
                  {pieData.map((entry, index) => <Cell key={entry.name} fill={CHART_COLORS[index % CHART_COLORS.length]} />)}
                </Pie>
                <Tooltip
                  contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
                  formatter={(value) => [`${Number(value).toFixed(2)} GB`, 'Used']}
                />
                <Legend />
              </PieChart>
            </ResponsiveContainer>
          )}
        </section>

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
                  onChange={() => setGrouping(option)}
                />
                {option === 'none' ? 'No grouping' : option[0].toUpperCase() + option.slice(1)}
              </label>
            ))}
          </div>
          {usageData.length === 0 ? <p className={styles.empty}>No usage data available</p> : (
            <ResponsiveContainer width="100%" height={280}>
              <LineChart data={usageData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
                <XAxis dataKey="label" tick={{ fill: '#8080a0', fontSize: 11 }} />
                <YAxis tick={{ fill: '#8080a0', fontSize: 12 }} tickFormatter={(value) => formatBytes(value)} width={64} />
                <Tooltip
                  contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
                  formatter={(value) => [formatBytes(Number(value)), 'Used']}
                />
                <Line type="monotone" dataKey="all" name="All users" stroke="#7c7cff" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          )}
        </section>

        <section className={styles.chartWrap}>
          <h3 className={styles.chartTitle}>Usage by user</h3>
          <div className={styles.userFilters}>
            <label className={styles.filterOption}>
              <input
                type="checkbox"
                checked={selectedUserIds === null}
                onChange={(event) => setSelectedUserIds(event.target.checked ? null : [])}
              />
              All users
            </label>
            {summaries.map((summary) => (
              <label key={summary.userId} className={styles.filterOption}>
                <input
                  type="checkbox"
                  checked={selectedUserIds === null || selectedUserIds.includes(summary.userId)}
                  onChange={(event) => {
                    const current = selectedUserIds ?? summaries.map((item) => item.userId)
                    const next = event.target.checked
                      ? [...current, summary.userId]
                      : current.filter((id) => id !== summary.userId)
                    setSelectedUserIds(next.length === summaries.length ? null : next)
                  }}
                />
                {summary.name}
              </label>
            ))}
          </div>
          {usageData.length === 0 || selectedUserIdsForChart.length === 0 ? <p className={styles.empty}>No usage data available</p> : (
            <ResponsiveContainer width="100%" height={280}>
              <LineChart data={usageData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
                <XAxis dataKey="label" tick={{ fill: '#8080a0', fontSize: 11 }} interval={3} />
                <YAxis tick={{ fill: '#8080a0', fontSize: 12 }} tickFormatter={(value) => formatBytes(value)} width={64} />
                <Tooltip
                  contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
                  formatter={(value, name) => [formatBytes(Number(value)), summaries.find((summary) => summary.userId === name)?.name ?? name]}
                />
                {summaries
                  .filter((summary) => selectedUserIdsForChart.includes(summary.userId))
                  .map((summary, index) => (
                    <Line
                      key={summary.userId}
                      type="monotone"
                      dataKey={summary.userId}
                      name={summary.userId}
                      stroke={CHART_COLORS[index % CHART_COLORS.length]}
                      strokeWidth={2}
                      dot={false}
                    />
                  ))}
              </LineChart>
            </ResponsiveContainer>
          )}
        </section>
      </div>

      <div className={styles.chartWrap}>
        <h3 className={styles.chartTitle}>Weekly comparison</h3>
        {weeklyData.length === 0 ? <p className={styles.empty}>No usage data available</p> : (
          <ResponsiveContainer width="100%" height={300}>
            <BarChart data={weeklyData} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#2a2a4a" />
              <XAxis dataKey="label" tick={{ fill: '#8080a0', fontSize: 12 }} />
              <YAxis tick={{ fill: '#8080a0', fontSize: 12 }} tickFormatter={(value) => formatBytes(value)} width={64} />
              <Tooltip
                contentStyle={{ background: '#1a1a2e', border: '1px solid #2a2a4a', borderRadius: 8 }}
                formatter={(value) => [formatBytes(Number(value)), 'Used']}
              />
              <Legend />
              {['Current week', 'Last week', '2 weeks ago', '3 weeks ago'].map((name, index) => (
                <Bar key={name} dataKey={`week${index}`} name={name} fill={CHART_COLORS[index]} radius={[3, 3, 0, 0]} />
              ))}
            </BarChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  )
}
