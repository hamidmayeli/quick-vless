import { Component, lazy, Suspense, type ComponentType, type ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuth } from '@/hooks/useAuth'
import { Layout } from '@/components/Layout'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { PwaUpdatePrompt } from '@/components/PwaUpdatePrompt'

const PRELOAD_RECOVERY_KEY = 'vite-preload-recovery-attempted'

function markPreloadRecoveryAttempt() {
  try {
    if (sessionStorage.getItem(PRELOAD_RECOVERY_KEY)) return false
    sessionStorage.setItem(PRELOAD_RECOVERY_KEY, 'true')
    return true
  } catch {
    return false
  }
}

function clearPreloadRecoveryAttempt() {
  try {
    sessionStorage.removeItem(PRELOAD_RECOVERY_KEY)
  } catch {
    return
  }
}

window.addEventListener('vite:preloadError', (event) => {
  if (!markPreloadRecoveryAttempt()) return

  event.preventDefault()
  window.location.reload()
})

function lazyPage<Props extends object>(load: () => Promise<{ default: ComponentType<Props> }>) {
  return lazy(async () => {
    const page = await load()
    clearPreloadRecoveryAttempt()
    return page
  })
}

interface RouteErrorBoundaryProps { children: ReactNode }
interface RouteErrorBoundaryState { hasError: boolean }

class RouteErrorBoundary extends Component<RouteErrorBoundaryProps, RouteErrorBoundaryState> {
  state: RouteErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): RouteErrorBoundaryState {
    return { hasError: true }
  }

  render() {
    if (this.state.hasError) {
      return (
        <main role="alert">
          <p>Unable to load this page. Check your connection and try again.</p>
          <button type="button" onClick={() => {
            clearPreloadRecoveryAttempt()
            window.location.reload()
          }}>
            Reload page
          </button>
        </main>
      )
    }

    return this.props.children
  }
}

const LoginPage = lazyPage(async () => {
  const { LoginPage } = await import('@/pages/LoginPage')
  return { default: LoginPage }
})
const UsersPage = lazyPage(async () => {
  const { UsersPage } = await import('@/pages/UsersPage')
  return { default: UsersPage }
})
const UsagePage = lazyPage(async () => {
  const { UsagePage } = await import('@/pages/UsagePage')
  return { default: UsagePage }
})
const AdminsPage = lazyPage(async () => {
  const { AdminsPage } = await import('@/pages/AdminsPage')
  return { default: AdminsPage }
})
const ProfilesPage = lazyPage(async () => {
  const { ProfilesPage } = await import('@/pages/ProfilesPage')
  return { default: ProfilesPage }
})

export function App() {
  const { isAuthenticated, session, isSuperAdmin, login, logout } = useAuth()

  return (
    <>
      <BrowserRouter>
        <PwaUpdatePrompt />
        <RouteErrorBoundary>
          <Suspense fallback={<p role="status">Loading page...</p>}>
            <Routes>
              <Route
                path="/login"
                element={isAuthenticated ? <Navigate to="/users" replace /> : <LoginPage onLogin={login} />}
              />
              <Route
                element={
                  <ProtectedRoute isAuthenticated={isAuthenticated}>
                    <Layout onLogout={logout} username={session?.username ?? null} isSuperAdmin={isSuperAdmin} />
                  </ProtectedRoute>
                }
              >
                <Route index element={<Navigate to="/users" replace />} />
                <Route path="/users" element={<UsersPage showOwner={isSuperAdmin} />} />
                <Route path="/usage" element={<UsagePage />} />
                <Route path="/admins" element={isSuperAdmin ? <AdminsPage /> : <Navigate to="/users" replace />} />
                <Route path="/profiles" element={isSuperAdmin ? <ProfilesPage /> : <Navigate to="/users" replace />} />
              </Route>
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </Suspense>
        </RouteErrorBoundary>
      </BrowserRouter>
    </>
  )
}
