import type { UsageRecord } from '@/types'

export const CHART_COLORS = [
  '#3b82f6', '#ef4444', '#10b981', '#f59e0b', '#8b5cf6', '#06b6d4',
  '#ec4899', '#84cc16', '#6366f1', '#f97316', '#be123c', '#d946ef',
  '#eab308', '#3b0764', '#064e3b', '#7f1d1d', '#0284c7', '#14b8a6',
  '#15803d', '#b45309', '#6d28d9', '#0f766e', '#a21caf', '#4d7c0f',
  '#1e40af', '#c2410c', '#047857', '#854d0e', '#5b21b6', '#9f1239',
  '#334155',
];

export const BYTES_PER_GB = 1_073_741_824
export type UsageGrouping = 'none' | 'hourly' | 'daily'
export interface UsagePeriod { from: string; to: string }

export function formatBytes(value: number) {
  if (value < BYTES_PER_GB) return `${(value / (1024 * 1024)).toFixed(0)} MB`
  return `${(value / BYTES_PER_GB).toFixed(2)} GB`
}

export function filterRecordsByPeriod(records: UsageRecord[], period: UsagePeriod) {
  const from = period.from ? Date.parse(period.from) : Number.NEGATIVE_INFINITY
  const to = period.to ? Date.parse(period.to) : Number.POSITIVE_INFINITY
  return records.filter((record) => {
    const timestamp = new Date(record.date).getTime()
    return timestamp >= from && timestamp <= to
  })
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