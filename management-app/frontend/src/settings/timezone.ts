import { useState, useEffect, useCallback } from 'react'

export type TimeZoneSetting = 'browser' | 'UTC' | 'Asia/Tehran' | 'Europe/London'

export const TIMEZONE_STORAGE_KEY = 'usage-timezone'
export const TIMEZONE_CHANGE_EVENT = 'easy-xray:timezone-change'

export const TIMEZONE_OPTIONS: { value: TimeZoneSetting; label: string }[] = [
  { value: 'browser', label: 'User browser settings' },
  { value: 'UTC', label: 'UTC' },
  { value: 'Asia/Tehran', label: 'Tehran' },
  { value: 'Europe/London', label: 'London' },
]

export function getStoredTimeZone(): TimeZoneSetting {
  const value = localStorage.getItem(TIMEZONE_STORAGE_KEY)
  return TIMEZONE_OPTIONS.some((option) => option.value === value) ? (value as TimeZoneSetting) : 'browser'
}

export function setStoredTimeZone(setting: TimeZoneSetting): void {
  localStorage.setItem(TIMEZONE_STORAGE_KEY, setting)
  window.dispatchEvent(new CustomEvent(TIMEZONE_CHANGE_EVENT, { detail: setting }))
}

export function useTimeZone(): [TimeZoneSetting, (setting: TimeZoneSetting) => void] {
  const [timeZoneSetting, setSetting] = useState<TimeZoneSetting>(getStoredTimeZone)

  useEffect(() => {
    const handleUpdate = () => setSetting(getStoredTimeZone())
    window.addEventListener(TIMEZONE_CHANGE_EVENT, handleUpdate)
    window.addEventListener('storage', handleUpdate)
    return () => {
      window.removeEventListener(TIMEZONE_CHANGE_EVENT, handleUpdate)
      window.removeEventListener('storage', handleUpdate)
    }
  }, [])

  const updateSetting = useCallback((newSetting: TimeZoneSetting) => {
    setStoredTimeZone(newSetting)
    setSetting(newSetting)
  }, [])

  return [timeZoneSetting, updateSetting]
}

export function getTimeZone(setting: TimeZoneSetting): string | undefined {
  return setting === 'browser' ? undefined : setting
}

function dateParts(timestamp: number, timeZone?: string) {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date(timestamp))
  return Object.fromEntries(parts.filter(({ type }) => type !== 'literal').map(({ type, value }) => [type, Number(value)])) as Record<string, number>
}

export function formatDateTimeLocal(timestamp: number, timeZone?: string) {
  const parts = dateParts(timestamp, timeZone)
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${parts.year}-${pad(parts.month)}-${pad(parts.day)}T${pad(parts.hour)}:${pad(parts.minute)}`
}

function timeZoneOffset(timestamp: number, timeZone: string) {
  const parts = dateParts(timestamp, timeZone)
  return Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute) - timestamp
}

export function parseDateTimeLocal(value: string, timeZone?: string) {
  if (!value) return Number.NaN
  if (!timeZone) return Date.parse(value)

  const [date, time] = value.split('T')
  const [year, month, day] = date.split('-').map(Number)
  const [hour, minute] = time.split(':').map(Number)
  const wallTime = Date.UTC(year, month - 1, day, hour, minute)
  const firstGuess = wallTime - timeZoneOffset(wallTime, timeZone)
  return wallTime - timeZoneOffset(firstGuess, timeZone)
}