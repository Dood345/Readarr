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
- **Metadata source is configurable.** `Readarr__Metadata__Source` (or `Metadata/Source` in
  config.xml) now overrides the dead `api.bookinfo.club` endpoint. A bare host is accepted and
  expanded to `<host>/v1/{route}`.
- `CLAUDE.md` written.

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

### 1. Upgrade net6.0 → net8.0

.NET 6 went out of support in Nov 2024. The package bumps above already forced one transitive
dependency to its .NET 8-era version, and that will keep happening. The Lidarr fork is on net8.0, so
every subsequent port is easier once the targets match.

Touch: `src/Directory.Build.props`, every `<TargetFrameworks>`, `global.json` (Readarr has none —
consider adding one pinned to 8.0.x to match Lidarr).

### 2. Replace the `DownloadProtocol` enum with marker interfaces

`src/NzbDrone.Core/Indexers/DownloadProtocol.cs` is still:

```csharp
public enum DownloadProtocol { Unknown = 0, Usenet = 1, Torrent = 2 }
```

Lidarr replaced this with an `IDownloadProtocol` marker interface plus string identity
(`nameof(TorrentDownloadProtocol)`), which is exactly why adding Soulseek there needed roughly one
mandatory edit. **Do this refactor before attempting slskd.** Bolting a third enum value on means
touching every switch site and leaves the same problem for the next protocol.

Port from `../Liedarr`, commit `fcfc60a27` ("New: Plugin support") plus the queue/history/API
plumbing that carries protocol as a string.

Watch for: `DelayProfile` persistence and migrations `043`/`046` in Lidarr converted enum→string;
Readarr will need an equivalent migration (next number is **041**).

### 3. Port the slskd indexer and download client

From `../Liedarr` branch `feature/slskd-indexer-and-download-client`, commit `3dabaab6b`:

| Lidarr file | Readarr equivalent |
|---|---|
| `Indexers/Slskd/SlskdResource.cs` | same, unchanged |
| `Indexers/Slskd/SlskdProxy.cs` | same, unchanged |
| `Indexers/Slskd/SlskdIndexerSettings.cs` | same, extensions default to `epub,mobi,azw3,m4b,mp3,m4a,flac` |
| `Indexers/Slskd/SlskdIndexer.cs` | Album/Artist criteria → Book/Author criteria |
| `Download/Clients/Slskd/*` | same, unchanged |
| `ProcessDownloadDecisions.cs` | same `HashSet<string> failedProtocols` change |
| `frontend/.../ProtocolLabel.*` | same, plus the `styles[protocol]` lookup bug is present here too |

Verified slskd API facts (against slskd 0.26.0.0):

- `POST /api/v0/searches` `{id, searchText}` → `GET /api/v0/searches/{id}` until `isComplete` →
  `GET /api/v0/searches/{id}/responses`
- A response is per **peer**: `username, queueLength, uploadSpeed, hasFreeUploadSlot, files[]`
- A file is `filename` (backslash-separated), `size`, `length`, `bitDepth`, `sampleRate`, `isLocked`
- Soulseek has no concept of a release, so the indexer groups a peer's files by parent folder
- `POST /api/v0/transfers/downloads/{username}` enqueues; `GET /api/v0/transfers/downloads` lists
- Searching works with a `readonly` API key; **enqueuing needs `readwrite`**

Readarr-specific wrinkle: a Soulseek folder may contain the epub *and* the m4b. Decide whether that
is one grab producing two editions, or whether the indexer emits separate releases per format. This
interacts directly with step 4.

### 4. Audiobook vs ebook as a first-class distinction

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

## Podcasts: recommendation

The instruction was "forget about podcasts entirely if Lidarr already supports them". It does not —
there are **zero** podcast references in either Lidarr or Readarr source.

Recommend leaving podcasts to Audiobookshelf regardless. It has native podcast support (search,
RSS subscription, scheduled episode download), the user's `S:\My Books\podcast\` tree is already
laid out the way Audiobookshelf expects, and building a parallel podcast pipeline into Readarr would
duplicate a solved problem. Keep `podcast/` excluded from Readarr's root folder so it never tries to
parse those as books.

Flagging rather than deciding: this reverses the literal instruction, so confirm before acting.

## Metadata: the elephant

The configurable source is in place, but **it is untested against a real backend**. Before anything
in this roadmap can be validated end to end, point it at a working metadata provider:

```
Readarr__Metadata__Source=http://rreading-glasses:8788/v1/{route}
```

Audiobookshelf uses Audible and Google Books for its own metadata; those are *not* drop-in
replacements for Readarr's `BookInfoProxy`, which expects the bookinfo.club schema
(`src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoResource/`). Writing an Audible or Open Library
proxy that speaks that schema is a real project in its own right — worth scoping separately if
rreading-glasses proves unreliable.

## Suggested first session after this

1. Stand up rreading-glasses in the compose stack, point `Readarr__Metadata__Source` at it, confirm
   an author search returns results. Nothing else is verifiable until this works.
2. Then step 1 (net8), then step 2 (protocol), then step 3 (slskd).
