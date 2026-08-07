# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Readarr — an ebook and audiobook collection manager for Usenet/BitTorrent. C# / .NET 8 backend
(`src/`) plus a React + Redux frontend (`frontend/`), served as a single self-hosted web app on
port 8787.

This is a fork (`Dood345/Readarr`). Read the next section before planning any work — the upstream
project is retired and that fact drives most decisions here.

## State of the upstream project

`git log` HEAD is **"Retirement announcement"** (27 Jun 2025). Readarr was retired by the Servarr
team, and the stated reason matters more than the retirement itself:

> the project's metadata has become unusable, we no longer have the time to remake or repair it,
> and the community effort to transition to using Open Library as the source has stalled

Practical consequences:

- **The upstream metadata service is dead; this fork resolves metadata in-process instead.**
  `api.bookinfo.club` is gone and there is **no sidecar container**. `MetadataSource/OpenLibrary/`
  supplies author identity, bibliography and text editions from Open Library, and
  `MetadataSource/Audible/` adds audiobook editions, narrators and series sequence. Neither needs an
  API key. See the "Metadata providers" section below.
- **The bookinfo path is gone entirely.** `BookInfoProxy`, its resources, `MetadataRequestBuilder`,
  `MetadataOptions`/`Readarr__Metadata__Source`, `ConfigService.MetadataSource` and the
  `IReadarrCloudRequestBuilder.Metadata` factory have all been deleted, along with their fixtures.
  Nothing in the tree talks to `api.bookinfo.club` any more.
- `MetadataSource/GoodreadsSearchProxy/` **survives** — it is used by `ImportListSyncService`, not
  only by the old metadata proxy. It hardcodes `https://www.goodreads.com/book/auto_complete` with a
  spoofed browser User-Agent and has no config hook. The OpenLibrary provider does not use it.
- **The Goodreads import lists still assume Goodreads ids.** `ImportListSyncService` passes
  `report.BookGoodreadsId` to `IProvideBookInfo`, which now resolves Open Library keys, so those
  lookups cannot match. That feature was already dependent on Goodreads' retired API; it is a known
  gap rather than a regression.
- Nothing upstream will be merged back, so there is no need to keep changes upstream-shaped. This is
  the opposite of the sibling Lidarr fork (see below).

## Sibling fork

`../Liedarr` is a Lidarr fork worked on in parallel. Lidarr and Readarr are both Sonarr forks and
share most infrastructure verbatim, so fixes usually port with only entity renames
(Artist→Author, Album→Book, Track→Edition/BookFile). Two differences are load-bearing and are
called out in the sections below: Readarr uses the older `System.Data.SQLite.Core.Servarr` provider
that needs a *system* libsqlite3, and Readarr models a book as Author → Book → **Edition** →
BookFile where Lidarr has Artist → Album → Track. Both forks are now on .NET 8, and both now use
the `IDownloadProtocol` marker interface rather than an enum.

## Commands

Same shape as Lidarr/Sonarr. Node 20 (pinned via volta to 20.11.1) and Yarn via `corepack enable`.
Projects target **`net8.0`** (migrated from the EOL `net6.0`). There is deliberately **no
`global.json`**: without one the SDK 8.0 image builds it and a newer local SDK still can, whereas
pinning to 8.0.x the way Lidarr does breaks local builds on machines without that exact SDK band.

### Backend

```bash
./build.sh --all                          # backend + frontend + lint + packages
./build.sh --backend -f net8.0 -r linux-x64
dotnet msbuild -restore src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix -t:PublishAllRids
```

### Docker

`Dockerfile` (multi-stage, self-contained publish) builds the image the compose stack at
`C:\Users\dood3\dockerComPlex\docker-compose.yml` runs as `readarr:local`:

```bash
docker build -t readarr:local .          # production image
docker build --target test .             # build, then run the unit suite
```

Two things in it are load-bearing and easy to break:

- **`libsqlite3-0` must be apt-installed.** `AssemblyLoader.LoadSqliteNativeLib` maps `sqlite3` to
  the *system* `libsqlite3.so.0` on Linux and `System.Data.SQLite.Core.Servarr` 1.0.115.5-18 bundles
  no native, so without it Readarr dies on "Error creating main database". The Lidarr fork needs no
  such package because its newer provider bundles `libe_sqlite3.so` — don't copy its apt list.
