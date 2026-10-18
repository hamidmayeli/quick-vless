import { useState, type FormEvent } from 'react'
import { useAdmins } from '@/hooks/useAdmins'
import { ApiError } from '@/services/api'
import type { Admin } from '@/types'
import styles from './UsersPage.module.css'
import adminStyles from './AdminsPage.module.css'

// Mirrors the backend's username rule.
const USERNAME_PATTERN = /^[A-Za-z0-9._@-]{1,64}$/
const INVALID_USERNAME = 'Use 1–64 letters, digits, or . _ @ -'

function createErrorMessage(e: unknown): string {
  if (e instanceof ApiError && e.status === 409) return 'An admin with this username already exists.'
  if (e instanceof ApiError && e.status === 400) return INVALID_USERNAME
  return 'Could not create the admin. Try again.'
}

export function AdminsPage() {
  const { admins, loading, error, create, remove } = useAdmins()
  const [creating, setCreating] = useState(false)
  const [username, setUsername] = useState('')
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  function openCreate() {
    setUsername('')
    setFormError(null)
    setCreating(true)
  }

  async function handleSave(e: FormEvent) {
    e.preventDefault()
    const trimmed = username.trim()
    if (!USERNAME_PATTERN.test(trimmed)) {
      setFormError(INVALID_USERNAME)
      return
    }
    setSaving(true)
    try {
      await create(trimmed)
      setCreating(false)
    } catch (err) {
      setFormError(createErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(admin: Admin) {
    const reassign = admin.user_count > 0
      ? ` Their ${admin.user_count === 1 ? '1 user' : `${admin.user_count} users`} will be reassigned to you.`
      : ''
    if (!confirm(`Delete admin ${admin.username}?${reassign}`)) return
    await remove(admin.username)
  }

  if (loading) return <p className={styles.state}>Loading admins…</p>
  if (error) return <p className={styles.error}>{error}</p>

  const hasOtherAdmins = admins.some((a) => a.role !== 'super_admin')

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h2 className={styles.title}>Admins</h2>
        <button className={styles.btnPrimary} onClick={openCreate}>+ Add Admin</button>
      </div>

      {creating && (
        <form className={styles.formCard} onSubmit={handleSave}>
          <h3 className={styles.formTitle}>New Admin</h3>
          <div className={styles.grid}>
            <label className={styles.field}>
              Username
              <input
                className={styles.input}
                value={username}
                onChange={(e) => { setUsername(e.target.value); setFormError(null) }}
                autoFocus
              />
            </label>
          </div>
          <p className={adminStyles.hint}>
            No password is needed. The admin chooses one the first time they sign in.
            Share the username and ask them to sign in soon.
          </p>
          {formError && <p className={styles.error}>{formError}</p>}
          <div className={styles.formActions}>
            <button className={styles.btnPrimary} type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
            <button className={styles.btnGhost} type="button" onClick={() => setCreating(false)}>Cancel</button>
          </div>
        </form>
      )}

      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Username</th>
              <th>Role</th>
              <th>Status</th>
              <th>Users</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {admins.map((a) => (
              <tr key={a.username}>
                <td data-label="Username">{a.username}</td>
                <td data-label="Role">{a.role === 'super_admin' ? 'Super admin' : 'Admin'}</td>
                <td data-label="Status">
                  {a.status === 'active'
                    ? <span className={styles.badgeOn}>Active</span>
                    : <span className={adminStyles.badgePending}>Pending first login</span>}
                </td>
                <td data-label="Users">{a.user_count}</td>
                <td data-label="Actions" className={styles.actions}>
                  {a.role !== 'super_admin' && (
                    <button className={`${styles.btnSmall} ${styles.danger}`} onClick={() => void handleDelete(a)}>
                      Delete
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {!hasOtherAdmins && <p className={styles.state}>No other admins yet. Add one above.</p>}
    </div>
  )
}
