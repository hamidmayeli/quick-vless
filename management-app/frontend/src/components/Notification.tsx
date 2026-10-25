import { useEffect } from 'react'
import styles from './Notification.module.css'

export type NotificationVariant = 'info' | 'success' | 'warning' | 'error'

interface NotificationProps {
  variant: NotificationVariant
  message: string
  title?: string
  action?: { label: string; onClick: () => void }
  onDismiss: () => void
  durationMs?: number
}

export function Notification({ variant, message, title, action, onDismiss, durationMs = 6000 }: NotificationProps) {
  useEffect(() => {
    if (durationMs <= 0) return
    const timeout = window.setTimeout(onDismiss, durationMs)
    return () => window.clearTimeout(timeout)
  }, [durationMs, onDismiss])

  return (
    <div
      className={`${styles.notification} ${styles[variant]}`}
      role={variant === 'warning' || variant === 'error' ? 'alert' : 'status'}
      aria-live={variant === 'warning' || variant === 'error' ? 'assertive' : 'polite'}
    >
      <div className={styles.content}>
        {title && <strong className={styles.title}>{title}</strong>}
        <p className={styles.message}>{message}</p>
        {action && <button className={styles.action} type="button" onClick={action.onClick}>{action.label}</button>}
      </div>
      <button className={styles.dismiss} type="button" onClick={onDismiss} aria-label="Dismiss notification">
        ×
      </button>
    </div>
  )
}