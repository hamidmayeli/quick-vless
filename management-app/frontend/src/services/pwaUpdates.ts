import { registerSW } from 'virtual:pwa-register'

let updateAvailable = false
const listeners = new Set<() => void>()

function publishUpdateAvailable() {
  updateAvailable = true
  listeners.forEach((listener) => listener())
}

export function subscribeToPwaUpdates(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function hasPwaUpdate() {
  return updateAvailable
}

const updateServiceWorker = registerSW({
  immediate: true,
  onNeedRefresh: publishUpdateAvailable,
})

export function applyPwaUpdate() {
  return updateServiceWorker(true)
}