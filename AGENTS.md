# AGENTS.md — Football Highlights App

## Typ projektu

Webová aplikácia (PWA) pre amatérskych futbalistov, trénerov a malé kluby. Používateľ nahrá
vlastné video zápasu, manuálne si otaguje momenty (gól, asistencia, zákrok, faul) a vygeneruje
zdieľateľné highlight klipy / zostrihy. Zdroj pravdy pre požiadavky je
`football-app-requirements.md` (odkazuj sa na čísla FR-x / NFR-x).

Mimo rozsahu MVP (nerob to, kým sa Matúš nepýta): automatická AI/ML detekcia highlightov,
štatistiky hráčov, natívna mobilná appka, viac kamier, klubový/tímový management nad rámec
vlastníctva zápasu.

## Ako sa na projekte pracuje

- Matúš kód len **reviewuje**, programuje Claude. Implementuj funkčné riešenia celé, bez
  `// TODO` značiek a bez nedokončených kostier.
- Pracuj na feature branchi a otvor PR; do `main` nepushuj priamo.
- Žiadny `Co-Authored-By` trailer v git commitoch. Commit message stručná, v angličtine.
- S Matúšom komunikuj po slovensky a stručne. Komentáre v kóde píš po anglicky a len tam, kde je
  dôvod nezrejmý.
- Issues a PR popisy píš po slovensky, jednoducho, bez žargónu: prvý riadok `Verzia: <číslo>
  (<názov>)`, potom `Aktuálne správanie` a `Požadované správanie`.
- Drž sa zadanej úlohy. Pred-existujúci bug alebo bezpečnostnú dieru mimo zadania nahlás slovom,
  potichu ju neopravuj.
- Pragmatický prístup: jednoduché riešenia, free/lacné nástroje, žiadny over-engineering.

## Architektúra

Backend (`backend/`) je ASP.NET Core Minimal API v Clean Architecture. Závislosti idú len
smerom dnu:

- `FootballApp.Domain` — entity a enumy, žiadne závislosti.
- `FootballApp.Application` — use case-y ako MediatR commands/queries, rozhrania
  (`IApplicationDbContext`, `ICurrentUserService`, `IVideoStorageService`,
  `IClipProcessingQueue`, `IVideoProcessingQueue`, `IVideoProcessor`). Závisí len na Domain.
- `FootballApp.Infrastructure` — EF Core (PostgreSQL), implementácie rozhraní, migrácie, Hangfire
  fronta a volanie ffmpeg/ffprobe (`Processing/`).
- `FootballApp.Api` — tenké endpointy (`Endpoints/`), JWT auth, composition root. Endpoint len
  pošle command/query cez MediatR, žiadna logika.
- `FootballApp.Worker` — samostatný kontajner s Hangfire serverom a ffmpeg (vlastný
  `Dockerfile`). Žiadne HTTP, žiadna logika: úloha len pošle command cez MediatR.

Nová funkcia = command/query v Application + endpoint v Api. Infrastructure nikdy neodkazuj z
Application.

### Spracovanie videa na pozadí

- API úlohy len **zaraďuje** (`IClipProcessingQueue`, `IVideoProcessingQueue`), ffmpeg púšťa iba
  worker. Fronta je Hangfire v tom istom Postgrese (schéma `hangfire`), bez Redisu.
- Pravidlá patria do Application: výpočet úsekov a limity v `Clips/ClipRules.cs`, stavové prechody
  v `Clips/ClipLifecycle.cs` a `Matches/VideoProcessing/VideoLifecycle.cs`. ffmpeg/ffprobe sa volá
  len cez `IVideoProcessor`.
- Commands pre worker (`ProcessUploadedVideo`, `RenderClip`, `FailClip`,
  `FailVideoProcessing`) **nemajú kontrolu vlastníka** — nikdy ich nevystavuj cez endpoint.
- Každá úloha musí zniesť opakované spustenie: hotový klip/video sa nemení, výstup ide vždy pod
  ten istý kľúč (`matches/{matchId}/clips/{clipId}.mp4`).
- API úlohu zaradí **pred** uložením stavu do DB (keby uloženie zlyhalo, úloha nič nenájde a
  skončí). Opačné poradie by nechalo klip navždy v `Queued`.
- Chybové hlášky pre používateľa (`FailureReason`) nesmú obsahovať výstup ffmpeg ani podpísané
  URL; do logov ide výstup s vymazaným query stringom.

Frontend (`frontend/`) je Vite + React + TypeScript. Stránky v `routes/`, zdieľané komponenty v
`components/`. Všetky volania API idú cez `lib/useApi.ts` (pridáva Clerk token); jediná
výnimka je verejná stránka `SharedClipPage` (bez prihlásenia, FR-16).

### Autentifikácia a oprávnenia

- Identitu rieši výhradne **Clerk**. Backend len overuje JWT (JWT Bearer proti Clerk JWKS,
  `Clerk:Authority`). Žiadny vlastný login, heslá ani session kód (FR-2, FR-3).
