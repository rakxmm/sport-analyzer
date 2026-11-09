import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { ClipList } from '../components/ClipList'
import { VideoUpload } from '../components/VideoUpload'
import { useApi } from '../lib/useApi'
import type { Clip, Match, Tag, TagType } from '../lib/types'

const TAG_TYPES: TagType[] = ['Goal', 'Assist', 'Save', 'Foul', 'Other']
const POLL_INTERVAL_MS = 3000

export function MatchDetailPage() {
  const { matchId } = useParams<{ matchId: string }>()
  const api = useApi()

  const [match, setMatch] = useState<Match | null>(null)
  const [tags, setTags] = useState<Tag[]>([])
  const [clips, setClips] = useState<Clip[]>([])
  const [selectedTagIds, setSelectedTagIds] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)

  const [tagType, setTagType] = useState<TagType>('Goal')
  const [timestampSeconds, setTimestampSeconds] = useState(0)

  const [videoUrl, setVideoUrl] = useState<string | null>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const hasVideo = match !== null && match.videoStatus !== 'PendingUpload'

  // The worker is still looking at the uploaded video, or still cutting a clip.
  const videoProcessing = match?.videoStatus === 'Uploaded' || match?.videoStatus === 'Processing'
  const clipsPending = clips.some((clip) => clip.status === 'Queued' || clip.status === 'Processing')
  const videoReady = match?.videoStatus === 'Ready'

  useEffect(() => {
    if (!matchId) return
    Promise.all([api.getMatch(matchId), api.getTags(matchId), api.getClips(matchId)])
      .then(([m, t, c]) => {
        setMatch(m)
        setTags(t)
        setClips(c)
      })
      .catch((err) => setError(err.message))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [matchId])

  useEffect(() => {
    if (!matchId || !hasVideo) return
    api
      .getMatchVideoUrl(matchId)
      .then((video) => setVideoUrl(video.url))
      .catch((err) => setError(err.message))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [matchId, hasVideo])

  // Refresh statuses while something is still being processed (FR-14); stops by itself once all is settled.
  useEffect(() => {
    if (!matchId || (!videoProcessing && !clipsPending)) return

    let cancelled = false
    let timer = 0

    const poll = async () => {
      try {
        const [latestMatch, latestClips] = await Promise.all([
          videoProcessing ? api.getMatch(matchId) : null,
          clipsPending ? api.getClips(matchId) : null,
        ])
        if (cancelled) return
        if (latestMatch) setMatch(latestMatch)
        if (latestClips) {
          // Keep a clip created while this request was in flight; the next poll will include it.
          const known = new Set(latestClips.map((clip) => clip.id))
          setClips((prev) => [...prev.filter((clip) => !known.has(clip.id)), ...latestClips])
        }
      } catch {
        // A failed poll (e.g. a network blip) is simply tried again on the next tick.
      }
      if (!cancelled) timer = window.setTimeout(poll, POLL_INTERVAL_MS)
    }

    timer = window.setTimeout(poll, POLL_INTERVAL_MS)
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [matchId, videoProcessing, clipsPending])

  async function handleAddTag(event: React.FormEvent) {
    event.preventDefault()
    if (!matchId) return
    setError(null)
    try {
      const tag = await api.createTag(matchId, {
        type: tagType,
        timestampSeconds,
        leadInSeconds: 5,
        leadOutSeconds: 3,
      })
      setTags((prev) => [...prev, tag].sort((a, b) => a.timestampSeconds - b.timestampSeconds))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add tag')
    }
  }

  async function handleDeleteTag(tagId: string) {
    setError(null)
    try {
      await api.deleteTag(tagId)
      setTags((prev) => prev.filter((t) => t.id !== tagId))
      setSelectedTagIds((prev) => prev.filter((id) => id !== tagId))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete tag')
    }
  }

  function toggleTagSelection(tagId: string) {
    setSelectedTagIds((prev) => (prev.includes(tagId) ? prev.filter((id) => id !== tagId) : [...prev, tagId]))
  }

  async function handleGenerateClip() {
    if (!matchId || selectedTagIds.length === 0) return
    setError(null)
    try {
      const type = selectedTagIds.length === 1 ? 'SingleClip' : 'HighlightReel'
      const clip = await api.createClip(matchId, type, selectedTagIds)
      setClips((prev) => [clip, ...prev])
      setSelectedTagIds([])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to generate clip')
    }
  }

  if (!match) {
    return <section className="page">{error ? <p className="error">{error}</p> : <p>Loading…</p>}</section>
  }

  return (
    <section className="page">
      <h1>{match.title}</h1>
      <p>
        {match.matchDate} — <span className={`status status--${match.videoStatus.toLowerCase()}`}>{match.videoStatus}</span>
      </p>

      {match.videoStatus === 'PendingUpload' && <VideoUpload match={match} onMatchChange={setMatch} />}

      {videoProcessing && (
        <p className="notice">The video is uploaded and is being checked. Clips can be generated in a moment.</p>
      )}

      {match.videoStatus === 'Failed' && (
        <p className="error">{match.videoFailureReason ?? 'The video could not be processed.'}</p>
      )}

      {hasVideo && videoUrl && (
        <div className="card">
          <video ref={videoRef} className="match-video" controls preload="metadata" src={videoUrl} />
        </div>
      )}

      {error && <p className="error">{error}</p>}

      <div className="card">
        <h2>Tag an event</h2>
        <form className="tag-form" onSubmit={handleAddTag}>
          <select value={tagType} onChange={(e) => setTagType(e.target.value as TagType)}>
            {TAG_TYPES.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>
          <input
            type="number"
            min={0}
            max={match.durationSeconds ?? undefined}
            value={timestampSeconds}
            onChange={(e) => setTimestampSeconds(Number(e.target.value))}
          />
          <span>seconds</span>
          {videoUrl && (
            <button
              type="button"
              onClick={() => setTimestampSeconds(Math.floor(videoRef.current?.currentTime ?? 0))}
            >
              Use video time
            </button>
          )}
          <button type="submit">Add tag</button>
        </form>
      </div>

      <div className="card">
        <h2>Tags</h2>
        {tags.length === 0 ? (
          <p>No tags yet.</p>
        ) : (
          <ul className="tag-list">
            {tags.map((tag) => (
              <li key={tag.id}>
                <label>
                  <input
                    type="checkbox"
                    checked={selectedTagIds.includes(tag.id)}
                    onChange={() => toggleTagSelection(tag.id)}
                  />
                  {tag.type} @ {tag.timestampSeconds}s
                </label>
                <button type="button" onClick={() => handleDeleteTag(tag.id)}>
                  Delete
                </button>
              </li>
            ))}
          </ul>
        )}
        <button type="button" disabled={!videoReady || selectedTagIds.length === 0} onClick={handleGenerateClip}>
          Generate {selectedTagIds.length > 1 ? 'highlight reel' : 'clip'} from selected tags
        </button>
        {!videoReady && <p className="clip__meta">Clips can be generated once the match video is ready.</p>}
      </div>

      <div className="card">
        <h2>Clips</h2>
        <ClipList clips={clips} />
      </div>
    </section>
  )
}
