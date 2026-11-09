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
| Background jobs | Hangfire with its queue in the same Postgres (no Redis), run by a separate ffmpeg worker container |

## Repo layout

```
backend/
  src/
    FootballApp.Domain/          entities, enums — no dependencies
    FootballApp.Application/     use cases (MediatR commands/queries), interfaces, clip rules
    FootballApp.Infrastructure/  EF Core, Clerk user resolution, R2 storage, Hangfire queue, ffmpeg
    FootballApp.Api/             Minimal API endpoints, JWT bearer auth, composition root
    FootballApp.Worker/          background worker (Hangfire server + ffmpeg), own Dockerfile
  tests/
    FootballApp.Application.Tests/     xUnit tests for the use cases (EF InMemory + fakes)
    FootballApp.Infrastructure.Tests/  integration tests that run the real ffmpeg
frontend/
  src/
    routes/                      pages (matches list, match detail/tagging, sign in/up, shared clip)
    components/                  Navbar, ProtectedRoute, VideoUpload, ClipList
    lib/                         typed API client (useApi), resumable video uploader, shared types
docker-compose.yml               local Postgres, MinIO (stands in for R2) and the worker
```

The Application layer only depends on Domain; Infrastructure, Api and Worker depend inward per Clean
Architecture. The API and the worker are two separate deployables that share Application and
Infrastructure: the API only puts jobs on the queue, the worker is the only one that runs ffmpeg.

## Prerequisites

