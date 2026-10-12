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
| Object storage | Cloudflare R2 (S3-compatible), browser uploads straight to it via presigned multipart URLs |

## Repo layout

```
backend/
  src/
    FootballApp.Domain/          entities, enums — no dependencies
    FootballApp.Application/     use cases (MediatR commands/queries), interfaces
    FootballApp.Infrastructure/  EF Core, Clerk user resolution, storage/queue implementations
    FootballApp.Api/             Minimal API endpoints, JWT bearer auth, composition root
  tests/
    FootballApp.Application.Tests/  xUnit tests for the use cases (EF InMemory + fake storage)
frontend/
  src/
    routes/                      pages (matches list, match detail/tagging, sign in/up, shared clip)
    components/                  Navbar, ProtectedRoute, VideoUpload
    lib/                         typed API client (useApi), resumable video uploader, shared types
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

Video storage needs a Cloudflare R2 bucket (see [Video upload & R2](#video-upload--r2) below).
Keys go in user-secrets, never in `appsettings`:

```bash
cd backend/src/FootballApp.Api
dotnet user-secrets set "R2:ServiceUrl" "https://<account-id>.eu.r2.cloudflarestorage.com"
dotnet user-secrets set "R2:BucketName" "<bucket>"
dotnet user-secrets set "R2:AccessKeyId" "<r2-access-key-id>"
dotnet user-secrets set "R2:SecretAccessKey" "<r2-secret-access-key>"
dotnet run
```

Without R2 configured the API still starts; only the video endpoints fail.

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

## Video upload & R2

The browser uploads the raw match video **directly to R2** as an S3 multipart upload (FR-6, FR-7);
the API only signs URLs and never handles video bytes:

1. `POST /api/matches/{id}/upload` starts the upload (or resumes it for the same file name + size)
   and returns the part size plus the parts R2 already has.
2. For each missing part the client asks `POST /api/matches/{id}/upload/parts/{n}` for a presigned
   PUT URL (valid 1 h) and PUTs that slice of the file. Up to 4 parts at a time, with retries;
   time offline doesn't count against the retries.
3. `POST /api/matches/{id}/upload/complete` checks the part list *from R2* against the declared size
   and assembles the file. The match moves to `Uploaded`.

Pause = abort the in-flight PUTs. Resume after a pause, a dropped connection or a page reload means
choosing the same file again, and only the missing parts are sent. `DELETE /api/matches/{id}/upload`
cancels. `GET /api/matches/{id}/video` returns a 4-hour signed playback URL for the owner.

This replaces the tus/`tusdotnet` plan from the requirements: with tus every byte would go through the
API container's disk and then get uploaded to R2 a second time. That's fragile for 15 GB files on
scale-to-zero Container Apps with small ephemeral disks.

R2 bucket setup:

- Create the bucket with the **EU jurisdiction** (NFR-1) and keep it private (no public access /
  r2.dev URL — NFR-5). Create an R2 API token with *Object Read & Write* on that bucket only.
- Add a CORS policy so the browser may PUT parts and play the video:

  ```json
  [
    {
      "AllowedOrigins": ["http://localhost:5173"],
      "AllowedMethods": ["PUT", "GET", "HEAD"],
      "AllowedHeaders": ["*"],
      "MaxAgeSeconds": 3600
    }
  ]
  ```

- R2 aborts unfinished multipart uploads after 7 days by default, so abandoned uploads clean
  themselves up.

## What's implemented vs. stubbed

Implemented: Clerk sign-in/up, JWT-authenticated Minimal API, Match/Tag/Clip domain model and
CRUD-ish endpoints enforcing per-user ownership (FR-5), EF Core migrations, resumable direct-to-R2
video upload with progress (FR-6–FR-8), signed playback of the uploaded video, a tagging UI, and
the public unauthenticated share-link page (FR-16).

Still stubbed:

- **`IClipProcessingQueue`** (`Infrastructure/Processing/ClipProcessingQueue.cs`) — currently just
  logs; swap in Hangfire enqueuing a job for the Dockerized ffmpeg worker (FR-14). The worker is
  also where the uploaded video gets probed (duration) and moved from `Uploaded` to `Ready`.
- The ffmpeg worker itself is not in this repo yet.

## Tests

```bash
cd backend
dotnet test
```
