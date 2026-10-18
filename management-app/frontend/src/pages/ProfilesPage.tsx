import { useState } from 'react'
import { useXrayProfiles } from '@/hooks/useXrayProfiles'
import { ApiError } from '@/services/api'
import type { XrayProfile } from '@/types'
import styles from './UsersPage.module.css'
import profileStyles from './ProfilesPage.module.css'

const SWITCH_WARNING =
  'Xray restarts and active connections drop for a few seconds. Users must refresh their config URL to get the new link.'

function describeError(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.body?.errors?.length) return e.body.errors.join(' ')
    if (e.body?.error) return e.body.error
    if (e.status === 404) return 'That profile no longer exists. Reload the page.'
  }
  return 'The switch failed. Try again.'
}

export function ProfilesPage() {
  const { state, loading, error, activate, reapply } = useXrayProfiles()
  const [busy, setBusy] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  async function run(name: string, action: () => Promise<void>, success: string) {
    setBusy(name)
    setActionError(null)
    setNotice(null)
    try {
      await action()
      setNotice(success)
    } catch (e) {
      setActionError(describeError(e))
    } finally {
      setBusy(null)
    }
  }

  async function handleActivate(profile: XrayProfile) {
    if (!confirm(`Activate ${profile.name}?\n\n${SWITCH_WARNING}`)) return
    await run(profile.name, () => activate(profile.name),
      `${profile.name} is now active. Users must refresh their config URL to get the new link.`)
  }

  async function handleReapply(name: string) {
    await run(name, reapply, `${name} was re-applied.`)
  }

  if (loading) return <p className={styles.state}>Loading profiles…</p>
  if (error || !state) return <p className={styles.error}>{error ?? 'Failed to load profiles'}</p>

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h2 className={styles.title}>Profiles</h2>
      </div>
      <p className={profileStyles.intro}>
        A profile is a way of serving VPN users, such as REALITY or a WebSocket through Cloudflare. Each one is a
        folder in <code>xray/</code> on the server. One profile is active at a time, and every user&apos;s config
        URL returns its link.
      </p>

      {state.mode === 'legacy' && (
        <p className={profileStyles.bannerWarning} role="status">
          Profiles are not enabled on this server. Run <code>install.sh --upgrade</code> to enable them.
        </p>
      )}
      {state.last_error && <p className={profileStyles.bannerError} role="alert">{state.last_error}</p>}
      {state.applied === false && state.active && (
        <div className={profileStyles.bannerWarning} role="status">
          <span>Xray is not running the selected profile yet.</span>
          <button className={styles.btnSmall} onClick={() => void handleReapply(state.active!)} disabled={busy !== null}>
            Re-apply
          </button>
        </div>
      )}
      {actionError && <p className={profileStyles.bannerError} role="alert">{actionError}</p>}
      {notice && <p className={profileStyles.bannerSuccess} role="status">{notice}</p>}
      {busy && (
        <p className={styles.state} role="status">
          Switching to {busy}… Xray restarts, so this can take up to a minute.
        </p>
      )}

      <div className={profileStyles.list}>
        {state.profiles.map((p) => (
          <section key={p.name} className={p.active ? profileStyles.cardActive : profileStyles.card} aria-label={p.name}>
            <div className={profileStyles.cardHeader}>
              <h3 className={profileStyles.name}>{p.name}</h3>
              {p.active && <span className={styles.badgeOn}>Active</span>}
              {!p.valid && <span className={profileStyles.badgeWarn}>Needs attention</span>}
            </div>
            {p.inbounds.length > 0 && (
              <ul className={profileStyles.inbounds}>
                {p.inbounds.map((i) => (
                  <li key={i.tag}>
                    {i.tag}: {i.network} + {i.security}{i.flow ? ` · ${i.flow}` : ''}
                  </li>
                ))}
              </ul>
            )}
            {p.errors.length > 0 && (
              <ul className={profileStyles.errors}>
                {p.errors.map((e) => <li key={e}>{e}</li>)}
              </ul>
            )}
            <div className={profileStyles.actions}>
              {p.active ? (
                <button className={styles.btnSmall} onClick={() => void handleReapply(p.name)} disabled={busy !== null || !p.valid}>
                  Re-apply
                </button>
              ) : (
                <button className={styles.btnPrimary} onClick={() => void handleActivate(p)} disabled={busy !== null || !p.valid}>
                  {busy === p.name ? 'Switching…' : 'Activate'}
                </button>
              )}
            </div>
          </section>
        ))}
      </div>

      {state.mode === 'profiles' && state.profiles.length === 0 && (
        <p className={styles.state}>No profiles found on the server.</p>
      )}
    </div>
  )
}