- .NET 10 SDK
- Node 20+
- Docker (local Postgres, MinIO and the worker)
- A [Clerk](https://clerk.com) application (free tier is fine for dev)

No Cloudflare account is needed for local development — MinIO plays the role of R2.

## Setup

### 1. Postgres, MinIO and the worker

```bash
docker compose up -d
```

This starts Postgres (`localhost:5432`), MinIO (S3 API on `localhost:9000`, web console on
`localhost:9001`, login `minioadmin` / `minioadmin`, bucket `football-app-dev` created automatically)
and builds and starts the worker. After changing backend code, rebuild the worker:

```bash
docker compose up -d --build worker
```

Then create the tables:

```bash
cd backend
dotnet tool install --global dotnet-ef   # if not already installed
dotnet ef database update --project src/FootballApp.Infrastructure --startup-project src/FootballApp.Infrastructure
```

Hangfire creates its own `hangfire` schema in the same database the first time it is used.

### 2. Backend

Set `Clerk:Authority` in `backend/src/FootballApp.Api/appsettings.Development.json` (or via
`dotnet user-secrets`) to your Clerk instance's issuer, e.g.
`https://your-app.clerk.accounts.dev` — found in the Clerk dashboard under **API Keys** /
**JWT templates**.

```bash
cd backend/src/FootballApp.Api
dotnet run
```

In Development the API stores video in the local MinIO (see `R2` in `appsettings.Development.json`).
To use a real Cloudflare R2 bucket instead (see [Video upload & R2](#video-upload--r2)), put its keys
in user-secrets, never in `appsettings`:

```bash
cd backend/src/FootballApp.Api
dotnet user-secrets set "R2:ServiceUrl" "https://<account-id>.eu.r2.cloudflarestorage.com"
dotnet user-secrets set "R2:BucketName" "<bucket>"
dotnet user-secrets set "R2:AccessKeyId" "<r2-access-key-id>"
dotnet user-secrets set "R2:SecretAccessKey" "<r2-secret-access-key>"
```

The worker project shares the API's user-secrets ID, so the same four values reach a worker started
with `dotnet run`. The worker in Docker reads them from environment variables (`R2__ServiceUrl`, …).

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
3. `POST /api/matches/{id}/upload/complete` checks the part list *from R2* against the declared size,
   assembles the file and queues it for the worker. The match moves to `Uploaded`.
4. The worker reads the video's length with ffprobe and moves the match to `Ready` — or to `Failed`
   with a reason if the file is not a usable video. Clips can only be generated for a `Ready` match.

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

Everything above has only been run against MinIO so far, not against a real R2 bucket.

## Clips and highlight reels

The owner selects tags on the match page and generates a clip (one tag) or a highlight reel (several
tags, joined in the order they were selected) — FR-13 to FR-16:

1. `POST /api/matches/{id}/clips` checks ownership, that the video is `Ready` and that every tag lies
   within the video, saves the clip as `Queued` and puts a job on the Hangfire queue.
2. The worker picks the job up (`Processing`). Each tag becomes the stretch
   `[timestamp − lead-in, timestamp + lead-out]`, cut off at the start and end of the video.
   ffmpeg reads the source through a signed URL and seeks with HTTP range requests, so only the
   seconds it needs are downloaded — never the whole match.
3. The result is an MP4 (H.264/AAC, `+faststart`, at most 1080p, source metadata such as GPS position
   removed) stored in R2 under `matches/{matchId}/clips/{clipId}.mp4`. The clip becomes `Done` and its
   share link starts counting down; on an error it becomes `Failed` with a reason.
4. The match page refreshes the statuses every few seconds while anything is `Queued` or `Processing`.

A finished clip has:

- a **share link** `/shared/{token}` that plays without logging in (`GET /api/shared-clips/{token}`)
  and stops working after `Clips:ShareLinkLifetime` (default 30 days),
- a **Download** button for the owner (`GET /api/clips/{id}/download`, a short-lived signed URL that
  the browser saves as a file). The owner can download even after the share link has expired.

Limits (in `Application/Clips/ClipRules.cs`): at most 50 tags and 10 minutes per reel, lead-in and
lead-out at most 60 s each.

### Worker

`FootballApp.Worker` is a plain .NET worker that hosts the Hangfire server; its Docker image adds
ffmpeg. It needs the same `ConnectionStrings:Postgres` and `R2:*` settings as the API, plus:

| Setting | Default | Meaning |
|---|---|---|
| `Clips:ShareLinkLifetime` | `30.00:00:00` | how long a share link works after the clip is rendered |
| `Processing:WorkerCount` | `2` | jobs run at the same time (encoding is CPU-bound) |
| `Processing:FfmpegPath` / `FfprobePath` | `ffmpeg` / `ffprobe` | executables, if not on `PATH` |

To run it outside Docker (ffmpeg must be installed): `dotnet run --project backend/src/FootballApp.Worker`.

Jobs are safe to run more than once: a finished clip or video is left alone, and a repeated render
overwrites the same storage object. A failed job is retried after 15 s and again after 60 s; if the
third run fails too, the clip (or match video) is marked `Failed`. A job whose worker died is handed
out again after 5 minutes.

There is no Hangfire dashboard. To look at the queue, query the `hangfire.job` and `hangfire.state`
tables.

## What's implemented vs. missing

Implemented: Clerk sign-in/up, JWT-authenticated Minimal API, Match/Tag/Clip domain model and
endpoints enforcing per-user ownership (FR-5), EF Core migrations, resumable direct-to-R2 video upload
with progress (FR-6–FR-8), video processing after upload, signed playback of the uploaded video, a
tagging UI, clip and highlight-reel generation on the ffmpeg worker (FR-13, FR-14), expiring share
links and owner download (FR-15, FR-16).

Not done yet: video.js player and tagging hotkeys (FR-10), editing tags (FR-12), Privacy Policy /
Terms / takedown pages (FR-17, FR-19, FR-20), PWA manifest, an API Dockerfile, GitHub Actions CI and
deployment to Azure.

## Tests

```bash
cd backend
dotnet test
```

`FootballApp.Infrastructure.Tests` runs the real ffmpeg on short generated videos. Without ffmpeg and
ffprobe on `PATH` those tests are reported as skipped; set `FFMPEG_TESTS_REQUIRED=true` (e.g. in CI)
to make a missing ffmpeg fail the run instead. To run everything without installing ffmpeg locally:

```bash
docker run --rm -e FFMPEG_TESTS_REQUIRED=true -v "$PWD/backend:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash -c "apt-get update -qq && apt-get install -y -qq --no-install-recommends ffmpeg && cp -r /src /work && cd /work && find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + && dotnet test"
```
