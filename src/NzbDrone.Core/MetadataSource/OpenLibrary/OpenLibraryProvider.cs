using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Audible;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    /// <summary>
    /// In-process metadata provider. Open Library supplies author identity, the bibliography and
    /// text editions; Audible supplies audiobook editions, narrators, runtime and series sequence,
    /// which Open Library either lacks or indexes too inconsistently to rely on.
    /// </summary>
    public class OpenLibraryProvider : IBookMetadataProvider
    {
        // One /search.json call returns the whole bibliography. Walking /authors/{k}/works.json and
        // then each work's editions would be one request per work - 138 for Frank Herbert alone.
        private const int MaxWorksPerAuthor = 500;
        private const int MaxEditionsPerWork = 50;
        private const int MaxSearchResults = 25;
        private const int MaxAudibleResults = 50;

        // How much of the longer title the shorter one must account for before the two are treated
        // as the same book recorded twice.
        private const double ContainmentWordRatio = 0.6;

        private const string EnglishLanguageCode = "eng";

        // Credited authors at or above which a record is an anthology rather than the author's book.
        // Five, not four: "The Road to Dune" credits four and is a real book, where the anthologies
        // worth dropping ("Five Fates", "TV 2000" with nineteen) all credit five or more. A
        // four-author anthology therefore survives, which is the better way round to be wrong.
        private const int MinAuthorsForAnthology = 5;

        /// <summary>
        /// Things Open Library files against an author that nobody would download as a book. Matched
        /// against the raw title, case-insensitively.
        /// </summary>
        private static readonly Regex[] NonBookProductPatterns =
        {
            // Collections that ship several books as one product.
            new Regex(@"\bbox(ed)?[ -]?set\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bomnibus\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bcomplete series\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\b(complete|unpublished) novels\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bcollection-\d+\s*vol", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bset of \d+ books?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\b\d+[- ]book\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\(set\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Merchandise and adaptations.
            new Regex(@"\bcolou?ring book\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bcalendar\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bgraphic novel\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bcomic (bk|book)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bcassette\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bsparknotes\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bstudy guide\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bnotebooks of\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Retail display units and publisher stock codes: "Trudi Canavan Header Whsmith",
            // "Herbert 15mxpk", "Herbert Tie-in 20mfl".
            new Regex(@"\bwhsmith\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\b\d+m[a-z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // A single book split across volumes: "Dune Messiah (1 of 2)".
            new Regex(@"\(\d+ of \d+\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Magazine issues the author has a story in.
            new Regex(@"\banalog\b.*\b(19|20)\d\d\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\banalog science fiction\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bscience fiction magazine\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\bvolume [\divxl]+, no\.", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        /*
         * On the works `language=eng` leaves behind, and why nothing here tries to recover them.
         *
         * The filter is applied by the search endpoint rather than to the language field it projects
         * back, because most translation records carry no language field at all and a client-side
         * filter kept every one of them - 83 of Trudi Canavan's 131 works, leaving one real book in
         * five.
         *
         * The cost is that works Open Library holds no language data for are dropped too, and a few
         * are real English books: `Hunters of Dune` and `Hellhole Awakening` in the corpus behind
         * BibliographyAccuracyFixture.
         *
         * Confirming those against /works/{key}/editions.json was tried and does not work. That
         * endpoint has no language data for them either - `Hunters of Dune` has a single edition
         * whose `languages` is null, though its publisher is Hodder & Stoughton. Measured across all
         * 75 works in the corpus that would have been probed, the number recovered was zero. It is
         * one request per work for nothing.
         *
         * Title heuristics were tried too and leak badly: Polish, Turkish and Dutch titles routinely
         * carry neither an accent nor a leading article, so `Wielki Mistrz`, `Zlodziejska magia` and
         * `Misja ambasadora` all read as English.
         *
         * So two books in sixty-nine are knowingly given up. Anyone revisiting this needs a source
         * that actually knows the language - a second IBookMetadataProvider - not another pass over
         * the same empty field.
         */

        private readonly IOpenLibraryProxy _openLibrary;
        private readonly IAudibleProxy _audible;
        private readonly Logger _logger;

        public OpenLibraryProvider(IOpenLibraryProxy openLibrary, IAudibleProxy audible, Logger logger)
        {
            _openLibrary = openLibrary;
            _audible = audible;
            _logger = logger;
        }

        public MetadataProviderType ProviderType => MetadataProviderType.OpenLibrary;

        public Author GetAuthorInfo(string readarrId, bool useCache = true)
        {
            var key = OpenLibraryProxy.NormalizeKey(readarrId);
            var resource = _openLibrary.GetAuthor(key);

            if (resource == null)
            {
                throw new AuthorNotFoundException(readarrId);
            }

            var metadata = MapAuthorMetadata(resource, key);
            var docs = _openLibrary.GetWorksByAuthor(key, MaxWorksPerAuthor, EnglishLanguageCode);

            var audibleProducts = _audible.SearchByAuthor(metadata.Name, MaxAudibleResults);
            var audibleByTitle = IndexByTitle(audibleProducts);

            var books = DeduplicateWorks(docs)
                .Select(x => MapBook(x, audibleByTitle))
                .ToList();

            books.ForEach(x => x.AuthorMetadata = metadata);

            var series = BuildSeriesFromAudible(books, audibleProducts, audibleByTitle);

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = series
            };
        }

        /// <summary>
        /// Open Library files a good deal that is not a book against an author: calendars, colouring
        /// books, magazine issues the author has a story in, box sets, study guides, split volumes
        /// ("Dune Messiah (1 of 2)") and publisher stock codes ("Herbert 15mxpk").
        /// </summary>
        private static bool IsNonBookProduct(string title)
        {
            var normalized = title ?? string.Empty;

            return NonBookProductPatterns.Any(x => x.IsMatch(normalized));
        }

        /// <summary>
        /// Open Library holds several work records for the same book, and separate records for
        /// collections, so an author's bibliography arrives with noise in it. Two shapes are dealt
        /// with here, in this order:
        ///
        /// 1. Omnibus works, whose title is a list of other books by the same author -
        ///    "Dune, Dune Messiah, Children of Dune".
        /// 2. Duplicate works under differing titles, where one title wholly contains the other -
        ///    "Dune: The Butlerian Jihad" and "The Butlerian Jihad".
        ///
        /// The order is load bearing. Run the other way round, the omnibus swallows the real books
        /// it names, because it contains each of their titles.
        /// </summary>
        private static List<OpenLibrarySearchDoc> DeduplicateWorks(IEnumerable<OpenLibrarySearchDoc> docs)
        {
            var all = docs
                .Where(x => x.Key.IsNotNullOrWhiteSpace())
                .Where(x => !IsNonBookProduct(x.Title))
                .Where(x => !IsAnthologyContribution(x))
                .Select(x => new WorkTitle(x, NormalizeTitle(x.Title, stripSubtitle: false)))
                .Where(x => x.Normalized.IsNotNullOrWhiteSpace())
                .ToList();

            // Only multi-word titles are safe to look for inside another title. "Dune" occurs inside
            // "Dune Messiah", which is a different book.
            var phrases = all.Select(x => x.Normalized)
                .Where(x => WordCount(x) >= 2)
                .Distinct()
                .ToList();

            var singles = all
                .Where(x => phrases.Count(p => p != x.Normalized && ContainsPhrase(x.Normalized, p)) < 2)
                .ToList();

            var kept = new List<WorkTitle>();

            // Shortest title first, so the plain "The Butlerian Jihad" becomes the record a longer
            // variant is recognised against rather than the other way round.
            foreach (var candidate in singles.OrderBy(x => WordCount(x.Normalized)).ThenBy(x => x.Normalized.Length))
            {
                // An identical title always collapses. Finding one title *inside* another is only
                // the same book when the two are close in length.
                var existing = kept.FirstOrDefault(x =>
                    x.Normalized == candidate.Normalized ||
                    IsSameBookByContainment(candidate.Normalized, x.Normalized));

                if (existing == null)
                {
                    kept.Add(candidate);
                    continue;
                }

                // Same book twice: keep whichever record is richer, since that is the one more
                // likely to be matched by an indexer and to carry usable covers and dates.
                if (Richness(candidate.Doc) > Richness(existing.Doc))
                {
                    kept[kept.IndexOf(existing)] = new WorkTitle(candidate.Doc, existing.Normalized);
                }
            }

            return kept.Select(x => x.Doc).ToList();
        }

        /// <summary>
        /// An anthology the author contributed a single story to, rather than a book they wrote.
        /// Credited-author count is the usable signal: "TV 2000" lists nineteen, and one Frank
        /// Herbert record lists eighty-six.
        /// </summary>
        /// <remarks>
        /// The threshold has to clear genuine collaborations, which is why it is four rather than
        /// two: Frank Herbert's Pandora novels with Bill Ransom carry three credited authors and
        /// must survive.
        /// </remarks>
        private static bool IsAnthologyContribution(OpenLibrarySearchDoc doc)
        {
            return (doc.AuthorNames?.Count ?? 0) >= MinAuthorsForAnthology;
        }

        /// <summary>
        /// "Dune: The Butlerian Jihad" and "The Butlerian Jihad" are one book; "Harry Potter and
        /// the Goblet of Fire" and "Harry Potter" are not. Both are containments, so length alone
        /// decides: the shorter title has to account for most of the longer one.
        /// </summary>
        /// <remarks>
        /// Without this, a short generic work title swallows an entire series. Open Library carries
        /// a work called simply "Harry Potter", and every "Harry Potter and the ..." collapsed into
        /// it, leaving one book where there were seven.
        /// </remarks>
        private static bool IsSameBookByContainment(string longer, string shorter)
        {
            var shortWords = WordCount(shorter);
            var longWords = WordCount(longer);

            if (shortWords < 2 || longWords == 0)
            {
                return false;
            }

            return shortWords >= longWords * ContainmentWordRatio && ContainsPhrase(longer, shorter);
        }

        private static long Richness(OpenLibrarySearchDoc doc)
        {
            return ((doc.EditionKeys?.Count ?? 0) * 1000L) + (doc.RatingsCount ?? 0);
        }

        private static int WordCount(string value)
        {
            return value.IsNullOrWhiteSpace() ? 0 : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <summary>
        /// Whole-phrase containment on the normalised (lowercase, punctuation-free) titles, so
        /// "children of dune" matches inside "dune dune messiah children of dune" but "une" does not.
        /// </summary>
        private static bool ContainsPhrase(string haystack, string needle)
        {
            if (haystack.IsNullOrWhiteSpace() || needle.IsNullOrWhiteSpace())
            {
                return false;
            }

            return haystack == needle
                   || haystack.StartsWith(needle + " ", StringComparison.Ordinal)
                   || haystack.EndsWith(" " + needle, StringComparison.Ordinal)
                   || haystack.Contains(" " + needle + " ", StringComparison.Ordinal);
        }

        private sealed class WorkTitle
        {
            public WorkTitle(OpenLibrarySearchDoc doc, string normalized)
            {
                Doc = doc;
                Normalized = normalized;
            }

            public OpenLibrarySearchDoc Doc { get; }
            public string Normalized { get; }
        }

        /// <summary>
        /// Open Library has no "what changed since" feed. Returning null makes RefreshAuthorService
        /// fall back to its own ShouldRefresh heuristic, which is the pre-existing behaviour when
        /// the metadata server does not answer.
        /// </summary>
        public HashSet<string> GetChangedAuthors(DateTime startTime) => null;

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id)
        {
            var key = OpenLibraryProxy.NormalizeKey(id);
            var work = _openLibrary.GetWork(key);

            if (work == null)
            {
                throw new BookNotFoundException(id);
            }

            var authorKeys = (work.Authors ?? new List<OpenLibraryWorkAuthor>())
                .Where(x => x.Author?.Key.IsNotNullOrWhiteSpace() == true)
                .Select(x => OpenLibraryProxy.NormalizeKey(x.Author.Key))
                .Distinct()
                .ToList();

            var metadata = new List<AuthorMetadata>();

            foreach (var authorKey in authorKeys)
            {
                var authorResource = _openLibrary.GetAuthor(authorKey);

                if (authorResource != null)
                {
                    metadata.Add(MapAuthorMetadata(authorResource, authorKey));
                }
            }

            if (!metadata.Any())
            {
                throw new BookNotFoundException($"{id} has no resolvable author");
            }

            var primaryAuthor = metadata.First();
            var audibleProducts = _audible.Search($"{work.Title} {primaryAuthor.Name}", 10);
            var audibleByTitle = IndexByTitle(audibleProducts);

            var book = MapBook(work);
            book.AuthorMetadata = primaryAuthor;

            var editions = _openLibrary.GetEditions(key, MaxEditionsPerWork);
            var mapped = editions.Select(MapEdition).Where(x => x != null).ToList();

            AppendAudibleEditions(mapped, MatchAudible(book.Title, audibleByTitle));
            FinaliseEditions(book, mapped);

            // Called for its side effect: it populates book.SeriesLinks. The Series list itself is
            // not part of the tuple, matching how BookInfoProxy.PollBook uses MapSeriesLinks.
            BuildSeriesFromAudible(new List<Book> { book }, audibleProducts, audibleByTitle);

            return Tuple.Create(primaryAuthor.ForeignAuthorId, book, metadata);
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            var docs = _openLibrary.SearchAuthors(title);

            return RankAuthors(docs, title)
                .Select(MapAuthorStub)
                .ToList();
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            var lower = title.ToLowerInvariant().Trim();
            var split = lower.Split(':');

            if (split.Length == 2)
            {
                var prefix = split[0];
                var value = split[1].Trim();

                switch (prefix)
                {
                    case "isbn":
                        return SearchByIsbn(value);
                    case "asin":
                        return SearchByAsin(value);
                    case "author":
                        return SearchAuthorWorks(value);
                    case "work":
                    case "edition":
                        return SearchByWorkKey(value);
                }
            }

            var query = author.IsNotNullOrWhiteSpace() ? $"{title} {author}" : title;
            var docs = _openLibrary.SearchWorks(query, MaxSearchResults);

            return docs.Select(x => MapBookWithAuthor(x, null)).Where(x => x != null).ToList();
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            var edition = _openLibrary.GetEditionByIsbn(isbn);
            var workKey = edition?.Works?.FirstOrDefault()?.Key;

            if (workKey.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            return SearchByWorkKey(workKey);
        }

        public List<Book> SearchByAsin(string asin)
        {
            // Open Library does not index ASINs. Audible is the authority for them, and its ASIN is
            // what Readarr will have stored on an audiobook edition.
            var products = _audible.Search(asin, 5);
            var match = products.FirstOrDefault(x => x.Asin.Equals(asin, StringComparison.OrdinalIgnoreCase))
                        ?? products.FirstOrDefault();

            if (match == null)
            {
                return new List<Book>();
            }

            var authorName = match.Authors?.FirstOrDefault()?.Name;
            var docs = _openLibrary.SearchWorks($"{match.Title} {authorName}", 5);

            return docs.Take(1).Select(x => MapBookWithAuthor(x, null)).Where(x => x != null).ToList();
        }

        /// <summary>
        /// Goodreads IDs are meaningless to Open Library. Only reachable from the Goodreads import
        /// lists, which are a separate feature from this provider.
        /// </summary>
        public List<Book> SearchByGoodreadsBookId(int goodreadsId, bool getAllEditions)
        {
            _logger.Debug("Goodreads id {0} cannot be resolved against Open Library", goodreadsId);
            return new List<Book>();
        }

        public List<object> SearchForNewEntity(string title)
        {
            var result = new List<object>();

            foreach (var author in SearchForNewAuthor(title))
            {
                result.Add(author);
            }

            foreach (var book in SearchForNewBook(title, null, false))
            {
                result.Add(book);
            }

            return result;
        }

        private List<Book> SearchAuthorWorks(string authorQuery)
        {
            var author = RankAuthors(_openLibrary.SearchAuthors(authorQuery), authorQuery).FirstOrDefault();

            if (author == null)
            {
                return new List<Book>();
            }

            var docs = _openLibrary.GetWorksByAuthor(author.Key, MaxSearchResults);

            return docs.Select(x => MapBookWithAuthor(x, MapAuthorMetadataStub(author))).Where(x => x != null).ToList();
        }

        private List<Book> SearchByWorkKey(string workKey)
        {
            try
            {
                var info = GetBookInfo(workKey);
                var book = info.Item2;

                var author = new Author { Metadata = info.Item3.First() };
                author.CleanName = Parser.Parser.CleanAuthorName(author.Metadata.Value.Name);
                book.Author = author;

                return new List<Book> { book };
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
        }

        /// <summary>
        /// Open Library's author search ranks badly - a query for "Frank Herbert" puts
        /// "Frank Herbert Hayward" and "Simonds, Frank Herbert" above the actual Frank Herbert.
        /// Exact-name matches first, then by how much of the catalogue they account for.
        /// </summary>
        private static List<OpenLibraryAuthorSearchDoc> RankAuthors(List<OpenLibraryAuthorSearchDoc> docs, string query)
        {
            var normalizedQuery = NormalizeTitle(query);

            return docs
                .Where(x => x.Key.IsNotNullOrWhiteSpace() && x.Name.IsNotNullOrWhiteSpace())
                .OrderByDescending(x => NormalizeTitle(x.Name) == normalizedQuery)
                .ThenByDescending(x => NormalizeTitle(x.Name).StartsWith(normalizedQuery, StringComparison.Ordinal))
                .ThenByDescending(x => x.WorkCount)
                .ToList();
        }

        private static Author MapAuthorStub(OpenLibraryAuthorSearchDoc doc)
        {
            var metadata = MapAuthorMetadataStub(doc);

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = new List<Book>(),
                Series = new List<Series>()
            };
        }

        private static AuthorMetadata MapAuthorMetadataStub(OpenLibraryAuthorSearchDoc doc)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = doc.Key,
                TitleSlug = doc.Key,
                Name = doc.Name.CleanSpaces(),
                Aliases = doc.AlternateNames ?? new List<string>(),
                Born = ParseOpenLibraryDate(doc.BirthDate),
                Died = ParseOpenLibraryDate(doc.DeathDate),
                Status = doc.DeathDate.IsNotNullOrWhiteSpace() ? AuthorStatusType.Ended : AuthorStatusType.Continuing
            };

            ApplyNameForms(metadata);
            metadata.Links.Add(new Links { Url = $"https://openlibrary.org/authors/{doc.Key}", Name = "Open Library" });

            return metadata;
        }

        private static AuthorMetadata MapAuthorMetadata(OpenLibraryAuthorResource resource, string key)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = key,
                TitleSlug = key,
                Name = (resource.Name ?? resource.PersonalName ?? string.Empty).CleanSpaces(),
                Overview = resource.Bio,
                Aliases = resource.AlternateNames ?? new List<string>(),
                Born = ParseOpenLibraryDate(resource.BirthDate),
                Died = ParseOpenLibraryDate(resource.DeathDate),
                Status = resource.DeathDate.IsNotNullOrWhiteSpace() ? AuthorStatusType.Ended : AuthorStatusType.Continuing
            };

            ApplyNameForms(metadata);

            var photo = resource.Photos?.FirstOrDefault(x => x > 0);

            if (photo.HasValue)
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = $"https://covers.openlibrary.org/a/id/{photo.Value}-L.jpg",
                    CoverType = MediaCoverTypes.Poster
                });
            }

            metadata.Links.Add(new Links { Url = $"https://openlibrary.org/authors/{key}", Name = "Open Library" });

            return metadata;
        }

        private static void ApplyNameForms(AuthorMetadata metadata)
        {
            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();
        }

        private Book MapBookWithAuthor(OpenLibrarySearchDoc doc, AuthorMetadata known)
        {
            var metadata = known;

            if (metadata == null)
            {
                var authorKey = doc.AuthorKeys?.FirstOrDefault();
                var authorName = doc.AuthorNames?.FirstOrDefault();

                if (authorKey.IsNullOrWhiteSpace() || authorName.IsNullOrWhiteSpace())
                {
                    return null;
                }

                metadata = new AuthorMetadata
                {
                    ForeignAuthorId = authorKey,
                    TitleSlug = authorKey,
                    Name = authorName.CleanSpaces(),
                    Status = AuthorStatusType.Continuing
                };

                ApplyNameForms(metadata);
            }

            var book = MapBook(doc, new Dictionary<string, List<AudibleProductResource>>());
            book.AuthorMetadata = metadata;

            var author = new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name)
            };

            book.Author = author;

            return book;
        }

        private Book MapBook(OpenLibrarySearchDoc doc, Dictionary<string, List<AudibleProductResource>> audibleByTitle)
        {
            var workKey = OpenLibraryProxy.NormalizeKey(doc.Key);

            var book = new Book
            {
                ForeignBookId = workKey,
                TitleSlug = workKey,
                Title = doc.Title,
                CleanTitle = Parser.Parser.CleanAuthorName(doc.Title),
                Genres = doc.Subject?.Take(10).ToList() ?? new List<string>(),
                RelatedBooks = new List<int>(),
                ReleaseDate = doc.FirstPublishYear.HasValue
                    ? new DateTime(doc.FirstPublishYear.Value, 1, 1)
                    : (DateTime?)null,
                Ratings = new Ratings
                {
                    Votes = doc.RatingsCount ?? 0,
                    Value = (decimal)(doc.RatingsAverage ?? 0)
                }
            };

            book.Links.Add(new Links { Url = $"https://openlibrary.org/works/{workKey}", Name = "Open Library" });

            // The search index carries no per-edition detail, so synthesise one text edition from
            // the work-level fields. Full editions are fetched on demand in GetBookInfo.
            var editions = new List<Edition>
            {
                new Edition
                {
                    ForeignEditionId = workKey,
                    TitleSlug = workKey,
                    Title = doc.Title,
                    Isbn13 = doc.Isbn?.FirstOrDefault(x => x != null && x.Length == 13),
                    Language = PreferredLanguage(doc.Language),
                    Format = "ebook",
                    IsEbook = true,
                    PageCount = doc.NumberOfPagesMedian ?? 0,
                    ReleaseDate = book.ReleaseDate,
                    Ratings = book.Ratings,
                    Images = CoverImages(doc.CoverId),
                    Links = new List<Links> { new Links { Url = $"https://openlibrary.org/works/{workKey}", Name = "Open Library" } }
                }
            };

            AppendAudibleEditions(editions, MatchAudible(doc.Title, audibleByTitle));
            FinaliseEditions(book, editions);

            return book;
        }

        private Book MapBook(OpenLibraryWorkResource work)
        {
            var workKey = OpenLibraryProxy.NormalizeKey(work.Key);

            var book = new Book
            {
                ForeignBookId = workKey,
                TitleSlug = workKey,
                Title = work.Title,
                CleanTitle = Parser.Parser.CleanAuthorName(work.Title),
                Genres = work.Subjects?.Take(10).ToList() ?? new List<string>(),
                RelatedBooks = new List<int>(),
                Ratings = new Ratings()
            };

            book.Links.Add(new Links { Url = $"https://openlibrary.org/works/{workKey}", Name = "Open Library" });

            return book;
        }

        private Edition MapEdition(OpenLibraryEditionResource resource)
        {
            if (resource.Key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var editionKey = OpenLibraryProxy.NormalizeKey(resource.Key);

            return new Edition
            {
                ForeignEditionId = editionKey,
                TitleSlug = editionKey,
                Title = (resource.Title ?? string.Empty).CleanSpaces(),
                Isbn13 = resource.Isbn13?.FirstOrDefault(),
                Overview = resource.Description,
                Publisher = resource.Publishers?.FirstOrDefault(),
                PageCount = resource.NumberOfPages ?? 0,
                ReleaseDate = ParseOpenLibraryDate(resource.PublishDate),
                Language = resource.Languages?.FirstOrDefault()?.Key is string lang
                    ? OpenLibraryProxy.NormalizeKey(lang)
                    : null,
                Format = resource.PhysicalFormat ?? "ebook",
                IsEbook = true,
                Disambiguation = resource.Subtitle,
                Images = CoverImages(resource.Covers?.FirstOrDefault()),
                Ratings = new Ratings(),
                Links = new List<Links> { new Links { Url = $"https://openlibrary.org/books/{editionKey}", Name = "Open Library" } }
            };
        }

        /// <summary>
        /// Audiobooks become their own editions with IsEbook false, which is what makes the
        /// audiobook/ebook split expressible downstream.
        /// </summary>
        private void AppendAudibleEditions(List<Edition> editions, List<AudibleProductResource> products)
        {
            foreach (var product in products)
            {
                if (product.Asin.IsNullOrWhiteSpace() || editions.Any(x => x.Asin == product.Asin))
                {
                    continue;
                }

                var narrators = product.Narrators?.Select(x => x.Name).Where(x => x.IsNotNullOrWhiteSpace()).ToList()
                                ?? new List<string>();

                var edition = new Edition
                {
                    ForeignEditionId = product.Asin,
                    TitleSlug = product.Asin,
                    Asin = product.Asin,
                    Title = (product.Title ?? string.Empty).CleanSpaces(),
                    Overview = product.MerchandisingSummary,
                    Publisher = product.PublisherName,
                    ReleaseDate = product.ReleaseDate,
                    Language = product.Language,
                    Format = "Audiobook",
                    IsEbook = false,
                    Disambiguation = narrators.Any() ? $"Narrated by {string.Join(", ", narrators)}" : product.Subtitle,
                    PageCount = 0,
                    Ratings = new Ratings(),
                    Images = new List<MediaCover.MediaCover>(),
                    Links = new List<Links> { new Links { Url = $"https://www.audible.com/pd/{product.Asin}", Name = "Audible" } }
                };

                var image = product.ProductImages?.OrderByDescending(x => x.Key).Select(x => x.Value).FirstOrDefault();

                if (image.IsNotNullOrWhiteSpace())
                {
                    edition.Images.Add(new MediaCover.MediaCover { Url = image, CoverType = MediaCoverTypes.Cover });
                }

                editions.Add(edition);
            }
        }

        /// <summary>
        /// Monitors the best edition of each media type, so a book that exists as both an epub and
        /// an audiobook tracks both rather than forcing a choice. Readarr allows one monitored
        /// edition per media type.
        /// </summary>
        private static void FinaliseEditions(Book book, List<Edition> editions)
        {
            if (!editions.Any())
            {
                book.Editions = new List<Edition>();
                book.AnyEditionOk = true;
                return;
            }

            foreach (var edition in editions)
            {
                edition.Monitored = false;
            }

            foreach (var group in editions.GroupBy(x => x.MediaType()))
            {
                // Within text editions prefer one with an ISBN, since that is what identification
                // and most indexers can actually match on.
                var best = group.FirstOrDefault(x => x.Isbn13.IsNotNullOrWhiteSpace())
                           ?? group.FirstOrDefault(x => x.Asin.IsNotNullOrWhiteSpace())
                           ?? group.First();

                best.Monitored = true;
            }

            var preferred = editions.PrimaryEdition();

            if (book.Title.IsNullOrWhiteSpace())
            {
                book.Title = preferred.Title;
            }

            if (!book.ReleaseDate.HasValue)
            {
                var dated = editions.Where(x => x.ReleaseDate.HasValue).ToList();

                if (dated.Any())
                {
                    book.ReleaseDate = dated.Min(x => x.ReleaseDate.Value);
                }
            }

            book.Editions = editions;
            book.AnyEditionOk = true;
        }

        /// <summary>
        /// Series come from Audible: Open Library carries series on the work record but does not
        /// expose it through the search index, so using it would cost one request per work.
        /// </summary>
        private static List<Series> BuildSeriesFromAudible(List<Book> books,
            List<AudibleProductResource> products,
            Dictionary<string, List<AudibleProductResource>> audibleByTitle)
        {
            var series = new Dictionary<string, Series>();
            var links = new Dictionary<string, List<SeriesBookLink>>();

            foreach (var book in books)
            {
                book.SeriesLinks = new List<SeriesBookLink>();
            }

            foreach (var book in books)
            {
                foreach (var product in MatchAudible(book.Title, audibleByTitle))
                {
                    foreach (var audibleSeries in product.Series ?? new List<AudibleSeries>())
                    {
                        if (audibleSeries.Asin.IsNullOrWhiteSpace() || audibleSeries.Title.IsNullOrWhiteSpace())
                        {
                            continue;
                        }

                        if (!series.TryGetValue(audibleSeries.Asin, out var current))
                        {
                            current = new Series
                            {
                                ForeignSeriesId = audibleSeries.Asin,
                                Title = audibleSeries.Title,
                                Numbered = audibleSeries.Sequence.IsNotNullOrWhiteSpace()
                            };

                            series[audibleSeries.Asin] = current;
                            links[audibleSeries.Asin] = new List<SeriesBookLink>();
                        }

                        if (book.SeriesLinks.Value.Any(x => x.Series?.Value?.ForeignSeriesId == audibleSeries.Asin))
                        {
                            continue;
                        }

                        int.TryParse(audibleSeries.Sequence, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position);

                        var link = new SeriesBookLink
                        {
                            Book = book,
                            Series = current,
                            IsPrimary = true,
                            Position = audibleSeries.Sequence,
                            SeriesPosition = position
                        };

                        links[audibleSeries.Asin].Add(link);
                        book.SeriesLinks.Value.Add(link);
                    }
                }
            }

            foreach (var entry in series)
            {
                entry.Value.LinkItems = links[entry.Key];
                entry.Value.WorkCount = links[entry.Key].Count;
                entry.Value.PrimaryWorkCount = links[entry.Key].Count;
            }

            return series.Values.ToList();
        }

        private static Dictionary<string, List<AudibleProductResource>> IndexByTitle(List<AudibleProductResource> products)
        {
            var index = new Dictionary<string, List<AudibleProductResource>>();

            foreach (var product in products)
            {
                var key = NormalizeTitle(product.Title);

                if (key.IsNullOrWhiteSpace())
                {
                    continue;
                }

                if (!index.TryGetValue(key, out var list))
                {
                    list = new List<AudibleProductResource>();
                    index[key] = list;
                }

                list.Add(product);
            }

            return index;
        }

        private static List<AudibleProductResource> MatchAudible(string title, Dictionary<string, List<AudibleProductResource>> index)
        {
            var key = NormalizeTitle(title);

            if (key.IsNullOrWhiteSpace() || !index.TryGetValue(key, out var matches))
            {
                return new List<AudibleProductResource>();
            }

            return matches;
        }

        /// <summary>
        /// The search index's "language" is an unordered aggregate over every edition of the work,
        /// so the first entry is arbitrary - Children of Dune leads with "pol", Heretics with
        /// "rus". Taking it literally makes the synthetic edition look Polish and the default
        /// "eng, null" metadata profile then discards the only text edition, leaving books with
        /// nothing but their Audible audiobook.
        ///
        /// The synthetic edition stands in for "the edition you would want", so prefer English
        /// when the work has an English edition at all. A work with no English edition still
        /// reports its actual language and is still filtered out correctly.
        /// </summary>
        private static string PreferredLanguage(List<string> languages)
        {
            if (languages == null || !languages.Any())
            {
                return null;
            }

            return languages.Contains("eng") ? "eng" : languages.First();
        }

        private static List<MediaCover.MediaCover> CoverImages(int? coverId)
        {
            var images = new List<MediaCover.MediaCover>();

            if (coverId.HasValue && coverId.Value > 0)
            {
                images.Add(new MediaCover.MediaCover
                {
                    Url = $"https://covers.openlibrary.org/b/id/{coverId.Value}-L.jpg",
                    CoverType = MediaCoverTypes.Cover
                });
            }

            return images;
        }

        /// <summary>
        /// Titles are compared across two sources that punctuate and subtitle differently, so
        /// strip to letters, digits and single spaces, and drop anything after a colon.
        /// </summary>
        private static string NormalizeTitle(string title)
        {
            return NormalizeTitle(title, stripSubtitle: true);
        }

        /// <summary>
        /// Dropping the subtitle is right when matching against Audible, where "Dune: Book One" and
        /// "Dune" are the same product. It is wrong for deduplication: "Dune: The Butlerian Jihad"
        /// would reduce to "dune" and never be recognised as the same book as "The Butlerian Jihad".
        /// </summary>
        private static string NormalizeTitle(string title, bool stripSubtitle)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var colon = title.IndexOf(':');
            var trimmed = stripSubtitle && colon > 0 ? title.Substring(0, colon) : title;

            var builder = new StringBuilder(trimmed.Length);
            var lastWasSpace = false;

            foreach (var c in trimmed)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                    lastWasSpace = false;
                }
                else if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Open Library dates are free text: "1920", "8 October 1920", "October 8, 1920".
        /// </summary>
        private static DateTime? ParseOpenLibraryDate(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            var year = new string(value.Where(char.IsDigit).ToArray());

            if (year.Length >= 4 && int.TryParse(year.Substring(0, 4), out var yearValue) && yearValue > 0 && yearValue <= DateTime.UtcNow.Year)
            {
                return new DateTime(yearValue, 1, 1);
            }

            return null;
        }
    }
}
