# Multi-format plan: one book, an ebook and an audiobook

Working plan for making Readarr handle a book that exists in more than one format, from the core
outwards. Read alongside `CLAUDE.md` (the map) and `ROADMAP.md` (the revival history).

Baseline commit: `865d1431` on `feature/slskd-and-metadata-revival`. Next migration number: **043**.

## The target case

```
My Books/
  Frank Herbert/
    Dune 0.1 - The Butlerian Jihad/
      Dune 0.1 - The Butlerian Jihad.m4b     <- audiobook
      Dune_ The Butlerian Jihad_ ... .epub   <- ebook, same book
```

One `Book`, two `Edition` rows, two `BookFile` rows, both monitored, tracked and upgraded
independently. ROADMAP step 4 built the monitoring half of this. This plan covers everything the
files touch: quality, acquisition, import, upgrade, statistics, naming and the API.

## Where the code actually is today

Step 4 landed the modelling and it is sound:

- `BookMediaType { Ebook, Audiobook }` with `MonitoredEditions()`, `PrimaryEdition()` and
  `MonitoredEditionFor(mediaType)` in `Books/Model/BookMediaType.cs`.
- Migration 042 opted existing books into one monitored edition per media type.
- `ReleaseSearchService.BookSearch` searches once per distinct monitored edition title.
- `DistanceCalculator` already refuses to match a text file to an audio edition
  (`wrong_format`, weight 5.0) and vice versa.
- `MetadataTagService` already dispatches to `AudioTagService` / `EBookTagService` by extension.

What was never done is the other half: **everything downstream of the edition still treats "the
files of this book" as one undifferentiated set.** Media type is also inferred from a magic quality
id range rather than being a property of the quality.

## The seven defects this plan fixes

Ordered by severity. The first is data loss and is live today.

### 1. Importing an audiobook deletes the ebook — data loss

`UpgradeMediaFileService.UpgradeBookFile` (`MediaFiles/UpgradeMediaFileService.cs:50`) takes
`localBook.Book.BookFiles.Value` — every file on the **book** — and recycles all of them before
moving the new file in. Every download import reaches it
(`ImportApprovedBooks.cs:231`, the `!localTrack.ExistingFile` branch).

So: a book with `dune.epub` on disk, grab the M4B, and the epub is recycled. Nothing in the path
scopes that deletion to the media type being replaced.

### 2. An ebook can never be imported once an audiobook exists

`BookImport/Specifications/UpgradeSpecification.cs:39` compares the incoming file against every
file on the book. Quality weights are one global ladder — EPUB is 11, M4B is 105 — so an EPUB is
always "not an upgrade for existing book file(s)" and is rejected.

### 3. An ebook release can never even be grabbed

Same shape, two specs:

- `DecisionEngine/Specifications/UpgradeDiskSpecification.cs:31` — `subject.Books.SelectMany(c =>
  c.BookFiles.Value)`, rejects with "Existing files on disk is of equal or higher preference: M4B".
- `DecisionEngine/Specifications/CutoffSpecification.cs:34` — same enumeration, rejects with
  "Existing files meets cutoff".

Defects 1–3 compose into: **the dual-format case cannot be reached by acquisition at all, and if it
is reached by a disk scan, the next grab destroys it.**

### 4. Housekeeping silently reverts dual-format monitoring

`Housekeeping/Housekeepers/FixMultipleMonitoredEditions.cs` un-monitors an edition on any book with
more than one monitored edition. That is precisely the state step 4 introduced, so every
housekeeping run erodes it. The Postgres branch is separately wrong — it sets `Monitored = true`
where the SQLite branch sets `0`, so it has never done anything on Postgres.

### 5. A quality profile cannot express two formats

`QualityProfile.Cutoff` is a single quality id. With a mixed profile:

- cutoff `EPUB` → `BookCutoffService.cs:36` takes `Items.Take(cutoffIndex)`, so no audio quality is
  ever below cutoff; audiobooks are never upgraded.
- cutoff `M4B` → every text quality is below cutoff, so every ebook is permanently "cutoff unmet"
  and permanently un-upgradeable, because defect 3 rejects the replacement.

`FirstAllowedQuality()` / `LastAllowedQuality()` have the same single-ladder assumption. The seeded
defaults ("eBook", "Spoken") are both single-format; nothing seeds a profile for the case this fork
exists to serve.

