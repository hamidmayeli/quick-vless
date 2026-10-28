import { useState, useSyncExternalStore } from 'react'
import { useLocation } from 'react-router-dom'
import { Notification } from '@/components/Notification'
import { applyPwaUpdate, checkForPwaUpdate, hasPwaUpdate, subscribeToPwaUpdates } from '@/services/pwaUpdates'
import { useEffect } from 'react'

export function PwaUpdatePrompt() {
  const { pathname } = useLocation()
  const updateAvailable = useSyncExternalStore(subscribeToPwaUpdates, hasPwaUpdate, () => false)
  const [dismissed, setDismissed] = useState(false)

  useEffect(() => {
    const checkWhenVisible = () => {
      if (document.visibilityState === 'visible') checkForPwaUpdate()
    }

    checkForPwaUpdate()
    window.addEventListener('focus', checkForPwaUpdate)
    document.addEventListener('visibilitychange', checkWhenVisible)
    return () => {
      window.removeEventListener('focus', checkForPwaUpdate)
      document.removeEventListener('visibilitychange', checkWhenVisible)
    }
  }, [pathname])

  if (!updateAvailable || dismissed) return null

  return (
    <Notification
      variant="info"
      title="Update available"
      message="A new version is ready. Refresh to install it."
      action={{ label: 'Refresh', onClick: () => void applyPwaUpdate() }}
      onDismiss={() => setDismissed(true)}
      durationMs={0}
    />
  )
}