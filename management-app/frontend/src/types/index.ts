export interface User {
  id: string
  name: string
  secret?: string
  quota: number | null
  expiry: string | null
  single_connection: boolean
  enabled: boolean
  created_by?: string | null
}

export type AdminRole = 'super_admin' | 'admin'

export interface Session {
  username: string
  role: AdminRole
}

export interface Admin {
  username: string
  role: AdminRole
  status: 'pending' | 'active'
  user_count: number
}

export interface XrayInbound {
  tag: string
  network: string
  security: string
  port: string | null
  flow: string
}

export interface XrayProfile {
  name: string
  active: boolean
  valid: boolean
  errors: string[]
  inbounds: XrayInbound[]
}

export interface XrayProfilesState {
  mode: 'profiles' | 'legacy'
  active: string | null
  /** Whether Xray runs the generated config; null when the server can't tell. */
  applied: boolean | null
  last_error: string | null
  profiles: XrayProfile[]
}

export interface AuthTokens {
  access_token: string
}

export interface UsageRecord {
  user_id: string
  date: string
  uplink: number
  downlink: number
  total: number
}

export interface UserUsageSummary {
  userId: string
  name: string
  totalBytes: number
  totalGb: number
  quotaGb: number | null
  records: UsageRecord[]
}

export interface ConfigResponse {
  id: string
  url: string
}
