import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { VideoUpload } from '../components/VideoUpload'
import { useApi } from '../lib/useApi'
import type { Clip, Match, Tag, TagType } from '../lib/types'

const TAG_TYPES: TagType[] = ['Goal', 'Assist', 'Save', 'Foul', 'Other']

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

  async function handleAddTag(event: React.FormEvent) {
    event.preventDefault()
    if (!matchId) return
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
        <button type="button" disabled={selectedTagIds.length === 0} onClick={handleGenerateClip}>
          Generate {selectedTagIds.length > 1 ? 'highlight reel' : 'clip'} from selected tags
        </button>
      </div>

      <div className="card">
        <h2>Clips</h2>
        {clips.length === 0 ? (
          <p>No clips generated yet.</p>
        ) : (
          <ul className="clip-list">
            {clips.map((clip) => (
              <li key={clip.id}>
                {clip.type} — <span className={`status status--${clip.status.toLowerCase()}`}>{clip.status}</span>
                {clip.status === 'Done' && (
                  <span> — share link: /shared/{clip.shareToken}</span>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  )
}
