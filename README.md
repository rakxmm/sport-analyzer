# Football Highlights App

Grassroots football players, coaches and small clubs upload self-filmed match footage, tag key
moments (goals, assists, saves, fouls), and generate/share highlight clips — see
`football-app-requirements.md` for the full spec this scaffold implements against.

## Stack

| Layer | Choice |
|---|---|
| Frontend | React + TypeScript (Vite), [Clerk](https://clerk.com) for auth |
| Backend API | ASP.NET Core Minimal API, Clean Architecture, validates Clerk JWTs |
| Database | PostgreSQL via EF Core |
| Object storage | Cloudflare R2 (stubbed — see below) |

## Repo layout

```
backend/
  src/
    FootballApp.Domain/          entities, enums — no dependencies
    FootballApp.Application/     use cases (MediatR commands/queries), interfaces
    FootballApp.Infrastructure/  EF Core, Clerk user resolution, storage/queue implementations
    FootballApp.Api/             Minimal API endpoints, JWT bearer auth, composition root
frontend/
  src/
    routes/                      pages (matches list, match detail/tagging, sign in/up, shared clip)
    components/                  Navbar, ProtectedRoute
    lib/                         typed API client (useApi), shared types
docker-compose.yml                local Postgres for dev
```

The Application layer only depends on Domain; Infrastructure and Api depend inward per Clean
Architecture. Swap `FootballApp.Infrastructure/Storage/R2VideoStorageService.cs` or
`Processing/ClipProcessingQueue.cs` for real implementations without touching Application or Api.

## Prerequisites

- .NET 10 SDK
- Node 20+
- Docker (for local Postgres), or a Postgres connection string of your own
- A [Clerk](https://clerk.com) application (free tier is fine for dev)

## Setup

### 1. Database

```bash
docker compose up -d postgres
```

```bash
cd backend
dotnet tool install --global dotnet-ef   # if not already installed
dotnet ef database update --project src/FootballApp.Infrastructure --startup-project src/FootballApp.Infrastructure
```

### 2. Backend

Set `Clerk:Authority` in `backend/src/FootballApp.Api/appsettings.Development.json` (or via
`dotnet user-secrets`) to your Clerk instance's issuer, e.g.
`https://your-app.clerk.accounts.dev` — found in the Clerk dashboard under **API Keys** /
**JWT templates**.

```bash
cd backend/src/FootballApp.Api
dotnet run
```

The API validates the Clerk-issued JWT on every request via JWT Bearer + Clerk's JWKS endpoint —
no custom auth/session code (`FootballApp.Infrastructure/Auth/CurrentUserService.cs` just reads
the `sub` claim). `GET /health` is unauthenticated and useful for a smoke test.

### 3. Frontend

```bash
cd frontend
cp .env.example .env   # fill in VITE_CLERK_PUBLISHABLE_KEY from the Clerk dashboard
npm install
npm run dev
```

## What's scaffolded vs. stubbed

Implemented: Clerk sign-in/up, JWT-authenticated Minimal API, Match/Tag/Clip domain model and
CRUD-ish endpoints enforcing per-user ownership (FR-5), EF Core + initial migration, a tagging UI,
and the public unauthenticated share-link page (FR-16).

Deliberately stubbed as interfaces ready to implement (see the requirements doc for why each one
needs a real backend and can't be client-only):

- **`IVideoStorageService`** (`Infrastructure/Storage/R2VideoStorageService.cs`) — wire up the AWS
  S3 SDK against Cloudflare R2's S3-compatible endpoint for presigned upload/download URLs.
- **tus resumable upload** (FR-6) — no `tusdotnet` endpoint or `tus-js-client`/Uppy integration
  yet; the match-detail page has a placeholder where the upload widget goes.
- **`IClipProcessingQueue`** (`Infrastructure/Processing/ClipProcessingQueue.cs`) — currently just
  logs; swap in Hangfire enqueuing a job for the Dockerized ffmpeg worker (FR-14).
- The ffmpeg worker itself is not in this repo yet.

## Tests

None yet — `backend/tests/` is reserved for them.