### 6. Media type is a magic id range

`BookMediaTypeExtensions.FirstAudioQualityId = 10` infers media type from `Quality.Id >= 10`. It
works for the nine qualities that exist, and it makes ids 5–9 the only slots a new **text** format
can ever occupy. `Quality.AllLookup` is a dense array sized by max id, so this is a real ceiling on
adding AZW, FB2, OPUS or AAC.

### 7. Statistics, wanted and the UI cannot see formats

- `AuthorStats/AuthorStatisticsRepository.cs:57` — `CASE WHEN MIN("BookFiles"."Id") IS NULL THEN 0
  ELSE 1`, grouped by book. A book with only the audiobook counts as fully available.
- `BookRepository.BuildQualityCutoffWhereClause` keys on `Authors.QualityProfileId` alone.
- The frontend has no notion of media type anywhere. `frontend/src/Book/BookFormats.js` renders
  *custom* formats, not media types. No column, no filter, no per-format monitor toggle.
- `NamingConfig` has one `StandardBookFormat`, so a 56-file MP3 audiobook and an epub are filed by
  the same rule into the same folder.

## Design decisions

Two choices shape everything below; both are settled here so the stages can be read as instructions.

**Media type belongs on `Quality`, not on an id range.** `Quality` gains a `MediaType` property set
in its private constructor. This is a two-line change that removes the id ceiling, makes every
downstream filter self-documenting, and is the natural place for the fact to live.

**One quality profile per author, with a cutoff per media type.** Not two profiles on `Author`.
Rationale: the profile's `Items` list already carries every quality with an `Allowed` flag, so a
mixed profile expresses "EPUB and AZW3 yes, MP3 no, M4B yes" today — the only thing it cannot
express is two cutoffs. Adding `Cutoffs` as an ordered list of `(MediaType, QualityId)` mirrors the
`DelayProfileProtocolItem` pattern this fork already adopted in ROADMAP step 2, and avoids a second
foreign key on `Author`, a second dropdown on every add-author path, and a migration over
`ImportList`, `RootFolder` and `AuthorEditor`.

The tradeoff, stated plainly: `UpgradeAllowed`, `MinFormatScore` and `CutoffFormatScore` stay
per-profile, so they cannot differ between an author's ebooks and audiobooks. If that turns out to
matter, the escape hatch is to move those three onto the same per-media-type item list later, which
is additive.

## Stages

Each stage builds, tests and ships on its own. Stage 1 is the one that matters; stages 0 and 1
together make dual-format actually work.

### Stage 0 — make media type explicit

Foundation for everything else. No behaviour change.

- `Qualities/Quality.cs`: add `public BookMediaType MediaType { get; set; }`, set it in the private
  constructor, pass it for all nine qualities.
- `Books/Model/BookMediaType.cs`: delete `FirstAudioQualityId`; `MediaType(this Quality)` returns
  `quality?.MediaType ?? BookMediaType.Ebook`.
- Add the filter helper the next stage needs, written once:
  `public static IEnumerable<BookFile> OfMediaType(this IEnumerable<BookFile>, BookMediaType)`, and
  a `MediaType(this BookFile)` reading `file.Quality.Quality.MediaType`.
- `Quality` is an `IEmbeddedDocument` persisted inside `QualityModel` — check that adding a property
  does not change the stored JSON shape in a way `QualityModel` deserialisation minds. It stores
  `{quality: {id, name}, revision: {...}}`; a new property serialises out and reads back from
  `FindById`, so this should be inert. **Verify with a round-trip test before relying on it.**

Tests: `QualityFixture` — every quality reports the expected media type; `Quality.All` has no id
gap assumption left.

### Stage 1 — stop the data loss and the cross-format rejections

The whole point of the plan. Five edits, one shared helper.

1. **`UpgradeMediaFileService.UpgradeBookFile`** — filter `existingFiles` to
   `.OfMediaType(bookFile.Quality.Quality.MediaType)`. Media type, not edition, is the right axis:
   replacing a chapter-per-file MP3 set with an M4B is a legitimate same-type upgrade across
   editions.
2. **`BookImport/Specifications/UpgradeSpecification`** — same filter on `item.Book.BookFiles`.
3. **`DecisionEngine/Specifications/UpgradeDiskSpecification`** — filter the enumeration by
   `subject.ParsedBookInfo.Quality.Quality.MediaType`.
