import { useEffect, useState } from 'react'
import { getStoredTimeZone, setStoredTimeZone, TIMEZONE_OPTIONS, type TimeZoneSetting } from '@/settings/timezone'
import { getThemePreference, setThemePreference, type ThemePreference } from '@/settings/theme'
import { settingsApi } from '@/services/api'
import styles from './SettingsModal.module.css'

interface SettingsModalProps {
  isSuperAdmin: boolean
  onClose: () => void
}

export function SettingsModal({ isSuperAdmin, onClose }: SettingsModalProps) {
  const [theme, setTheme] = useState<ThemePreference>(getThemePreference)
  const [timeZone, setTimeZone] = useState<TimeZoneSetting>(getStoredTimeZone)
  const [domainOverride, setDomainOverride] = useState('')
  const [savedDomainOverride, setSavedDomainOverride] = useState('')
  const [domainLoaded, setDomainLoaded] = useState(false)
  const [applyingChanges, setApplyingChanges] = useState(false)
  const [domainError, setDomainError] = useState<string | null>(null)

  useEffect(() => {
    if (!isSuperAdmin) return
    void settingsApi.get().then((settings) => {
      const savedDomain = settings.domain_override ?? ''
      setDomainOverride(savedDomain)
      setSavedDomainOverride(savedDomain)
      setDomainLoaded(true)
    })
      .catch(() => setDomainError('Could not load the domain override setting.'))
  }, [isSuperAdmin])

  async function applyChanges() {
    setApplyingChanges(true)
    setDomainError(null)
    try {
      const normalizedDomain = domainOverride.trim()
      if (isSuperAdmin && domainLoaded && normalizedDomain !== savedDomainOverride) {
        const settings = await settingsApi.update(normalizedDomain)
        const savedDomain = settings.domain_override ?? ''
        setDomainOverride(savedDomain)
        setSavedDomainOverride(savedDomain)
      }
      setThemePreference(theme)
      setStoredTimeZone(timeZone)
      onClose()
    } catch (error) {
      setDomainError(error instanceof Error ? error.message : 'Could not save the domain override.')
    } finally {
      setApplyingChanges(false)
    }
  }

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  return (
    <div className={styles.overlay} onClick={onClose} role="presentation">
      <div
        className={styles.dialog}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="settings-dialog-title"
      >
        <div className={styles.header}>
          <h3 id="settings-dialog-title" className={styles.title}>Settings</h3>
          <button type="button" className={styles.closeBtn} onClick={onClose} aria-label="Close settings">
            ×
          </button>
        </div>

        <div className={styles.body}>
          <div className={styles.section}>
            <label className={styles.field}>
              <span className={styles.fieldLabel}>Appearance</span>
              <span className={styles.fieldDescription}>Choose a theme or follow your device setting.</span>
              <select
                className={styles.select}
                value={theme}
                onChange={(event) => {
                  const preference = event.target.value as ThemePreference
                  setTheme(preference)
                }}
              >
                <option value="system">System</option>
                <option value="light">Light</option>
                <option value="dark">Dark</option>
              </select>
            </label>
          </div>
          <div className={styles.section}>
            <label className={styles.field}>
              <span className={styles.fieldLabel}>Time zone</span>
              <span className={styles.fieldDescription}>Choose the time zone used for usage reports and date filters.</span>
              <select
                className={styles.select}
                value={timeZone}
                onChange={(event) => setTimeZone(event.target.value as TimeZoneSetting)}
              >
                {TIMEZONE_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
          {isSuperAdmin && (
            <div className={styles.section}>
              <label className={styles.field}>
                <span className={styles.fieldLabel}>Domain override</span>
                <span className={styles.fieldDescription}>Config links will use this host. Leave blank to use the current domain.</span>
                <input
                  className={styles.input}
                  type="text"
                  inputMode="url"
                  autoComplete="url"
                  placeholder="vpn.example.com"
                  value={domainOverride}
                  disabled={!domainLoaded || applyingChanges}
                  onChange={(event) => { setDomainOverride(event.target.value); setDomainError(null) }}
                />
              </label>
              {domainError && <p className={styles.error} role="alert">{domainError}</p>}
            </div>
          )}
        </div>

        <div className={styles.footer}>
          <button
            type="button"
            className={styles.doneBtn}
            onClick={() => void applyChanges()}
            disabled={applyingChanges || (isSuperAdmin && !domainLoaded)}
          >
            {applyingChanges ? 'Applying…' : 'Apply changes'}
          </button>
        </div>
      </div>
    </div>
  )
}
