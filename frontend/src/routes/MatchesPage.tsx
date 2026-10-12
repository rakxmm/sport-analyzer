import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useApi } from '../lib/useApi'
import type { Match } from '../lib/types'

export function MatchesPage() {
  const api = useApi()
  const [matches, setMatches] = useState<Match[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [title, setTitle] = useState('')
  const [matchDate, setMatchDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [consentConfirmed, setConsentConfirmed] = useState(false)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    api
      .getMatches()
      .then(setMatches)
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault()
    if (!consentConfirmed) return

    setCreating(true)
    setError(null)
    try {
      const match = await api.createMatch(title, matchDate, consentConfirmed)
      setMatches((prev) => [match, ...prev])
      setTitle('')
      setConsentConfirmed(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create match')
    } finally {
      setCreating(false)
    }
  }

  return (
    <section className="page">
      <h1>Your matches</h1>

      <form className="card" onSubmit={handleCreate}>
        <h2>New match</h2>
        <label>
          Title
          <input value={title} onChange={(e) => setTitle(e.target.value)} required />
        </label>
        <label>
          Match date
          <input type="date" value={matchDate} onChange={(e) => setMatchDate(e.target.value)} required />
        </label>
        <label className="checkbox">
          <input
            type="checkbox"
            checked={consentConfirmed}
            onChange={(e) => setConsentConfirmed(e.target.checked)}
            required
          />
          I confirm I hold all necessary consents (including parental consent for minors) to film
          and publish this footage.
        </label>
        <button type="submit" disabled={creating || !consentConfirmed}>
          {creating ? 'Creating…' : 'Create match'}
        </button>
      </form>

      {error && <p className="error">{error}</p>}
      {loading ? (
        <p>Loading…</p>
      ) : matches.length === 0 ? (
        <p>No matches yet — create your first one above.</p>
      ) : (
        <ul className="match-list">
          {matches.map((match) => (
            <li key={match.id}>
              <Link to={`/matches/${match.id}`}>
                <strong>{match.title}</strong>
                <span>{match.matchDate}</span>
                <span className={`status status--${match.videoStatus.toLowerCase()}`}>
                  {match.videoStatus}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
