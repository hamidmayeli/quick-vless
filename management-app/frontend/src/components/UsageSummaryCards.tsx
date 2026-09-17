import type { UserUsageSummary } from '@/types'
import { CHART_COLORS } from './usageData'
import styles from '../pages/UsagePage.module.css'

export function UsageSummaryCards({ summaries }: { summaries: UserUsageSummary[] }) {
  return (
    <div className={styles.cards}>
      {summaries.sort((a, b) => b.totalBytes - a.totalBytes).map((summary, index) => {
        const pct = summary.quotaGb ? Math.min(100, Math.round((summary.totalGb / summary.quotaGb) * 100)) : null
        const dotColor = CHART_COLORS[index % CHART_COLORS.length]
        return (
          <div key={summary.userId} className={styles.card}>
            <div className={styles.cardHeader}>
              <span className={styles.userDot} style={{ backgroundColor: dotColor }} />
              <span className={styles.userName} title={summary.name}>{summary.name}</span>
            </div>
            <span className={styles.usedLabel}>{summary.totalGb.toFixed(2)} GB</span>
            <div className={styles.cardFooter}>
              {summary.quotaGb != null && (
                <>
                  <div className={styles.progressTrack}>
                    <div
                      className={styles.progressBar}
                      style={{ width: `${pct}%`, background: pct! >= 90 ? '#ff6b6b' : '#7c7cff' }}
                    />
                  </div>
                  <span className={styles.quota}>{pct}% of {summary.quotaGb} GB</span>
                </>
              )}
              {summary.quotaGb == null && <span className={styles.quota}>Unlimited</span>}
            </div>
          </div>
        )
      })}
    </div>
  )
}