4. **`DecisionEngine/Specifications/CutoffSpecification`** — same filter, and take the cutoff for
   that media type once stage 2 lands (until then, keep the single cutoff).
5. **`FixMultipleMonitoredEditions`** — partition by `(BookId, IsEbook)` instead of `BookId`, so one
   monitored edition per media type survives, and fix the Postgres branch to set `false`. Both
   dialects need the same semantics; the existing file already branches on `DatabaseType`.
6. **Delete `ImportApprovedBooks.RemoveExistingTrackFiles`** and the commented-out call above it
   (`ImportApprovedBooks.cs:128-131`, `:549`). It is dead code that deletes every file on a book;
   leaving it is a loaded gun for whoever re-enables `replaceExisting`.

Tests — these are the regression tests that pin the fork's headline feature:

- `UpgradeMediaFileServiceFixture`: importing an M4B when an EPUB exists deletes the EPUB **not at
  all**, and still deletes an existing MP3.
- `UpgradeSpecificationFixture` / `UpgradeDiskSpecificationFixture` / `CutoffSpecificationFixture`:
  an EPUB is accepted when the book's only file is an M4B.
- `FixMultipleMonitoredEditionsFixture`: a book with one monitored ebook edition and one monitored
  audiobook edition is left alone; a book with two monitored ebook editions is fixed.

`UpgradeMediaFileServiceFixture` currently has an `[Ignore("Pending readarr fix")]` test at line
126 — check whether the scoping change resolves it.

### Stage 2 — the quality profile gains a cutoff per media type

- `Profiles/Qualities/QualityProfileCutoffItem.cs` — new, `{ BookMediaType MediaType, int Quality }`.
- `QualityProfile`: replace `int Cutoff` with `List<QualityProfileCutoffItem> Cutoffs`; add
  `int? GetCutoff(BookMediaType)`, `FirstAllowedQuality(BookMediaType)`,
  `LastAllowedQuality(BookMediaType)`. A media type with no allowed quality has no cutoff entry and
  means "nothing of this type is wanted".
- `UpgradableSpecification.QualityCutoffNotMet` / `CutoffNotMet` — take the cutoff for the quality's
  own media type.
- `BookCutoffService` — build `QualitiesBelowCutoff` per (profile, media type);
  `BookRepository.BuildQualityCutoffWhereClause` gains the media-type dimension. Note it matches
  quality by `LIKE '%_quality_: {id},%'` against the serialised JSON, which stays workable.
- `QualityProfileService.Handle(ApplicationStartedEvent)` — seed a third default,
  **"eBook + Audiobook"**, allowing EPUB/AZW3/MOBI and M4B/MP3 with a cutoff for each. Keep "eBook"
  and "Spoken" for single-format users.
- `QualityModelComparer` — add a debug-level guard that logs when asked to compare across media
  types. After stage 1 no caller should; the guard catches the next one that does.
- **Migration 043** `add_media_type_cutoffs`: add the `Cutoffs` column, backfill each profile's
  existing `Cutoff` under the media type it belongs to, and seed the other media type with that
  type's highest allowed quality where one is allowed. Drop the old `Cutoff` column. Never edit 042.
- API + UI in the same stage so nothing is left half-migrated: `QualityProfileResource.Cutoffs`,
  and the quality-profile settings screen gets a cutoff selector per media type. New strings go in
  `Localization/Core/en.json` only.

### Stage 3 — statistics, wanted and missing become per-format

- `AuthorStats/BookStatistics.cs` — add per-media-type file counts and sizes. Cheapest shape that
  keeps the SQL one pass: group by `(Author, Book, Edition.IsEbook)` and fold in
  `AuthorStatisticsService`, rather than two queries.
- `AuthorStatisticsRepository` — carry `Editions.IsEbook` into the grouping.
- `BookRepository.BooksWithoutFiles` already joins per monitored edition, so a book missing one
  format does surface; what is missing is *which*. Project the media type through so Wanted can say
  "Audiobook missing" rather than "missing".
- `Readarr.Api.V1` — `BookStatisticsResource`, `AuthorStatisticsResource`, and `mediaType` on
  `EditionResource` alongside the existing `isEbook`.

