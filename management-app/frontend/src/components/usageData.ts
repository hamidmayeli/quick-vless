import type { UsageRecord } from '@/types'

export const CHART_COLORS = [
  '#7c7cff', '#43d9ad', '#ffb86b', '#ff6b8a', '#65a8ff', '#c792ea',
  '#50fa7b', '#f1fa8c', '#ff79c6', '#8be9fd', '#fd9644', '#00d2d3',
  '#a3e635', '#fab1a0', '#ff5252', '#f368e0',
]
export const BYTES_PER_GB = 1_073_741_824
export type UsageGrouping = 'none' | 'hourly' | 'daily'

export function formatBytes(value: number) {
  if (value < BYTES_PER_GB) return `${(value / (1024 * 1024)).toFixed(0)} MB`
  return `${(value / BYTES_PER_GB).toFixed(2)} GB`
}

function startOfUtcDay(timestamp: number) {
  const date = new Date(timestamp)
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate())
}

function startOfUtcWeek(timestamp: number) {
  const day = new Date(timestamp).getUTCDay()
  return startOfUtcDay(timestamp) - ((day + 6) % 7) * 24 * 60 * 60 * 1000
}

export function buildUsageData(records: UsageRecord[], grouping: UsageGrouping) {
  const sortedRecords = [...records].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
  const points = new Map<number, Record<string, string | number>>()
  sortedRecords.forEach((record) => {
    const timestamp = new Date(record.date).getTime()
    const bucket = grouping === 'hourly' ? Math.floor(timestamp / 3_600_000) * 3_600_000 : grouping === 'daily' ? startOfUtcDay(timestamp) : timestamp
    const point = points.get(bucket) ?? {
      timestamp: bucket,
      label: grouping === 'daily' ? new Date(bucket).toISOString().slice(0, 10) : new Date(bucket).toISOString().slice(0, 16).replace('T', ' '),
      all: 0,
    }
    point.all = Number(point.all) + record.total
    point[record.user_id] = Number(point[record.user_id] ?? 0) + record.total
    points.set(bucket, point)
  })
  return [...points.values()]
}

export function buildWeeklyData(records: UsageRecord[], anchor: number) {
  const currentWeek = startOfUtcWeek(anchor)
  return Array.from({ length: 7 }, (_, dayIndex) => {
    const point: Record<string, string | number> = {
      label: new Intl.DateTimeFormat(undefined, { weekday: 'short', timeZone: 'UTC' }).format(new Date(currentWeek + dayIndex * 86_400_000)),
    }
    for (let weekIndex = 0; weekIndex < 4; weekIndex += 1) {
      const dayStart = currentWeek - weekIndex * 7 * 86_400_000 + dayIndex * 86_400_000
      point[`week${weekIndex}`] = records.filter((record) => startOfUtcDay(new Date(record.date).getTime()) === dayStart).reduce((sum, record) => sum + record.total, 0)
    }
    return point
  })
}