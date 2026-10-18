import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { SettingsModal } from './SettingsModal'
import styles from './Layout.module.css'

interface LayoutProps {
  onLogout: () => void
  username: string | null
  isSuperAdmin: boolean
}

export function Layout({ onLogout, username, isSuperAdmin }: LayoutProps) {
  const navigate = useNavigate()
  const [settingsOpen, setSettingsOpen] = useState(false)

  function handleLogout() {
    onLogout()
    navigate('/login', { replace: true })
  }

  return (
    <div className={styles.shell}>
      <nav className={styles.nav}>
        <span className={styles.brand}>Easy Xray</span>
        <div className={styles.links}>
          <NavLink to="/users" className={({ isActive }) => isActive ? styles.active : ''}>Users</NavLink>
          <NavLink to="/usage" className={({ isActive }) => isActive ? styles.active : ''}>Usage</NavLink>
          {isSuperAdmin && (
            <>
              <NavLink to="/admins" className={({ isActive }) => isActive ? styles.active : ''}>Admins</NavLink>
              <NavLink to="/profiles" className={({ isActive }) => isActive ? styles.active : ''}>Profiles</NavLink>
            </>
          )}
          <button
            type="button"
            className={`${styles.navButton} ${settingsOpen ? styles.active : ''}`}
            onClick={() => setSettingsOpen(true)}
          >
            Settings
          </button>
        </div>
        {username && (
          <span className={styles.whoami} data-testid="signed-in-as">
            {username}{isSuperAdmin ? ' · Super admin' : ''}
          </span>
        )}
        <button className={styles.logout} onClick={handleLogout}>Logout</button>
      </nav>
      <main className={styles.main}>
        <Outlet />
      </main>
      {settingsOpen && <SettingsModal onClose={() => setSettingsOpen(false)} />}
    </div>
  )
}
