# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Readarr — an ebook and audiobook collection manager for Usenet/BitTorrent. C# / .NET 6 backend
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

- **The metadata service is dead.** `src/NzbDrone.Common/Cloud/ReadarrCloudRequestBuilder.cs`
  hardcodes `https://api.bookinfo.club/v1/{route}` in its constructor. There is **no config or
  environment override** — pointing at a replacement currently requires a code change (or DNS/hosts
  interception). The community mirror named in the README is
  [rreading-glasses](https://github.com/blampe/rreading-glasses). Adding a proper config hook for
  the metadata base URL is the highest-value first change.
- Without working metadata, author/book lookup, `RefreshAuthor` and import-list sync all fail. Any
  refactor that can't be tested against a metadata source will be hard to validate end to end.
- Nothing upstream will be merged back, so there is no need to keep changes upstream-shaped. This is
  the opposite of the sibling Lidarr fork (see below).

## Sibling fork

`../Liedarr` is a Lidarr fork worked on in parallel. Lidarr and Readarr are both Sonarr forks and
share most infrastructure verbatim, so fixes usually port with only entity renames
(Artist→Author, Album→Book, Track→Edition/BookFile). Two differences are load-bearing and are
called out in the sections below: Readarr is on .NET 6 (Lidarr is on 8), and Readarr still has the
`DownloadProtocol` **enum** where Lidarr has a pluggable marker interface.

## Commands

Same shape as Lidarr/Sonarr. Node 20 (pinned via volta to 20.11.1) and Yarn via `corepack enable`.
There is **no `global.json`**, so the SDK is not pinned — but projects target `net6.0`, which is
**out of support** (EOL Nov 2024). A newer SDK will build it, but expect analyzer and dependency
friction; migrating to .NET 8 is a natural early task.

### Backend

```bash
./build.sh --all                          # backend + frontend + lint + packages
./build.sh --backend -f net6.0 -r linux-x64
dotnet msbuild -restore src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix -t:PublishAllRids
```

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
  with `[Migration(NNN)]`. Currently 41 migrations, highest **040**. Always add the next number;
  never edit an existing migration.
- **Providers (ThingiProvider)**: indexers, download clients, notifications, import lists and
  metadata consumers are all `IProvider` + `IProviderConfig` + `ProviderDefinition`, discovered by
  reflection. Adding an indexer means adding a folder — no registration or UI changes.
- **Grab decisions**: every `IDecisionEngineSpecification` in `DecisionEngine/Specifications/` runs
  against each release; any rejection blocks the grab.

### Domain model

`src/NzbDrone.Core/Books/Model/`. Deeper than Sonarr's, and it has a second axis Lidarr lacks:

**Author** (with a shared `AuthorMetadata` row) → **Book** → **Edition** (a specific published
edition; one is monitored) → **BookFile**. Separately, **Series** ↔ **Book** is many-to-many through
`SeriesBookLink`.

Refresh logic is in `Books/Services/Refresh*Service.cs` over a shared `RefreshEntityServiceBase`.

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

- **slskd / Soulseek support is expensive here.** `src/NzbDrone.Core/Indexers/DownloadProtocol.cs`
  is still a plain `enum { Unknown, Usenet, Torrent }`. Lidarr replaced this with an
  `IDownloadProtocol` marker interface plus string identity specifically so new protocols could be
  added, which made a Soulseek client tractable there. In Readarr, adding a third protocol means
  touching every site that switches on the enum. **Port Lidarr's marker-interface refactor first**;
  don't bolt a third enum value on.
- **Audiobookshelf as the frontend** means Readarr's own UI matters less than its API. Favour
  keeping `Readarr.Api.V1` stable and well-formed over UI polish.
- **.NET 6 is EOL.** Upgrading to .NET 8 aligns with the Lidarr fork and unblocks current tooling.

## Conventions

- Backend: 4 spaces. `TreatWarningsAsErrors` is **on** with StyleCop and `EnforceCodeStyleInBuild` —
  style violations, including unused usings (IDE0005), fail the build.
- Commit with \*nix line endings; feature branches, not `develop` directly.
- Commit messages for user-visible changes start with `New:` or `Fixed:`.
- Any new user-facing string needs a key in `src/NzbDrone.Core/Localization/Core/en.json`, consumed
  via `ILocalizationService.GetLocalizedString("Key")` (backend) or `translate('Key')` (frontend).
  Other language files are Weblate-managed — do not hand-edit. Log messages are not translated.
