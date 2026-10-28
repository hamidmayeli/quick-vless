import { registerSW } from 'virtual:pwa-register'

let updateAvailable = false
let serviceWorkerRegistration: ServiceWorkerRegistration | null = null
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
  onRegisteredSW: (_serviceWorkerUrl, registration) => {
    serviceWorkerRegistration = registration ?? null
  },
})

export function checkForPwaUpdate() {
  void serviceWorkerRegistration?.update().catch(() => undefined)
}

export function applyPwaUpdate() {
  return updateServiceWorker(true)
}