# Readarr revival roadmap

Working notes for continuing this fork. Read alongside `CLAUDE.md`, which covers build commands,
architecture and the traps. This file is the *plan*; CLAUDE.md is the *map*.

Branch: `feature/slskd-and-metadata-revival`

## Goal

1. Search for new authors / books / series and organise the library
2. slskd as indexer + download client, the way it was done in the sibling Lidarr fork
3. Clearer separation of audiobooks vs ebooks
4. Audiobookshelf is the reading/listening frontend; Readarr is the acquisition and organisation backend

## Done so far

- **Readarr builds again.** It did not. NuGet audit flags `Mailkit` 4.8.0 and `SixLabors.ImageSharp`
  3.1.7 as vulnerable and `TreatWarningsAsErrors` promotes NU1902 to an error, so `Readarr.Core` and
  everything above it failed to compile. Bumped to 4.16.0 / 3.1.12 (the versions the Lidarr fork
  runs), which dragged `System.Text.Encoding.CodePages` to 8.0.0.
- **Metadata resolves in-process.** Open Library + Audible, no sidecar, no API key. The whole
  bookinfo.club path has since been deleted; see "Metadata: resolved, in-process" below.
- **Migrated net6.0 to net8.0.** Not optional: see "Why net8 was forced" below.
- **Fixed API writes.** Every POST/PUT in `Readarr.Api.V1` silently bound an empty resource on
  net8; 33 actions needed an explicit `[FromBody]`.
- **Readarr runs in Docker.** `Dockerfile` + `docker/entrypoint.sh` (ported from the Lidarr fork),
  wired into the dockerComPlex compose stack.
- `CLAUDE.md` written.

## Why net8 was forced

The net6.0 target did not merely warn — Readarr **crashlooped on startup**:

```
System.IO.FileLoadException: Could not load file or assembly
'System.Text.Encoding.CodePages, Version=8.0.0.0'. The located assembly's manifest
definition does not match the assembly reference.
```

The MailKit 4.16 bump (needed to clear the NU1902 vulnerability error that made the build fail at
all) drags in `System.Text.Encoding.CodePages` 8.0.0. Under a **self-contained** net6.0 publish the
net6.0 runtime pack's own 6.0.0.0 copy of that assembly wins the file-conflict resolution and lands
in the publish output, while `deps.json` still declares 8.0.0.0. The result compiles cleanly and
dies instantly at runtime. There is no way out of that on net6.0 without reintroducing the
vulnerable MailKit.

Fallout fixed during the migration:

- `SYSLIB0051` — obsolete `(SerializationInfo, StreamingContext)` exception constructors in
  `DestinationAlreadyExistsException`, `RecycleBinException`, `RootFolderNotFoundException`,
  `AzwTagException`. Dropped, matching how the Lidarr fork resolved the same files.
- `CS0618` — `ISystemClock` removed from the `AuthenticationHandler` base constructor in
  `ApiKeyAuthenticationHandler`, `BasicAuthenticationHandler`, `NoAuthenticationHandler`.
- `ASP0019` — `IHeaderDictionary.Add` → indexer in `BasicAuthenticationHandler` and
  `VersionMiddleware`.
- **Two test call sites did not compile even before the migration** (`CoreTest.cs:32`,
  `SystemTimeCheckFixture.cs:21`, `error CS7036`): the previous session's
  `ReadarrCloudRequestBuilder` constructor change was verified against the host only, never against
  the test projects. (`MetadataOptions` has since been deleted along with the bookinfo path, so
  both now construct `ReadarrCloudRequestBuilder` with no arguments.)

No `global.json` was added. Without one, the SDK 8.0 image builds it and a newer local SDK still
can; pinning to 8.0.x the way Lidarr does would break local builds on any machine without that
exact SDK band.

## Library scan results (real library, S:\My Books)

2150 files / 30.5 GB, of which 1868 are media. After a full scan against the Open Library provider:

- **12 authors identified**, 12 files imported, **1638 files unmapped**
- The library was untouched — byte-for-byte identical before and after (`renameBooks` and
  `deleteEmptyFolders` are both off, so Readarr only reads)

Where the unmapped files are:

