import { useState } from 'react'
import { useUsers } from '@/hooks/useUsers'
import type { User } from '@/types'
import { formatQuota, parseQuota } from '@/services/quota'
import styles from './UsersPage.module.css'

type UserForm = Omit<User, 'id' | 'secret' | 'quota'> & { quota: string }

const EMPTY_FORM: UserForm = {
  name: '',
  quota: '',
  expiry: null,
  single_connection: false,
  enabled: true,
}

export function UsersPage() {
  const { users, loading, error, create, update, remove, toggle } = useUsers()
  const [editing, setEditing] = useState<User | null>(null)
  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState<UserForm>(EMPTY_FORM)
  const [saving, setSaving] = useState(false)
  const [copiedId, setCopiedId] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  function openCreate() {
    setForm(EMPTY_FORM)
    setFormError(null)
    setCreating(true)
    setEditing(null)
  }

  function openEdit(user: User) {
    setEditing(user)
    setForm({ name: user.name, quota: user.quota == null ? '' : formatQuota(user.quota), expiry: user.expiry, single_connection: user.single_connection, enabled: user.enabled })
    setFormError(null)
    setCreating(false)
  }

  function closeForm() { setEditing(null); setCreating(false) }

  async function handleSave() {
    const quota = parseQuota(form.quota)
    if (form.quota.trim() && quota == null) {
      setFormError('Enter a quota such as 10GB, 500MB, or 1TB.')
      return
    }
    setSaving(true)
    try {
      const data = { ...form, quota }
      if (creating) await create(data)
      else if (editing) await update(editing.id, data)
      closeForm()
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this user?')) return
    await remove(id)
  }

  async function handleCopyConfigUrl(userId: string) {
    const url = `${window.location.origin}/api/v1/config/${userId}`
    await navigator.clipboard.writeText(url)
    setCopiedId(userId)
    setTimeout(() => setCopiedId(null), 2000)
  }

  if (loading) return <p className={styles.state}>Loading users…</p>
  if (error) return <p className={styles.error}>{error}</p>

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h2 className={styles.title}>Users</h2>
        <button className={styles.btnPrimary} onClick={openCreate}>+ Add User</button>
      </div>

      {(creating || editing) && (
        <div className={styles.formCard}>
          <h3 className={styles.formTitle}>{creating ? 'New User' : `Edit — ${editing!.name}`}</h3>
          <div className={styles.grid}>
            <label className={styles.field}>
              Name
              <input className={styles.input} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
            </label>
            <label className={styles.field}>
              Quota
              <input className={styles.input} type="text" inputMode="text" value={form.quota} placeholder="Unlimited (e.g. 25GB)"
                onChange={(e) => { setForm({ ...form, quota: e.target.value }); setFormError(null) }} />
            </label>
            <label className={styles.field}>
              Expiry date
              <input className={styles.input} type="date" value={form.expiry ?? ''}
                onChange={(e) => setForm({ ...form, expiry: e.target.value || null })} />
            </label>
            <label className={styles.checkField}>
              <input type="checkbox" checked={form.single_connection} onChange={(e) => setForm({ ...form, single_connection: e.target.checked })} />
              Single connection (kick old on reconnect)
            </label>
            <label className={styles.checkField}>
              <input type="checkbox" checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
              Enabled
            </label>
          </div>
          {formError && <p className={styles.error}>{formError}</p>}
          <div className={styles.formActions}>
            <button className={styles.btnPrimary} onClick={handleSave} disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
            <button className={styles.btnGhost} onClick={closeForm}>Cancel</button>
          </div>
        </div>
      )}

      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Name</th>
              <th>Quota</th>
              <th>Expiry</th>
              <th>Single conn.</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {users.map((u) => (
              <tr key={u.id} className={!u.enabled ? styles.disabled : ''}>
                <td data-label="Name">{u.name}</td>
                <td data-label="Quota">{formatQuota(u.quota)}</td>
                <td data-label="Expiry">{u.expiry ?? '—'}</td>
                <td data-label="Single conn.">{u.single_connection ? 'Yes' : 'No'}</td>
                <td data-label="Status">
                  <span className={u.enabled ? styles.badgeOn : styles.badgeOff}>
                    {u.enabled ? 'Active' : 'Disabled'}
                  </span>
                </td>
                <td data-label="Actions" className={styles.actions}>
                  <button className={styles.btnSmall} onClick={() => openEdit(u)}>Edit</button>
                  <button className={styles.btnSmall} onClick={() => toggle(u)}>
                    {u.enabled ? 'Disable' : 'Enable'}
                  </button>
                  <button className={styles.btnSmall} onClick={() => void handleCopyConfigUrl(u.id)}>
                    {copiedId === u.id ? 'Copied!' : 'Copy Config URL'}
                  </button>
                  <button className={`${styles.btnSmall} ${styles.danger}`} onClick={() => handleDelete(u.id)}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {users.length === 0 && <p className={styles.state}>No users yet. Add one above.</p>}
    </div>
  )
}
