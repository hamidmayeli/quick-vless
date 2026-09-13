import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import styles from './Layout.module.css'

interface LayoutProps {
  onLogout: () => void
}

export function Layout({ onLogout }: LayoutProps) {
  const navigate = useNavigate()

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
        </div>
        <button className={styles.logout} onClick={handleLogout}>Logout</button>
      </nav>
      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  )
}
