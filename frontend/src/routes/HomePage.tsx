import { SignedIn, SignedOut } from '@clerk/clerk-react'
import { Link } from 'react-router-dom'

export function HomePage() {
  return (
    <section className="page">
      <h1>Football Highlights</h1>
      <p>
        Upload self-filmed match footage, tag goals, assists, saves and fouls, and turn them into
        shareable highlight reels — no expensive hardware or subscriptions required.
      </p>
      <SignedIn>
        <Link className="button" to="/matches">
          Go to your matches
        </Link>
      </SignedIn>
      <SignedOut>
        <Link className="button" to="/sign-in">
          Sign in to get started
        </Link>
      </SignedOut>
    </section>
  )
}
