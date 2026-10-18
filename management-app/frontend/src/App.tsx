import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuth } from '@/hooks/useAuth'
import { Layout } from '@/components/Layout'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { LoginPage } from '@/pages/LoginPage'
import { UsersPage } from '@/pages/UsersPage'
import { UsagePage } from '@/pages/UsagePage'
import { AdminsPage } from '@/pages/AdminsPage'

export function App() {
  const { isAuthenticated, session, isSuperAdmin, login, logout } = useAuth()

  return (
    <BrowserRouter>
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
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