| Folder | Files |
|---|---|
| `Tolkien Lord Of The Rings Hobbit audiobook Rob Inglis 56 CD MP3` | 1310 |
| `J K Rowling - Harry Potter 1-7 Unabridged Audiobooks Narrated by Jim Dale` | 199 |
| `podcast` | 104 |
| `Frank Herbert` | 14 |
| `The Black Magician Trilogy by Trudi Canavan [EPUB] [MOBI]` | 4 |

1613 of 1638 are the three raw release folders plus `podcast/` — release names never filed under an
author, so there is no author folder to match against. Only ~16 files inside correctly named author
folders failed to map, and those are multi-file mp3 audiobooks.

This is step 5's problem, not a metadata problem: `podcast/` should be excluded from the root folder
outright, and the three release folders need manual author assignment before any rename pass.

## The user's actual library

`S:\My Books`, bind-mounted as `/books`. Roughly 9 top-level entries, mixed conventions.

**Target shape** (what the tidy entries already look like):

```
My Books/
  Frank Herbert/
    Dune 0.1 - The Butlerian Jihad/
      Dune 0.1 - The Butlerian Jihad.m4b     <- audiobook
      Dune_ The Butlerian Jihad_ ... .epub   <- ebook, same book
      Dune 0.1 - The Butlerian Jihad.jpg     <- cover
      metadata.json                          <- written by Audiobookshelf
  Brené Brown/
    The Gifts of Imperfection - .../
  Dinniman Matt/
    Dungeon Crawler Carl .../
  podcast/                                   <- separate tree, Audiobookshelf-managed
    StarTalk Radio/ , The Adventure Zone/ , ...
```

**Untidy entries that need organising** — raw release folders never filed under an author:

- `J K Rowling - Harry Potter 1-7 Unabridged Audiobooks Narrated by Jim Dale`
- `The Black Magician Trilogy by Trudi Canavan [EPUB] [MOBI]`
- `Tolkien Lord Of The Rings Hobbit audiobook Rob Inglis 56 CD MP3`
- `Boys Club`

Author folder naming is inconsistent: `Frank Herbert` (First Last) vs `Colfer Eoin` / `Dinniman Matt`
(Last First). Whatever naming config is chosen has to be applied deliberately, not assumed.

File extensions present: `.mp3 .jpg .log .epub .json .webp .m4b .mobi .m4a .txt`

### The load-bearing observation

**A single book folder holds both an audiobook and an ebook of the same book.** In Readarr's model
those are two `Edition` rows under one `Book`. Readarr has no notion of "this edition is audio and
that one is text", so quality profiles, monitoring and the UI cannot currently tell them apart. Any
work on item 3 of the goals starts here, not in the UI.

## Ordered plan

The order matters; each step unblocks the next.

### 1. ~~Upgrade net6.0 → net8.0~~ — DONE

See "Why net8 was forced" above. Targets now match the Lidarr fork, so subsequent ports are
straight copies rather than retargeting exercises.

### 2. ~~Replace the `DownloadProtocol` enum with marker interfaces~~ — DONE

`IDownloadProtocol` marker interface + `UsenetDownloadProtocol` / `TorrentDownloadProtocol`,
identified by type name as a string. 204 references across 71 files.

What a third protocol now costs: add the class, add a `DelayProfileProtocolItem` entry, implement
the indexer and download client. No switch sites to hunt down.

Things worth knowing before step 3:

- **`DelayProfile` changed shape, not just type.** `EnableUsenet`/`EnableTorrent`/`UsenetDelay`/
  `TorrentDelay`/`PreferredProtocol` became an ordered `List<DelayProfileProtocolItem>` where order
  carries preference. Ask `IsAllowedProtocol` / `IsPreferredProtocol` / `GetProtocolDelay`.
- **Migration 041** converts existing rows. Blocklist and DownloadHistory needed a new text column
  populated from the old int and then a rename, because SQLite cannot change a column type in place.
  Next migration number is **042**.
- **`ProtocolLabel` strips the `DownloadProtocol` suffix** before its CSS lookup. `styles[protocol]`
  against the raw value returns undefined and renders unstyled — the bug the Lidarr port also had.
- A new protocol needs a `DelayProfileProtocolItem` in `DelayProfile`'s constructor, or it will not
  be allowed by default and releases will be silently rejected by `ProtocolSpecification`.

### 3. ~~Port the slskd indexer and download client~~ — DONE

Soulseek is a third protocol. Adding `SoulseekDownloadProtocol` was one class; the indexer and
download client needed only entity renames from the Lidarr port.

