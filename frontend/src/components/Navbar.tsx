import { SignedIn, SignedOut, UserButton } from '@clerk/clerk-react'
import { Link } from 'react-router-dom'

export function Navbar() {
  return (
    <header className="navbar">
      <Link to="/" className="navbar__brand">
        Football Highlights
      </Link>
      <nav className="navbar__links">
        <SignedIn>
          <Link to="/matches">Matches</Link>
          <UserButton />
        </SignedIn>
        <SignedOut>
          <Link to="/sign-in">Sign in</Link>
        </SignedOut>
      </nav>
    </header>
  )
}
