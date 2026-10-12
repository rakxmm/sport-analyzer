import { SignUp } from '@clerk/clerk-react'

export function SignUpPage() {
  return (
    <section className="page page--centered">
      <SignUp signInUrl="/sign-in" />
    </section>
  )
}
