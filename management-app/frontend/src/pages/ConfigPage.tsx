import { useState, useEffect } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { usersApi, configApi } from '@/services/api'
import styles from './ConfigPage.module.css'

export function ConfigPage() {
  const [url, setUrl] = useState<string | null>(null)
  const [shareUrl, setShareUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [copiedShare, setCopiedShare] = useState(false)

  useEffect(() => {
    void (async () => {
      setLoading(true)
      setError(null)
      try {
        const users = await usersApi.list()
        const first = users.find((u) => u.enabled)
        if (!first) {
          setError('No enabled users found')
          return
        }
        const vlessUrl = await configApi.getByUser(first.id)
        setUrl(vlessUrl)
        setShareUrl(`${window.location.origin}/api/v1/config/${first.id}`)
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to load config')
      } finally {
        setLoading(false)
      }
    })()
  }, [])

  async function handleCopy() {
    if (!url) return
    await navigator.clipboard.writeText(url)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  async function handleCopyShare() {
    if (!shareUrl) return
    await navigator.clipboard.writeText(shareUrl)
    setCopiedShare(true)
    setTimeout(() => setCopiedShare(false), 2000)
  }

  if (loading) return <p className={styles.state}>Loading config…</p>
  if (error) return <p className={styles.error}>{error}</p>

  return (
    <div className={styles.page}>
      <h2 className={styles.title}>My Connection Config</h2>
      <p className={styles.hint}>Scan the QR code or copy the URL into your Xray client.</p>

      <div className={styles.card}>
        {url && (
          <div className={styles.qr}>
            <QRCodeSVG value={url} size={220} bgColor="#1a1a2e" fgColor="#e8e8f0" level="M" />
          </div>
        )}
        <div className={styles.urlBox}>
          <code className={styles.url}>{url}</code>
          <button className={styles.copyBtn} onClick={() => void handleCopy()}>
            {copied ? 'Copied!' : 'Copy'}
          </button>
        </div>
      </div>

      {shareUrl && (
        <div className={styles.card}>
          <p className={styles.hint}>Share this link with the user to let them fetch their config:</p>
          <div className={styles.urlBox}>
            <code className={styles.url}>{shareUrl}</code>
            <button className={styles.copyBtn} onClick={() => void handleCopyShare()}>
              {copiedShare ? 'Copied!' : 'Copy Link'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
