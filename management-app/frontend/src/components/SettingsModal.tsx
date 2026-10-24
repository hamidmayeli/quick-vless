import { useEffect, useState } from 'react'
import { TIMEZONE_OPTIONS, useTimeZone, type TimeZoneSetting } from '@/settings/timezone'
import { settingsApi } from '@/services/api'
import styles from './SettingsModal.module.css'

interface SettingsModalProps {
  isSuperAdmin: boolean
  onClose: () => void
}

export function SettingsModal({ isSuperAdmin, onClose }: SettingsModalProps) {
  const [timeZone, setTimeZone] = useTimeZone()
  const [domainOverride, setDomainOverride] = useState('')
  const [domainLoaded, setDomainLoaded] = useState(false)
  const [savingDomain, setSavingDomain] = useState(false)
  const [domainError, setDomainError] = useState<string | null>(null)

  useEffect(() => {
    if (!isSuperAdmin) return
    void settingsApi.get().then((settings) => {
      setDomainOverride(settings.domain_override ?? '')
      setDomainLoaded(true)
    })
      .catch(() => setDomainError('Could not load the domain override setting.'))
  }, [isSuperAdmin])

  async function saveDomain() {
    setSavingDomain(true)
    setDomainError(null)
    try {
      const settings = await settingsApi.update(domainOverride.trim())
      setDomainOverride(settings.domain_override ?? '')
    } catch (error) {
      setDomainError(error instanceof Error ? error.message : 'Could not save the domain override.')
    } finally {
      setSavingDomain(false)
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
                  disabled={!domainLoaded || savingDomain}
                  onChange={(event) => { setDomainOverride(event.target.value); setDomainError(null) }}
                />
              </label>
              {domainError && <p className={styles.error} role="alert">{domainError}</p>}
              <button type="button" className={styles.doneBtn} onClick={() => void saveDomain()} disabled={!domainLoaded || savingDomain}>
                {savingDomain ? 'Saving…' : 'Save domain'}
              </button>
            </div>
          )}
        </div>

        <div className={styles.footer}>
          <button type="button" className={styles.doneBtn} onClick={onClose}>
            Done
          </button>
        </div>
      </div>
    </div>
  )
}
