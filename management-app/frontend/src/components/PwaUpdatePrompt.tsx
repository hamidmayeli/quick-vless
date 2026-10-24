import { useState, useSyncExternalStore } from 'react'
import { Notification } from '@/components/Notification'
import { applyPwaUpdate, hasPwaUpdate, subscribeToPwaUpdates } from '@/services/pwaUpdates'

export function PwaUpdatePrompt() {
  const updateAvailable = useSyncExternalStore(subscribeToPwaUpdates, hasPwaUpdate, () => false)
  const [dismissed, setDismissed] = useState(false)

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