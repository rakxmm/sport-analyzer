# Football Highlights App — Requirements Specification

## 1. Overview
A web application (PWA) that lets grassroots football players, coaches, and small clubs upload self-filmed match footage, tag key moments (goals, assists, saves, fouls), and generate/share highlight clips and reels — without expensive hardware or subscriptions.

## 2. Tech Stack & Architecture Decision

**Question: can this be built as a pure React app, with no separate backend (e.g. ASP.NET)?**

No. A server-side backend is required no matter which frontend framework is used, because:
- Match/tag/clip data needs a persistent database (Postgres), queried through a real data/API layer — not just client-side state.
- The tus resumable-upload protocol needs a server implementation to receive, track, and resume chunked uploads.
- Highlight-reel generation runs ffmpeg on the server (in a Dockerized worker) — this cannot run reliably in the browser for full-length matches (client-side ffmpeg.wasm caps out around ~100–200 MB files).
- Signed, time-limited URLs to R2 storage must be issued server-side, so raw video stays private by default instead of sitting on a public bucket URL.
- Authorization (who can see/manage which club's matches) must be enforced server-side against the identity provider's token — a client-only check can be bypassed by anyone reading the browser's network requests.

**Decision: keep ASP.NET Core (Minimal API) as the backend**, with React + TypeScript as the frontend, and **Clerk** for authentication. Clerk handles all identity UI/flows; the ASP.NET Core backend does zero custom auth work — it just validates the JWT Clerk issues, on every request, the same pattern already used on the TaxiSaas project. No new auth code, no password storage, no session management to build.

| Layer | Choice |
|---|---|
| Frontend | React + TypeScript, PWA |
| Auth | **Clerk** (`@clerk/clerk-react` on the frontend) |
| Backend API | ASP.NET Core Minimal API — validates Clerk JWTs via JWT Bearer middleware + Clerk's JWKS endpoint |
| Database | PostgreSQL, accessed via EF Core |
| File upload | tus protocol — `tusdotnet` (server) + `tus-js-client` / Uppy (client) |
| Video processing | Dockerized worker running ffmpeg, triggered by a background job (Hangfire to start) |
| Object storage | Cloudflare R2 (S3-compatible, zero egress) |
| Compute hosting | Azure Container Apps (API + worker), using Azure for Students credit |

## 3. Functional Requirements

### 3.1 Authentication & Authorization (Clerk)
- **FR-1**: Users sign up / sign in via Clerk's hosted components (email/password and/or social login).
- **FR-2**: Clerk handles all identity concerns — registration, login, session management, password reset, optional 2FA. No custom auth code in this app.
- **FR-3**: The backend validates the Clerk-issued JWT on every API request (JWT Bearer auth against Clerk's JWKS) and reads the user ID from its claims. This app's own database never stores a password or session token.
- **FR-4**: Only account holders can create/manage content — coaches, parents, or club admins hold accounts, not the players directly. This also keeps the app clear of GDPR Article 8's under-16 signup-consent complications (see the legal report).
- **FR-5**: A user can only see and manage matches/clips they uploaded, or that belong to their club/team (enforced server-side against the Clerk user ID, not just hidden in the UI).

### 3.2 Video Upload
- **FR-6**: User selects a video file and uploads it via resumable/chunked upload (tus); the upload can pause, resume, and survive a dropped connection without restarting from zero.
- **FR-7**: Upload progress is shown in the UI.
- **FR-8**: On upload completion, the raw video is moved to R2 storage and a processing job is queued.
- **FR-9**: A mandatory, unchecked-by-default consent checkbox must be confirmed at upload time (see 3.5).

### 3.3 Tagging
- **FR-10**: User plays back the uploaded video in-browser (video.js) and tags events (goal, assist, save, foul — configurable list) at specific timestamps via a button or hotkey.
- **FR-11**: Each tag has a configurable lead-in/lead-out (e.g. -5s/+3s) used when a clip is generated from it.
- **FR-12**: Tags are editable and deletable before export.

### 3.4 Clip & Highlight Generation
- **FR-13**: User can generate an individual clip from one tag, or a combined highlight reel from a selection of tags.
- **FR-14**: Generation runs asynchronously on the ffmpeg worker; the UI shows processing status (queued / processing / done / failed).
- **FR-15**: Finished clips/reels are stored on R2 and made available via a signed, time-limited shareable link.
- **FR-16**: A shared link is viewable without logging in; the owner can also download the clip/reel.

### 3.5 Legal & Consent (minimum safeguards)
- **FR-17**: Terms of Service state that the uploader is solely responsible for their content and warrants they hold all necessary consents (including parental consent for minors) to film and publish it.
- **FR-18**: Upload-time consent attestation checkbox, logged with the uploader's user ID and a timestamp.
- **FR-19**: A Privacy Policy page (what's stored, why, for how long, which sub-processors — Clerk, R2, hosting — and where they're located).
- **FR-20**: A visible contact method / takedown-request process for anyone wanting footage removed.

## 4. Non-Functional Requirements
- **NFR-1** (Data residency): Database and R2 bucket hosted in an EU region.
- **NFR-2** (Privacy/GDPR): No facial recognition or biometric processing anywhere in the pipeline — tagging stays manual for the MVP.
- **NFR-3** (Reliability): Uploads of files up to at least ~15 GB must reliably complete over an unstable connection, resuming after drops (tus).
- **NFR-4** (Cost): MVP infrastructure cost target: under ~€10/month at 10–30 matches/month, following the R2 + Azure Container Apps (scale-to-zero) model.
- **NFR-5** (Security): Raw video is private by default; only signed/expiring URLs are ever shared externally, never a public bucket URL.
- **NFR-6** (Maintainability): Backend and worker are independently deployable Docker containers with GitHub Actions CI, matching the deployment pattern already used on the Safe & Travel project.

## 5. Out of Scope for MVP (Later)
- Automatic event/highlight detection (AI/ML-based)
- Per-player statistics/analytics
- Native mobile app (the PWA covers mobile-browser use for the MVP)
- Multi-camera-angle support
- Club/team management beyond basic ownership of matches
