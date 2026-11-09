import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { formatDate } from '../lib/format'
import type { ClipStatus, ClipType } from '../lib/types'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL

interface SharedClip {
  id: string
  type: ClipType
  status: ClipStatus
  playbackUrl: string | null
  expiresAt: string | null
}

const TITLES: Record<ClipType, string> = {
  SingleClip: 'Match clip',
  HighlightReel: 'Match highlights',
}

/** Public — viewable without logging in (FR-16). Deliberately does not use useApi/Clerk auth. */
export function SharedClipPage() {
  const { shareToken } = useParams<{ shareToken: string }>()
  const [clip, setClip] = useState<SharedClip | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!shareToken) return
    fetch(`${API_BASE_URL}/api/shared-clips/${encodeURIComponent(shareToken)}`)
      .then((res) => {
        if (!res.ok) throw new Error('This clip could not be found, or the link has expired.')
        return res.json()
      })
      .then(setClip)
      .catch((err) => setError(err.message))
  }, [shareToken])

  if (error) {
    return (
      <section className="page">
        <p className="error">{error}</p>
      </section>
    )
  }

  if (!clip) {
    return (
      <section className="page">
        <p>Loading…</p>
      </section>
    )
  }

  return (
    <section className="page">
      <h1>{TITLES[clip.type]}</h1>
      {clip.status === 'Done' && clip.playbackUrl ? (
        <>
          <video className="match-video" controls playsInline preload="metadata" src={clip.playbackUrl} />
          {clip.expiresAt && <p className="clip__meta">This link works until {formatDate(clip.expiresAt)}.</p>}
        </>
      ) : clip.status === 'Failed' ? (
        <p className="error">This clip could not be generated.</p>
      ) : (
        <p>This clip is still being prepared. Check back shortly.</p>
      )}
    </section>
  )
}