- **`AssemblyVersion` must be substituted.** `Directory.Build.props` ships `10.0.0.*`, and
  `RuntimeInfo.InternalIsOfficialBuild()` treats `Major >= 10` as unofficial, which makes
  `CacheableSpecification` mark every response no-cache and re-download the UI bundle on each page
  load.

Output goes to `_output/`, tests to `_tests/`, intermediates to `_temp/`, packages to `_artifacts/`.
`build.sh` accepts `--all --backend --frontend --packages --lint --installer
--enable-extra-platforms-in-sdk`.

### Frontend

```bash
yarn install
yarn start                      # webpack --watch into _output/UI
yarn build
yarn lint --fix
yarn stylelint-windows --fix    # stylelint-linux elsewhere
```

### Tests

NUnit. `test.sh <Windows|Linux|Mac> <Unit|Integration|Automation> <Test|Coverage>` runs prebuilt
assemblies from `_tests/`.

**`test.sh` always reports success.** It ends with `if [ "$EXIT_CODE" -ge 0 ]; then exit 0`, so a
red suite still exits 0. Use `dotnet test` directly whenever the result matters:

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj --filter "FullyQualifiedName~RefreshAuthorServiceFixture"
```

Two traps when building or testing a **single project** rather than the solution:

- `Directory.Build.props` pulls in StyleCop via `<AdditionalFiles Include="$(SolutionDir)stylecop.json" />`.
  `$(SolutionDir)` is only set when MSBuild is driven by the `.sln`, so building a bare `.csproj`
  drops the config and floods the build with hundreds of bogus `SA1200` errors on untouched files
  (`TreatWarningsAsErrors` is on, so they're fatal). Pass `-p:SolutionDir=<repo>/src/`.
- `AutoMoqer` calls `Assembly.Load` for the platform assembly (`Readarr.Mono` / `Readarr.Windows`)
  in its constructor, so every fixture touching `Mocker` throws `FileNotFoundException` unless that
  assembly sits next to the test dll. Building the solution puts it there; building only the test
  project does not.

### Dependencies use Central Package Management

Unlike Lidarr, versions live in `src/Directory.Packages.props` (~67 `<PackageVersion>` entries), and
`.csproj` files carry bare `<PackageReference Include="..." />` with **no `Version` attribute**.
Adding a version in a csproj is an error. Add or bump in `Directory.Packages.props`.

### OpenAPI spec

`./docs.sh <Windows|Linux|Mac> [arch]` rebuilds and regenerates the spec via Swashbuckle.

## Naming: NzbDrone vs Readarr

Readarr kept Sonarr's namespaces:

- **Directories** are `src/NzbDrone.Core`, `src/NzbDrone.Common`, …
- **Project/assembly names** are `Readarr.Core.csproj`, `Readarr.Common.csproj`, …
- **Root namespaces** are `NzbDrone.*` — `Directory.Build.props` rewrites the assembly name back

`Readarr.Api.V1` and `Readarr.Http` genuinely use the `Readarr.*` namespace. When searching, expect
`NzbDrone.Core.Books` for code under `src/NzbDrone.Core/Books/`.

## Architecture

Layering, DI, events, commands, data access and the provider pattern are all inherited from Sonarr
and behave exactly as in Lidarr:

- **DI**: DryIoc with convention-based auto-registration (`src/NzbDrone.Common/Composition/`). Every
  interface becomes a singleton, every concrete type transient. **Never register services manually** —
  defining `IFooService`/`FooService` is enough.
- **Events**: `IEventAggregator.PublishEvent<T>()`; subscribers implement `IHandle<T>` /
  `IHandleAsync<T>` and are discovered by DI. Controllers often handle events to push SignalR updates.
- **Commands**: a `Command` subclass plus an `IExecute<TCommand>` handler. `CommandQueueManager`
  persists, `CommandExecutor` runs them. `TaskManager` schedules recurring ones.
- **Data**: Dapper over SQLite (default) **or** Postgres — both must keep working.
  `BasicRepository<TModel>`, `TableMapping.cs`, `WhereBuilderSqlite`/`WhereBuilderPostgres`.
  Migrations are FluentMigrator in `src/NzbDrone.Core/Datastore/Migration/`, `NNN_snake_case.cs`
  with `[Migration(NNN)]`. Highest is **042**, so the next number is **043**. Always add the next
  number; never edit an existing migration.
- **Providers (ThingiProvider)**: indexers, download clients, notifications, import lists and
  metadata consumers are all `IProvider` + `IProviderConfig` + `ProviderDefinition`, discovered by
  reflection. Adding an indexer means adding a folder — no registration or UI changes.
- **Grab decisions**: every `IDecisionEngineSpecification` in `DecisionEngine/Specifications/` runs
  against each release; any rejection blocks the grab.

### Domain model

`src/NzbDrone.Core/Books/Model/`. Deeper than Sonarr's, and it has a second axis Lidarr lacks:

**Author** (with a shared `AuthorMetadata` row) → **Book** → **Edition** (a specific published
edition) → **BookFile**. Separately, **Series** ↔ **Book** is many-to-many through `SeriesBookLink`.

Refresh logic is in `Books/Services/Refresh*Service.cs` over a shared `RefreshEntityServiceBase`.

### Media type: a book can be an ebook *and* an audiobook

This fork exists largely for the case where one book folder holds both an epub and an m4b, so a
book monitors **one edition per media type**, not one edition outright. Upstream assumed exactly one.

- `BookMediaType { Ebook, Audiobook }` in `Books/Model/BookMediaType.cs` is **derived, not stored** —
  from `Edition.IsEbook` for editions and from the quality id for files (ids below 10 are text,
  10 and above audio).
- **`PrimaryEdition()` vs `MonitoredEditions()`.** Callers that only want a representative edition
  for display use `PrimaryEdition()`. Anything acting on what is *tracked* must use
  `MonitoredEditions()` or `MonitoredEditionFor(mediaType)`. Replacing either with
  `Single(x => x.Monitored)` reintroduces the single-format assumption.
- Migration 042 opted existing books in. `DistanceCalculator` scores `wrong_format` at 5.0 so a text
  file will not match an audio edition. Search runs once per distinct monitored edition title,
  because audiobooks are often titled "… (Unabridged)".
- **The file-handling half is unfinished** — see "Constraints on planned work" and
  `PLAN-MULTI-FORMAT.md`.

### Metadata providers

`src/NzbDrone.Core/MetadataSource/`. The five interfaces — `IProvideAuthorInfo`, `IProvideBookInfo`,
`ISearchForNewAuthor`, `ISearchForNewBook`, `ISearchForNewEntity` — are served by **one** class,
`BookMetadataProxy`, which dispatches to an `IBookMetadataProvider` selected by the
`MetadataProvider` config setting. There is currently exactly one provider, `OpenLibrary`; the seam
exists so a second (Hardcover, Google Books) can be added without touching consumers.

**Never add a second implementation of those five interfaces.** Composition registers every
interface as a singleton with a single default (`Composition/Extensions.cs`), so a second one makes
resolution ambiguous and the container throws at startup. Implement `IBookMetadataProvider`; the
facade resolves them as `IEnumerable<IBookMetadataProvider>`.

Notes that are easy to get wrong:

- **`MinPopularity` is provider-relative.** `Ratings.Popularity` is `Votes * Value`. Goodreads
  reports tens of thousands of votes; Open Library reports tens. The stock default of 350 filters
  out an author's entire catalogue on Open Library data, silently — books simply never appear.
  `MetadataProfileService` seeds the default from the configured provider.
- **Open Library's search `language` field is a work-level aggregate in arbitrary order** — not the
  language of any one edition. Use `PreferredLanguage`, not `First()`.
- **Open Library author search ranks poorly** and is re-sorted by `RankAuthors`.
- **Audible is an undocumented app API.** All enrichment is best-effort and must degrade to "no
  audiobook data" rather than failing a lookup.
- `IProvideSeriesInfo`/`IProvideListInfo` are *not* part of this path — they belong to the Goodreads
  import lists and are implemented by `GoodreadsProxy`.

### API controllers

**POST/PUT actions must carry `[FromBody]` explicitly.** `V1ApiControllerAttribute` implements
`IApiBehaviorMetadata`, but binding-source inference does not fire for it on .NET 8, so a complex
parameter without the attribute binds from form/query and silently arrives as a default instance —
every write then fails FluentValidation with "must not be empty" and a null `propertyValue`, which
looks like a client bug rather than a server one. `ProviderControllerBase` already had it; the other
33 actions did not until this fork added them.

### Calibre

`src/NzbDrone.Core/Books/Calibre/` integrates with a Calibre content server (`CalibreProxy`) for
conversion and library management. This has no analogue in the other *arrs and is a real asset for
ebook work.

### Frontend

React 18 + Redux, webpack 5, gradually migrating JS → TS.

- **Imports are absolute from `frontend/src`** (`import translate from 'Utilities/String/translate';`).
- **Redux is factory-driven**: compose handler creators from `Store/Actions/Creators/` rather than
  hand-writing reducers. Follow the neighbouring `*Actions.js`.
- **CSS Modules** with generated, *committed* `.css.d.ts` files. If class names change, run a build
  so they regenerate, and commit them.
- SignalR keeps the store live; most updates are pushed cache invalidations that trigger a refetch.

## Known performance pathology (shared with Lidarr)

Readarr has the same command-queue design, and the same resulting lag:

- `CommandQueue.TryGet` (`src/NzbDrone.Core/Messaging/Commands/CommandQueue.cs:169`) serializes
  **every** command with `RequiresDiskAccess` against every other one, process-wide. That filter runs
  *before* the priority sort, so a user-initiated command cannot preempt a running scheduled one.
- `CommandExecutor.THREAD_LIMIT = 3`, no preemption — effective disk concurrency is 1.
- `RescanFoldersCommand` is both `RequiresDiskAccess` **and** on the 24h schedule, so a full-library
  scan holds the global lock for its entire duration.
- Everything in that lock group is user-facing: `ManualImport`, `RenameFiles`, `RenameAuthor`,
  `RetagFiles`, `RetagAuthor`, `MoveAuthor`, `BulkMoveAuthor`.
- `CommandQueueManager.Push()` publishes no event, so the UI is not told a command is queued — only
  when it *starts*. Hence "nothing happens until you open System → Tasks".

`../Liedarr` has a fix for this (`Command.DiskAccessPaths` + `DiskAccessConflictDetector`, scoping
the lock to overlapping paths, plus fanning the scheduled scan out per root folder). It ports over
with entity renames.

## Constraints on planned work

- **Cross-format file handling is not finished, and one part of it loses data.**
  `UpgradeMediaFileService.UpgradeBookFile` recycles every file on the *book*, so importing an
  audiobook deletes the ebook; `UpgradeSpecification`, `UpgradeDiskSpecification` and
  `CutoffSpecification` all compare a release against every file on the book regardless of media
  type, so an ebook is rejected whenever an audiobook exists. See `PLAN-MULTI-FORMAT.md`; stages 0
  and 1 there are the fix. Anything touching those paths should scope by media type.
- **Audiobookshelf as the frontend** means Readarr's own UI matters less than its API. Favour
  keeping `Readarr.Api.V1` stable and well-formed over UI polish.
- ~~.NET 6 is EOL.~~ Done — the fork is on .NET 8, matching Lidarr.
- ~~slskd needs the protocol enum replaced first.~~ Done — `IDownloadProtocol` marker interface,
  plus the slskd indexer and download client.

## Conventions

- Backend: 4 spaces. `TreatWarningsAsErrors` is **on** with StyleCop and `EnforceCodeStyleInBuild` —
  style violations, including unused usings (IDE0005), fail the build.
- Commit with \*nix line endings; feature branches, not `develop` directly.
- Commit messages for user-visible changes start with `New:` or `Fixed:`.
- Any new user-facing string needs a key in `src/NzbDrone.Core/Localization/Core/en.json`, consumed
  via `ILocalizationService.GetLocalizedString("Key")` (backend) or `translate('Key')` (frontend).
  Other language files are Weblate-managed — do not hand-edit. Log messages are not translated.