Two things a third protocol needed that were not obvious:

- **`DelayProfileService.AddMissingItems`** reconciles stored profiles against registered protocols.
  Without it a protocol added after a profile was saved is absent from `Items`, `IsAllowedProtocol`
  returns false, and `ProtocolSpecification` rejects every release on it with no visible cause.
- **`ProcessDownloadDecisions`** tracked failure with a bool per protocol; now a `HashSet<string>`.

Settings are retuned for books: extensions default to `epub,mobi,azw3,pdf,m4b,mp3,m4a,flac` and
`MinimumFileCount` is 1, since an ebook is a single file where an album is not.

Verified against slskd 0.26.0.0 on the live stack: both connectivity tests pass and an interactive
search for "Children of Dune" returned 139 releases including M4B audiobooks.

**Known tuning issue:** because `mp3` and `flac` are allowed (needed for mp3 audiobooks), a search
also returns music that merely matches the title — the Brian Tyler *Children of Dune* soundtrack
came back as FLAC albums. Worth a book-oriented filter, or dropping `flac` from the default.

### 4. Audiobook vs ebook as a first-class distinction — partly done

`Edition` already had `IsEbook` and `Format`, and the Open Library/Audible provider now populates
both: text editions from Open Library carry `IsEbook = true`, Audible editions carry
`IsEbook = false` and `Format = "Audiobook"` with the narrators in `Disambiguation`. A book with both
an epub and an m4b - exactly the user's folder layout - now models as one `Book` with two `Edition`
rows that can be told apart.

What is still missing is option **b** below: letting quality profiles and monitoring *target* a
media type, so "monitor audiobooks only for this author" becomes expressible. The data is there now;
the profile/monitoring surface is not.

Original options, for reference:

Options, roughly increasing in cost:

- **a.** Derive it from file extension at import and expose it on `BookFile` / `Edition`, then filter
  in the UI. Cheapest, no schema change beyond a column.
- **b.** Add a media-type concept to `Edition` and let quality profiles and monitoring target it, so
  "monitor audiobooks only for this author" becomes expressible. This is what actually makes the
  library manageable and is probably the right answer.
- **c.** Separate root folders per media type. Matches how the user already separates `podcast/`, but
  fights the fact that their book folders deliberately hold both formats together.

Recommend **b**, with **a** as the migration path to populate it.

### 5. Organisation / naming

`src/NzbDrone.Core/Organizer/` drives folder and file naming. Target the shape above:
`{Author Name}/{Series} {Position} - {Book Title}/`. The user's series numbering
(`Dune 0.1 - ...`) maps onto `Series` + `SeriesBookLink`, which Readarr already models.

Do not mass-rename without a preview run; three existing top-level folders are raw release names and
will need manual author assignment first.

## Podcasts: resolved

Moved `S:\My Books\podcast` to `S:\Podcasts` (110 files, 7.3 GB, 8 shows) and mounted it into
Audiobookshelf as `/podcasts`. Readarr's unmapped count dropped 1638 -> 1534 and no podcast entries
remain.

Worth knowing: Audiobookshelf had only **one** library, "Books" at `/books`, so it was scanning those
podcast episodes as books too. The move fixes both. Add a Podcasts library in the Audiobookshelf UI
pointing at `/podcasts` to get its native podcast handling.

Readarr has no per-folder exclusion. `DiskScanService.ExcludedSubFoldersRegex` skips `extras`,
`@eadir`, `extrafanart`, `plex versions` and anything starting with a dot - so a dot-prefixed folder
would also have worked, but that would have hidden it from Audiobookshelf as well.

## Metadata: resolved, in-process

**There is no metadata sidecar, and no bookinfo code path.** rreading-glasses and its Postgres are
gone from the compose stack, and `BookInfoProxy`, its resources, `MetadataRequestBuilder`,
`MetadataOptions` / `Readarr__Metadata__Source`, `ConfigService.MetadataSource` and the
`IReadarrCloudRequestBuilder.Metadata` factory have been deleted from the tree. Readarr resolves
metadata itself:

| Concern | Source |
|---|---|
| Author identity, bibliography | Open Library `/authors/{key}` + `/search.json?author_key=` |
| Text editions, ISBNs, covers, page counts | Open Library `/works/{key}/editions.json` |
| Audiobook editions, narrators, runtime | Audible `catalog/products` |
| Series and sequence | Audible (`("Dune", 3)`) |

