using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface ICandidateService
    {
        List<CandidateEdition> GetDbCandidatesFromTags(LocalEdition localEdition, IdentificationOverrides idOverrides, bool includeExisting);
        IEnumerable<CandidateEdition> GetRemoteCandidates(LocalEdition localEdition, IdentificationOverrides idOverrides);
    }

    public class CandidateService : ICandidateService
    {
        private readonly ISearchForNewBook _bookSearchService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        private static readonly Regex SeriesPrefixRegex = new Regex(@"^.*\d[\d.]*\s*-\s*(?<title>.+)$", RegexOptions.Compiled);

        public CandidateService(ISearchForNewBook bookSearchService,
                                IAuthorService authorService,
                                IBookService bookService,
                                IEditionService editionService,
                                IMediaFileService mediaFileService,
                                Logger logger)
        {
            _bookSearchService = bookSearchService;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public List<CandidateEdition> GetDbCandidatesFromTags(LocalEdition localEdition, IdentificationOverrides idOverrides, bool includeExisting)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Generally author, book and release are null.  But if they're not then limit candidates appropriately.
            // We've tried to make sure that tracks are all for a single release.
            List<CandidateEdition> candidateReleases;

            // if we have a Book ID, use that
            Book tagMbidRelease = null;
            List<CandidateEdition> tagCandidate = null;

            // TODO: select by ISBN?
            // var releaseIds = localEdition.LocalTracks.Select(x => x.FileTrackInfo.ReleaseMBId).Distinct().ToList();
            // if (releaseIds.Count == 1 && releaseIds[0].IsNotNullOrWhiteSpace())
            // {
            //     _logger.Debug("Selecting release from consensus ForeignReleaseId [{0}]", releaseIds[0]);
            //     tagMbidRelease = _releaseService.GetReleaseByForeignReleaseId(releaseIds[0], true);

            //     if (tagMbidRelease != null)
            //     {
            //         tagCandidate = GetDbCandidatesByRelease(new List<BookRelease> { tagMbidRelease }, includeExisting);
            //     }
            // }
            if (idOverrides?.Edition != null)
            {
                var release = idOverrides.Edition;
                _logger.Debug("Edition {0} was forced", release);
                candidateReleases = GetDbCandidatesByEdition(new List<Edition> { release }, includeExisting);
            }
            else if (idOverrides?.Book != null)
            {
                // use the release from file tags if it exists and agrees with the specified book
                if (tagMbidRelease?.Id == idOverrides.Book.Id)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByBook(idOverrides.Book, includeExisting);
                }
            }
            else if (idOverrides?.Author != null)
            {
                // use the release from file tags if it exists and agrees with the specified book
                if (tagMbidRelease?.AuthorMetadataId == idOverrides.Author.AuthorMetadataId)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByAuthor(localEdition, idOverrides.Author, includeExisting);
                }
            }
            else
            {
                if (tagMbidRelease != null)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidates(localEdition, includeExisting);
                }
            }

            watch.Stop();
            _logger.Debug($"Getting {candidateReleases.Count} candidates from tags for {localEdition.LocalBooks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return candidateReleases;
        }

        private List<CandidateEdition> GetDbCandidatesByEdition(List<Edition> editions, bool includeExisting)
        {
            // get the local tracks on disk for each book
            var bookFiles = editions.Select(x => x.BookId)
                .Distinct()
                .ToDictionary(id => id, id => includeExisting ? _mediaFileService.GetFilesByBook(id) : new List<BookFile>());

            return editions.Select(x => new CandidateEdition
            {
                Edition = x,
                ExistingFiles = bookFiles[x.BookId]
            }).ToList();
        }

        private List<CandidateEdition> GetDbCandidatesByBook(Book book, bool includeExisting)
        {
            // Sort by most voted so less likely to swap to a random release
            return GetDbCandidatesByEdition(_editionService.GetEditionsByBook(book.Id)
                                            .OrderByDescending(x => x.Ratings.Popularity)
                                            .ToList(), includeExisting);
        }

        private List<CandidateEdition> GetDbCandidatesByAuthor(LocalEdition localEdition, Author author, bool includeExisting)
        {
            _logger.Trace("Getting candidates for {0}", author);
            var candidateReleases = new List<CandidateEdition>();

            // The album tag is the usual source for the title, but a chapter-per-file audiobook
            // often has no tags at all, so the containing folder is tried as well.
            foreach (var bookTag in GetBookTitleCandidates(localEdition))
            {
                var possibleBooks = _bookService.GetCandidates(author.AuthorMetadataId, bookTag);
                foreach (var book in possibleBooks)
                {
                    candidateReleases.AddRange(GetDbCandidatesByBook(book, includeExisting));
                }

                var possibleEditions = _editionService.GetCandidates(author.AuthorMetadataId, bookTag);
                candidateReleases.AddRange(GetDbCandidatesByEdition(possibleEditions, includeExisting));
            }

            return candidateReleases;
        }

        private List<CandidateEdition> GetDbCandidates(LocalEdition localEdition, bool includeExisting)
        {
            // most general version, nothing has been specified.
            // get all plausible authors, then all plausible books, then get releases for each of these.
            var candidateReleases = new List<CandidateEdition>();

            // check if it looks like VA.
            if (TrackGroupingService.IsVariousAuthors(localEdition.LocalBooks))
            {
                var va = _authorService.FindById(DistanceCalculator.VariousAuthorIds[0]);
                if (va != null)
                {
                    candidateReleases.AddRange(GetDbCandidatesByAuthor(localEdition, va, includeExisting));
                }
            }

            var authorTags = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.Authors) ?? new List<string>();
            if (authorTags.Any())
            {
                var variants = DistanceCalculator.GetAuthorVariants(authorTags.Where(x => x.IsNotNullOrWhiteSpace()).ToList());

                foreach (var authorTag in variants)
                {
                    if (authorTag.IsNotNullOrWhiteSpace())
                    {
                        var possibleAuthors = _authorService.GetCandidates(authorTag);
                        foreach (var author in possibleAuthors)
                        {
                            candidateReleases.AddRange(GetDbCandidatesByAuthor(localEdition, author, includeExisting));
                        }
                    }
                }
            }

            // Audiobooks are frequently untaggable in practice: a chapter-per-file rip often has
            // empty tags entirely, and many carry the *narrator* in the artist field rather than
            // the author. In a library laid out as <root>/<Author>/<Book>/files the path is the
            // more reliable signal, so it is used in addition to the tags rather than only as a
            // fallback - a wrong tag would otherwise win over a correct folder.
            foreach (var name in GetPathNameCandidates(localEdition))
            {
                foreach (var author in _authorService.GetCandidates(name))
                {
                    candidateReleases.AddRange(GetDbCandidatesByAuthor(localEdition, author, includeExisting));
                }
            }

            return candidateReleases;
        }

        /// <summary>
        /// Titles worth trying for the book, most trustworthy first: the album tag, then the name
        /// of the folder holding the files.
        /// </summary>
        private static List<string> GetBookTitleCandidates(LocalEdition localEdition)
        {
            var candidates = new List<string>();

            var bookTag = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "";

            if (bookTag.IsNotNullOrWhiteSpace())
            {
                candidates.Add(bookTag);
            }

            foreach (var name in GetPathNameCandidates(localEdition))
            {
                Add(candidates, name);

                // A folder named "<Series> <position> - <Title>" is a common convention, and this
                // library uses it throughout ("Dune 0.1 - The Butlerian Jihad"). Offer the title on
                // its own as well, otherwise "The Lord of the Rings 1 - The Fellowship of the Ring"
                // scores against the omnibus "The Lord of the Rings" rather than the actual book.
                Add(candidates, StripSeriesPrefix(name));
            }

            return candidates;
        }

        private static void Add(List<string> candidates, string value)
        {
            if (value.IsNotNullOrWhiteSpace() && !candidates.Contains(value, StringComparer.InvariantCultureIgnoreCase))
            {
                candidates.Add(value);
            }
        }

        /// <summary>
        /// Returns the part after the separator when a folder is named "&lt;Series&gt; &lt;position&gt; - &lt;Title&gt;",
        /// otherwise null. The position is what distinguishes this from a title that merely
        /// contains a dash, so a digit is required before the separator.
        /// </summary>
        private static string StripSeriesPrefix(string name)
        {
            var match = SeriesPrefixRegex.Match(name ?? string.Empty);

            return match.Success ? match.Groups["title"].Value.Trim() : null;
        }

        /// <summary>
        /// Folder names above the files, nearest first. For &lt;root&gt;/&lt;Author&gt;/&lt;Book&gt;/file.mp3 that is
        /// the book folder then the author folder, which covers both that layout and the flatter
        /// &lt;root&gt;/&lt;Author&gt;/file.epub one without needing to know where the root is.
        /// </summary>
        private static List<string> GetPathNameCandidates(LocalEdition localEdition)
        {
            var names = new List<string>();

            foreach (var track in localEdition.LocalBooks)
            {
                if (track.Path.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var dir = Path.GetDirectoryName(track.Path);

                for (var i = 0; i < 2 && dir.IsNotNullOrWhiteSpace(); i++)
                {
                    var name = Path.GetFileName(dir);

                    if (name.IsNotNullOrWhiteSpace() && !names.Contains(name, StringComparer.InvariantCultureIgnoreCase))
                    {
                        names.Add(name);
                    }

                    dir = Path.GetDirectoryName(dir);
                }
            }

            return names;
        }

        public IEnumerable<CandidateEdition> GetRemoteCandidates(LocalEdition localEdition, IdentificationOverrides idOverrides)
        {
            // TODO handle edition override

            // Gets candidate book releases from the metadata server.
            // Will eventually need adding locally if we find a match
            List<Book> remoteBooks;
            var seenCandidates = new HashSet<string>();

            var isbns = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Isbn).Distinct().ToList();
            var asins = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Asin).Distinct().ToList();
            var goodreads = localEdition.LocalBooks.Select(x => x.FileTrackInfo.GoodreadsId).Distinct().ToList();

            // grab possibilities for all the IDs present
            if (isbns.Count == 1 && isbns[0].IsNotNullOrWhiteSpace())
            {
                _logger.Trace($"Searching by isbn {isbns[0]}");

                try
                {
                    remoteBooks = _bookSearchService.SearchByIsbn(isbns[0]);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping ISBN search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (asins.Count == 1 &&
                asins[0].IsNotNullOrWhiteSpace() &&
                asins[0].Length == 10)
            {
                _logger.Trace($"Searching by asin {asins[0]}");

                try
                {
                    remoteBooks = _bookSearchService.SearchByAsin(asins[0]);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping ASIN search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (goodreads.Count == 1 &&
                goodreads[0].IsNotNullOrWhiteSpace())
            {
                if (int.TryParse(goodreads[0], out var id))
                {
                    _logger.Trace($"Searching by goodreads id {id}");

                    try
                    {
                        remoteBooks = _bookSearchService.SearchByGoodreadsBookId(id, true);
                    }
                    catch (GoodreadsException e)
                    {
                        _logger.Info(e, "Skipping Goodreads ID search due to Goodreads Error");
                        remoteBooks = new List<Book>();
                    }

                    foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                    {
                        yield return candidate;
                    }
                }
            }

            // If we got an id result, or any overrides are set, stop
            if (seenCandidates.Any() ||
                idOverrides?.Edition != null ||
                idOverrides?.Book != null ||
                idOverrides?.Author != null)
            {
                yield break;
            }

            // fall back to author / book name search
            var authorTags = new List<string>();

            if (TrackGroupingService.IsVariousAuthors(localEdition.LocalBooks))
            {
                authorTags.Add("Various Authors");
            }
            else
            {
                // the most common list of authors reported by a file
                var authors = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Authors.Where(a => a.IsNotNullOrWhiteSpace()).ToList())
                    .GroupBy(x => x.ConcatToString())
                    .OrderByDescending(x => x.Count())
                    .First()
                    .First();
                authorTags.AddRange(authors);
            }

            var bookTag = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "";

            // If no valid author or book tags, stop
            if (!authorTags.Any() || bookTag.IsNullOrWhiteSpace())
            {
                yield break;
            }

            // Search by author+book
            foreach (var authorTag in authorTags)
            {
                try
                {
                    remoteBooks = _bookSearchService.SearchForNewBook(bookTag, authorTag);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping author/title search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            // If we got an author/book search result, stop
            if (seenCandidates.Any())
            {
                yield break;
            }

            // Search by just book title
            try
            {
                remoteBooks = _bookSearchService.SearchForNewBook(bookTag, null);
            }
            catch (GoodreadsException e)
            {
                _logger.Info(e, "Skipping book title search due to Goodreads Error");
                remoteBooks = new List<Book>();
            }

            foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
            {
                yield return candidate;
            }

            // Search by just author
            foreach (var a in authorTags)
            {
                try
                {
                    remoteBooks = _bookSearchService.SearchForNewBook(a, null);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping author search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }
        }

        private List<CandidateEdition> ToCandidates(IEnumerable<Book> books, HashSet<string> seenCandidates, IdentificationOverrides idOverrides)
        {
            var candidates = new List<CandidateEdition>();

            foreach (var book in books)
            {
                // We have to make sure various bits and pieces are populated that are normally handled
                // by a database lazy load
                foreach (var edition in book.Editions.Value)
                {
                    edition.Book = book;

                    if (!seenCandidates.Contains(edition.ForeignEditionId) && SatisfiesOverride(edition, idOverrides))
                    {
                        seenCandidates.Add(edition.ForeignEditionId);
                        candidates.Add(new CandidateEdition
                        {
                            Edition = edition,
                            ExistingFiles = new List<BookFile>()
                        });
                    }
                }
            }

            return candidates;
        }

        private bool SatisfiesOverride(Edition edition, IdentificationOverrides idOverride)
        {
            if (idOverride?.Edition != null)
            {
                return edition.ForeignEditionId == idOverride.Edition.ForeignEditionId;
            }

            if (idOverride?.Book != null)
            {
                return edition.Book.Value.ForeignBookId == idOverride.Book.ForeignBookId;
            }

            if (idOverride?.Author != null)
            {
                return edition.Book.Value.Author.Value.ForeignAuthorId == idOverride.Author.ForeignAuthorId;
            }

            return true;
        }
    }
}