### Stage 4 — naming and file management (ROADMAP step 5)

`RenameBooks` defaults to false and the user keeps it off, so this stage is inert until switched on
— but it is what makes the target layout reachable.

- `Organizer/NamingConfig.cs` — add `AudiobookFormat`, defaulting to today's `StandardBookFormat`
  value so existing behaviour is unchanged. `StandardBookFormat` becomes the ebook rule.
  Rationale for two strings rather than one with a token: a chapter-per-file MP3 audiobook needs its
  own subfolder (the user's Tolkien folder is 1310 files) while an epub belongs at the book-folder
  root. One format string cannot express both.
- `FileNameBuilder.BuildBookFileName` — select the pattern by `bookFile.Quality.Quality.MediaType`.
  `IBuildFileNames` signature is unchanged; the media type comes off the file it is already given.
- New tokens: `{Book MediaType}` → `Ebook`/`Audiobook`, and `{Book Format}` → the quality title
  (`EPUB`, `M4B`), so two audiobook editions can be told apart in one folder.
- `FileNameSampleService` — a sample per media type; `FileNameValidationService` and
  `BasicNamingConfig` follow.
- `NamingConfigResource` + the naming settings screen: second field, second sample.
- Do not mass-rename without `RenameBookController`'s preview. Three top-level folders are still raw
  release names and need manual author assignment first (see ROADMAP).

### Stage 5 — the rest of the API and frontend

Readarr's own UI matters less than its API here (Audiobookshelf is the frontend), so this stage is
API-first and the UI is the thin part.

- Book details: ebook and audiobook as separate rows with independent monitor toggles, driven by
  `MonitoredEditionFor(mediaType)`.
- Book index: media-type column and filter.
- Interactive search: show the release's media type.
- Wanted / Missing / Cutoff Unmet: media-type column.
- All new strings via `translate(...)` with keys added to `en.json` only — the other language files
  are Weblate-managed.

### Stage 6 — format coverage and release-side detection

- `MediaFiles/MediaFileExtensions.cs` — add `.azw`, `.fb2`, `.djvu`, `.lit`, `.pdb`, `.txt`, `.rtf`
  for text and `.opus` for audio. Stage 0 removed the id ceiling, so new qualities can take ids 14+
  regardless of media type. Decide deliberately whether `.aac`/`.ogg` keep mapping to `MP3` or get
  their own quality — mapping distinct containers onto one quality makes upgrades between them
  no-ops.
- `Parser/QualityParser` — recognise audiobook markers in release names ("Unabridged", "M4B",
  "audiobook", bitrate patterns) so a grab gets the right media type *before* download, which is
  what stages 1–2 key off.
- The slskd tuning noted in ROADMAP: `flac` in the default extension list pulls in soundtracks that
  merely match the title. A media-type-aware release filter is now cheap to add.

## Order and what each stage is worth

| Stage | Unblocks | Ship on its own? |
|---|---|---|
| 0 media type on `Quality` | everything | yes, inert |
| 1 scoped upgrades + housekeeper | **dual-format works at all**; stops data loss | yes |
| 2 per-media-type cutoff | upgrades and Wanted are correct per format | yes |
| 3 statistics | you can see what is missing | yes |
| 4 naming | the target folder layout | yes, inert until `RenameBooks` |
| 5 API/UI | Audiobookshelf and the web UI agree | yes |
| 6 formats + parsing | more formats, better grabs | yes |

Stages 0 and 1 are the ones that change whether the feature exists. If nothing else gets done, do
those two.

## Conventions that bind this work

From `CLAUDE.md`, repeated because each is a build break or a silent bug:

- 4 spaces, `TreatWarningsAsErrors` with StyleCop and `EnforceCodeStyleInBuild` — an unused using
  fails the build.
- Building a bare `.csproj` needs `-p:SolutionDir=<repo>/src/` or StyleCop floods `SA1200`.
- Migrations are append-only. Next is **043**.
- Never register services manually; never add a second implementation of the five metadata
  interfaces.
- Both SQLite and Postgres must keep working — `FixMultipleMonitoredEditions` and the statistics SQL
  both branch on dialect.
- `test.sh` always exits 0. Use `dotnet test` when the result matters.
- Commit messages for user-visible changes start with `New:` or `Fixed:`.
