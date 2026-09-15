import { useEffect } from 'react'

export function useRefreshOnReconnect(refresh: () => void | Promise<void>) {
  useEffect(() => {
    function handleReconnect() {
      if (document.visibilityState === 'visible') void refresh()
    }

    window.addEventListener('focus', handleReconnect)
    document.addEventListener('visibilitychange', handleReconnect)
    return () => {
      window.removeEventListener('focus', handleReconnect)
      document.removeEventListener('visibilitychange', handleReconnect)
    }
  }, [refresh])
}
