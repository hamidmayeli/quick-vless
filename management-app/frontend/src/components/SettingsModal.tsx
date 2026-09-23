import { useEffect } from 'react'
import { TIMEZONE_OPTIONS, useTimeZone, type TimeZoneSetting } from '@/settings/timezone'
import styles from './SettingsModal.module.css'

interface SettingsModalProps {
  onClose: () => void
}

export function SettingsModal({ onClose }: SettingsModalProps) {
  const [timeZone, setTimeZone] = useTimeZone()

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
