const UNIT_MULTIPLIERS: Record<string, number> = {
  b: 1,
  kb: 1024,
  mb: 1024 ** 2,
  mg: 1024 ** 2,
  gb: 1024 ** 3,
  tb: 1024 ** 4,
}

export function parseQuota(value: string): number | null {
  const normalized = value.trim().toLowerCase()
  if (!normalized) return null

  const match = normalized.match(/^(\d+(?:\.\d+)?)\s*(b|kb|mb|mg|gb|tb)$/)
  if (!match) return null

  const amount = Number(match[1])
  const bytes = amount * UNIT_MULTIPLIERS[match[2]]
  if (!Number.isSafeInteger(bytes) || bytes < 0) return null
  return bytes
}

export function formatQuota(bytes: number | null): string {
  if (bytes == null) return 'Unlimited'
  if (bytes % (1024 ** 3) === 0) return `${bytes / (1024 ** 3)} GB`
  if (bytes % (1024 ** 2) === 0) return `${bytes / (1024 ** 2)} MB`
  return `${bytes} B`
}
