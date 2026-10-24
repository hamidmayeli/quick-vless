import { useEffect, useState } from 'react'
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
  const [menuOpen, setMenuOpen] = useState(false)

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setMenuOpen(false)
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [])

  function handleLogout() {
    setMenuOpen(false)
    onLogout()
    navigate('/login', { replace: true })
  }

  return (
    <div className={styles.shell}>
      {menuOpen && <button className={styles.menuBackdrop} onClick={() => setMenuOpen(false)} aria-label="Close menu" />}
      <nav className={styles.nav}>
        <span className={styles.brand}>Easy Xray</span>
        <button
          className={styles.menuToggle}
          type="button"
          aria-label={menuOpen ? 'Close menu' : 'Open menu'}
          aria-expanded={menuOpen}
          aria-controls="primary-menu"
          onClick={() => setMenuOpen((open) => !open)}
        >
          <span />
          <span />
          <span />
        </button>
        <div id="primary-menu" className={`${styles.menuPanel} ${menuOpen ? styles.menuPanelOpen : ''}`}>
          <div className={styles.links}>
            <NavLink to="/users" className={({ isActive }) => isActive ? styles.active : ''} onClick={() => setMenuOpen(false)}>Users</NavLink>
            <NavLink to="/usage" className={({ isActive }) => isActive ? styles.active : ''} onClick={() => setMenuOpen(false)}>Usage</NavLink>
            {isSuperAdmin && (
              <>
                <NavLink to="/admins" className={({ isActive }) => isActive ? styles.active : ''} onClick={() => setMenuOpen(false)}>Admins</NavLink>
                <NavLink to="/profiles" className={({ isActive }) => isActive ? styles.active : ''} onClick={() => setMenuOpen(false)}>Profiles</NavLink>
              </>
            )}
            <button
              type="button"
              className={`${styles.navButton} ${settingsOpen ? styles.active : ''}`}
              onClick={() => { setMenuOpen(false); setSettingsOpen(true) }}
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
        </div>
      </nav>
      <main className={styles.main}>
        <Outlet />
      </main>
      {settingsOpen && <SettingsModal isSuperAdmin={isSuperAdmin} onClose={() => setSettingsOpen(false)} />}
    </div>
  )
}
