import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL

interface SharedClip {
  id: string
  type: string
  status: string
  playbackUrl: string | null
}

/** Public — viewable without logging in (FR-16). Deliberately does not use useApi/Clerk auth. */
export function SharedClipPage() {
  const { shareToken } = useParams<{ shareToken: string }>()
  const [clip, setClip] = useState<SharedClip | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!shareToken) return
    fetch(`${API_BASE_URL}/api/shared-clips/${shareToken}`)
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
      <h1>{clip.type}</h1>
      {clip.status !== 'Done' ? (
        <p>This clip is still processing (status: {clip.status}). Check back shortly.</p>
      ) : clip.playbackUrl ? (
        <video controls src={clip.playbackUrl} style={{ width: '100%' }} />
      ) : (
        <p>Playback URL unavailable.</p>
      )}
    </section>
  )
}
