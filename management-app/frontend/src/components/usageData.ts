import type { UsageRecord } from '@/types'
import { parseDateTimeLocal as parseTimeZoneDateTime } from '@/settings/timezone'

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

export function filterRecordsByPeriod(records: UsageRecord[], period: UsagePeriod, timeZone?: string) {
  const from = period.from ? parseTimeZoneDateTime(period.from, timeZone) : Number.NEGATIVE_INFINITY
  const to = period.to ? parseTimeZoneDateTime(period.to, timeZone) : Number.POSITIVE_INFINITY
  return records.filter((record) => {
    const timestamp = Date.parse(record.date)
    return timestamp >= from && timestamp <= to
  })
}

export function buildUsageData(records: UsageRecord[], grouping: UsageGrouping, timeZone?: string) {
  if (records.length === 0) return []

  const sortedRecords = [...records].sort((a, b) => Date.parse(a.date) - Date.parse(b.date))
  const points = new Map<string, Record<string, string | number>>()

  const dayFmt = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  })

  const hourFmt = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    hourCycle: 'h23',
  })

  const noneFmt = new Intl.DateTimeFormat(undefined, {
    timeZone,
    dateStyle: 'short',
    timeStyle: 'short',
  })

  for (const record of sortedRecords) {
    const recordDate = new Date(record.date)
    const timestamp = recordDate.getTime()
    let bucket: string
    let label: string

    if (grouping === 'daily') {
      bucket = dayFmt.format(recordDate)
      label = bucket
    } else if (grouping === 'hourly') {
      bucket = hourFmt.format(recordDate)
      label = bucket.replace(',', '') + ':00'
    } else {
      bucket = String(timestamp)
      label = noneFmt.format(recordDate)
    }

    const point = points.get(bucket) ?? {
      timestamp,
      label,
      all: 0,
    }
    point.all = Number(point.all) + record.total
    point[record.user_id] = Number(point[record.user_id] ?? 0) + record.total
    points.set(bucket, point)
  }

  return [...points.values()]
}

export function buildWeeklyData(records: UsageRecord[], anchor: number, timeZone?: string) {
  if (!anchor || records.length === 0) return []

  const dayFmt = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  })
  const weekdayFmt = new Intl.DateTimeFormat(undefined, { timeZone: 'UTC', weekday: 'short' })

  const dayTotals = new Map<string, number>()
  for (const record of records) {
    const key = dayFmt.format(new Date(record.date))
    dayTotals.set(key, (dayTotals.get(key) ?? 0) + record.total)
  }

  const anchorDayKey = dayFmt.format(new Date(anchor))
  const [y, m, d] = anchorDayKey.split('-').map(Number)
  const anchorCalTime = Date.UTC(y, m - 1, d)
  const dayOfWeek = new Date(anchorCalTime).getUTCDay()
  const monOffset = (dayOfWeek + 6) % 7
  const currentWeekMondayCal = anchorCalTime - monOffset * 86_400_000

  return Array.from({ length: 7 }, (_, dayIndex) => {
    const dayCal = currentWeekMondayCal + dayIndex * 86_400_000
    const point: Record<string, string | number> = {
      label: weekdayFmt.format(new Date(dayCal)),
    }
    for (let weekIndex = 0; weekIndex < 4; weekIndex += 1) {
      const targetDayCal = currentWeekMondayCal - weekIndex * 7 * 86_400_000 + dayIndex * 86_400_000
      const targetDayKey = new Date(targetDayCal).toISOString().slice(0, 10)
      point[`week${weekIndex}`] = dayTotals.get(targetDayKey) ?? 0
    }
    return point
  })
}