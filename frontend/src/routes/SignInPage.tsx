import { SignIn } from '@clerk/clerk-react'

export function SignInPage() {
  return (
    <section className="page page--centered">
      <SignIn signUpUrl="/sign-up" />
    </section>
  )
}
