import { useState } from 'react'
import { formatDate } from '../lib/format'
import type { Clip } from '../lib/types'
import { useApi } from '../lib/useApi'

const CLIP_TYPE_LABELS: Record<Clip['type'], string> = {
  SingleClip: 'Clip',
  HighlightReel: 'Highlight reel',
}

const STATUS_HINTS: Partial<Record<Clip['status'], string>> = {
  Queued: 'Waiting in the queue…',
  Processing: 'Cutting the video…',
}

function shareUrl(clip: Clip): string {
  return `${window.location.origin}/shared/${clip.shareToken}`
}

/** Clips and reels of a match with their processing status, share link and download (FR-14 to FR-16). */
export function ClipList({ clips }: { clips: Clip[] }) {
  if (clips.length === 0) {
    return <p>No clips generated yet.</p>
  }

  return (
    <ul className="clip-list">
      {clips.map((clip) => (
        <ClipItem key={clip.id} clip={clip} />
      ))}
    </ul>
  )
}

function ClipItem({ clip }: { clip: Clip }) {
  const api = useApi()
  const [copied, setCopied] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Read once when the item appears; render itself has to stay free of clock reads.
  const [shownAt] = useState(() => Date.now())
  const shareExpired = clip.shareExpiresAt !== null && new Date(clip.shareExpiresAt).getTime() <= shownAt

  async function copyShareLink() {
    try {
      await navigator.clipboard.writeText(shareUrl(clip))
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      setError('Could not copy automatically — select the link and copy it by hand.')
    }
  }

  async function download() {
    setDownloading(true)
    setError(null)
    try {
      const { url } = await api.getClipDownloadUrl(clip.id)
      // The signed URL answers with Content-Disposition: attachment, so the browser saves the file and stays on this page.
      window.location.assign(url)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to download the clip')
    } finally {
      setDownloading(false)
    }
  }

  return (
    <li className="clip">
      <div className="clip__header">
        <strong>{CLIP_TYPE_LABELS[clip.type]}</strong>
        <span className={`status status--${clip.status.toLowerCase()}`}>{clip.status}</span>
        <span className="clip__meta">{formatDate(clip.createdAt)}</span>
      </div>

      {STATUS_HINTS[clip.status] && <p className="clip__meta">{STATUS_HINTS[clip.status]}</p>}

      {clip.status === 'Failed' && <p className="error">{clip.failureReason ?? 'The clip could not be generated.'}</p>}

      {clip.status === 'Done' && (
        <>
          {shareExpired ? (
            <p className="clip__meta">The share link has expired. You can still download the clip.</p>
          ) : (
            <div className="clip__share">
              <input
                type="text"
                readOnly
                aria-label="Share link"
                value={shareUrl(clip)}
                onFocus={(event) => event.target.select()}
              />
              <button type="button" onClick={copyShareLink}>
                {copied ? 'Copied' : 'Copy link'}
              </button>
            </div>
          )}
          <div className="button-row">
            <button type="button" onClick={download} disabled={downloading}>
              {downloading ? 'Preparing…' : 'Download'}
            </button>
            {!shareExpired && clip.shareExpiresAt && (
              <span className="clip__meta">
                Anyone with the link can watch until {formatDate(clip.shareExpiresAt)}.
              </span>
            )}
          </div>
        </>
      )}

      {error && <p className="error">{error}</p>}
    </li>
  )
}