Neither needs an API key. Google Books was rejected: it returns HTTP 429 unauthenticated.

### Shape

The five metadata interfaces (`IProvideAuthorInfo`, `IProvideBookInfo`, `ISearchForNewAuthor`,
`ISearchForNewBook`, `ISearchForNewEntity`) are served by a single `BookMetadataProxy` facade, which
dispatches to an `IBookMetadataProvider` chosen by the `MetadataProvider` config setting
There is currently exactly one provider, `OpenLibrary`; the seam exists so a second (Hardcover,
Google Books) can be added without touching consumers.

**Only one class may implement those five interfaces.** Composition registers every interface as a
singleton with a single default, so a second implementation makes resolution ambiguous and the
container throws at startup. New backends implement `IBookMetadataProvider` instead.

Three properties of the existing code made this tractable, contrary to the earlier assumption that a
replacement would have to speak the bookinfo schema:

- The interfaces are written in Readarr's own domain types, not the wire schema.
- All foreign IDs are `string` and nothing parses them as ints, so `OL79034A` needs no schema change.
- `GetChangedAuthors` may return `null`, which makes `RefreshAuthorService` fall back to its own
  `ShouldRefresh` heuristic. No change-feed endpoint is required.

`IProvideSeriesInfo` and `IProvideListInfo` are *not* part of this path — they belong to the
Goodreads import lists and are implemented by `GoodreadsProxy`.

### Traps found while building it

- **`MinPopularity` is provider-relative.** `Ratings.Popularity` is `Votes * Value`, and Open Library
  reports tens of ratings where Goodreads reports tens of thousands. The stock default of 350 filters
  out essentially an author's whole catalogue on Open Library data. The seeded default is now chosen
  from the configured provider (`OPEN_LIBRARY_DEFAULT_MIN_POPULARITY = 20`). Existing profiles are
  untouched, so an install created before this needs its profile lowered by hand.
- **Open Library's `language` is a work-level aggregate in arbitrary order.** Children of Dune leads
  with `pol`, Heretics with `rus`. Taking the first entry made the synthetic text edition look
  Polish, and the default `eng, null` profile then deleted it - leaving books with only an Audible
  audiobook. `PreferredLanguage` picks `eng` when the work has an English edition at all.
- **Author search ranks badly.** A query for "Frank Herbert" returns "Frank Herbert Hayward" and
  "Simonds, Frank Herbert" above the real one. `RankAuthors` re-sorts on exact match, then prefix,
  then work count.
- Audible enrichment is strictly best-effort. It is an undocumented app API; every failure degrades
  to "no audiobook data" rather than failing the lookup.

### Verified end to end

On a from-scratch install with an empty config directory:

```
seeded minPopularity              20         (provider-aware)
author lookup "Frank Herbert"     OL79034A ranked first
add author + RefreshAuthor        8 books persisted
editions                          every book has a text edition; 5 of 8 also have an audiobook
monitored edition                 the text one (Format "Book")
series                            Dune (positions 3-6), The Dune Sequence (14-17)
```

`OpenLibraryProviderFixture` covers this against the live APIs (16 tests).

## Running the stack

Readarr is built locally, not pulled:

```bash
cd "C:\Users\dood3\Documents\Projects\Claud Code\Readarr"
docker build -t readarr:local .
cd C:\Users\dood3\dockerComPlex
docker compose up -d readarr
```

Readarr on :8787, and nothing else — metadata needs no companion service. Watchtower is excluded via
label so it never replaces the local build with a registry pull.

The runtime image needs `libsqlite3-0` from apt. `AssemblyLoader.LoadSqliteNativeLib` maps
`sqlite3` to the *system* `libsqlite3.so.0` on Linux, and `System.Data.SQLite.Core.Servarr`
1.0.115.5-18 ships no bundled native — without the package Readarr dies on "Error creating main
database". The Lidarr fork's Dockerfile has no such line because its newer provider bundles
`libe_sqlite3.so`; do not copy that apt list verbatim.

## Next session

1. Step 2 — the `DownloadProtocol` marker-interface refactor. This is now the blocker for
   everything acquisition-related.
2. Then step 3 (slskd), step 4 (media type), step 5 (naming).

Worth doing at some point, independent of that chain: route `GoodreadsSearchProxy` through the
configurable metadata source so book search stops hitting goodreads.com directly.
