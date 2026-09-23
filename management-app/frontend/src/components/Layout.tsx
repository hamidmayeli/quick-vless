import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { SettingsModal } from './SettingsModal'
import styles from './Layout.module.css'

interface LayoutProps {
  onLogout: () => void
}

export function Layout({ onLogout }: LayoutProps) {
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
          <button
            type="button"
            className={`${styles.navButton} ${settingsOpen ? styles.active : ''}`}
            onClick={() => setSettingsOpen(true)}
          >
            Settings
          </button>
        </div>
        <button className={styles.logout} onClick={handleLogout}>Logout</button>
      </nav>
      <main className={styles.main}>
        <Outlet />
      </main>
      {settingsOpen && <SettingsModal onClose={() => setSettingsOpen(false)} />}
    </div>
  )
}