- V DB **nie je tabuľka používateľov**. Vlastník zápasu je `Match.OwnerId` = Clerk `sub`
  (string). Aktuálneho používateľa čítaj cez `ICurrentUserService`.
- Vlastníctvo (FR-5) sa vynucuje na serveri: každý handler, ktorý siaha na zápas/tag/klip,
  načíta zápas a porovná `OwnerId` s `currentUser.UserId`, inak `ForbiddenAccessException`.
  Nespoliehaj sa na skrývanie v UI.
- Role zatiaľ nie sú rozhodnuté (možnosti: Clerk Organizations pre kluby, alebo `publicMetadata`
  ako jednoduchý label). Nič z toho neimplementuj bez opýtania sa.

### Súkromie a bezpečnosť

- Žiadne rozpoznávanie tvárí ani biometria (NFR-2); tagovanie je manuálne.
- Surové video je súkromné; von idú len podpísané, expirujúce URL, nikdy verejný bucket (NFR-5).
- Dáta a R2 bucket v EÚ regióne (NFR-1).
- Do repa nikdy nedávaj tajomstvá (Clerk kľúče, R2 kľúče, connection stringy). Konfigurácia ide
  cez user-secrets / env premenné; `.env` je v `.gitignore`.

## Aktuálny stav

Hotové: Clerk sign-in/up, JWT-chránené API, doménový model Match/Tag/Clip, endpointy s kontrolou
vlastníctva, EF Core migrácie, UI na zápasy a tagovanie, verejná stránka zdieľaného klipu.
Nahrávanie videa (FR-6 až FR-8): prehliadač nahráva priamo do R2 cez S3 multipart s
podpísanými URL pre jednotlivé časti (`lib/videoUpload.ts`, `Matches/Commands/*VideoUpload`,
`R2VideoStorageService`). API bajty videa nikdy nedrží. **tus/`tusdotnet` sa nepoužíva** (dôvod
je v README). Podpísaná URL na prehrávanie nahraného videa.

Generovanie klipov (FR-13 až FR-16): po nahratí worker zistí dĺžku videa (`Uploaded → Ready`,
pri chybe `Failed` s dôvodom). Klip alebo zostrih sa dá vytvoriť len pre zápas v stave `Ready`;
worker ho vystrihne cez podpísanú URL (ffmpeg číta len potrebné úseky), uloží MP4 do R2 a nastaví
expiráciu share linku (`Clips:ShareLinkLifetime`, predvolene 30 dní). Vlastník si klip stiahne cez
`GET /api/clips/{id}/download`. Stránka zápasu obnovuje stav, kým sa niečo spracúva.

Známe obmedzenia: zápas vo `Failed` sa nedá opraviť (treba založiť nový), share link sa nedá
predĺžiť ani zrušiť, zmazanie tagu použitého v klipe skončí chybou 500.

Chýba:

- video.js a hotkeys pri tagovaní (FR-10; zatiaľ je tam obyčajný `<video>`), úprava tagov
  (FR-12), stránky Privacy Policy / Terms / takedown kontakt (FR-17, FR-19, FR-20), PWA manifest.
- Dockerfile pre API, GitHub Actions CI, nasadenie na Azure. Worker s Hangfire serverom stále
  polluje DB, takže na Container Apps sám od seba neklesne na nulu (NFR-4) — treba KEDA pravidlo
  nad počtom úloh v `hangfire.jobqueue` alebo Container Apps Job.
- Nič z ukladania videa nebolo overené proti skutočnému Cloudflare R2, len proti MinIO.

Testy: `backend/tests/FootballApp.Application.Tests` (xUnit v3, EF InMemory, fake storage, fronta
a ffmpeg) a `backend/tests/FootballApp.Infrastructure.Tests` (púšťa skutočný ffmpeg; bez ffmpeg na
PATH sa preskočia, pozri README).

## Príkazy

```bash
# Postgres, MinIO (namiesto R2) a worker pre lokálny vývoj
docker compose up -d
docker compose up -d --build worker   # po zmene backend kódu

# Backend (z backend/)
dotnet build
dotnet test
dotnet run --project src/FootballApp.Api
dotnet ef database update --project src/FootballApp.Infrastructure --startup-project src/FootballApp.Infrastructure
dotnet ef migrations add <Name> --project src/FootballApp.Infrastructure --startup-project src/FootballApp.Infrastructure -o Persistence/Migrations

# Frontend (z frontend/)
npm install
npm run dev
npm run build   # tsc + vite build
npm run lint    # oxlint
```

Konfigurácia: `Clerk:Authority`, `ConnectionStrings:Postgres` a `R2:*` (backend aj worker; v
`appsettings.Development.json` ukazuje `R2` na lokálne MinIO s jeho verejne známym dev heslom,
skutočné R2 kľúče idú len cez user-secrets / env premenné), `Clips:ShareLinkLifetime` a
`Processing:*` (worker), `VITE_CLERK_PUBLISHABLE_KEY` a `VITE_API_BASE_URL` (frontend, vzor v
`frontend/.env.example`). Pred odovzdaním práce over, že `dotnet build`, `dotnet test`,
`npm run build` aj `npm run lint` prejdú bez chýb.
