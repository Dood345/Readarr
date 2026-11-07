using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Parser
{
    public interface IParsingService
    {
        Author GetAuthor(string title);
        RemoteBook Map(ParsedBookInfo parsedBookInfo, SearchCriteriaBase searchCriteria = null);
        RemoteBook Map(ParsedBookInfo parsedBookInfo, int authorId, IEnumerable<int> bookIds);
        List<Book> GetBooks(ParsedBookInfo parsedBookInfo, Author author, SearchCriteriaBase searchCriteria = null);

        ParsedBookInfo ParseBookTitleFuzzy(string title);

        // Music stuff here
        Book GetLocalBook(string filename, Author author);
    }

    public class ParsingService : IParsingService
    {
        // "Dominion of Blades, Book 2", "Dominion of Blades: Vol. 2", "Dominion of Blades #2"
        private const string SeriesPositionSuffix = @"\s*(?:[,:#\-–—]\s*(?:book|bk|vol|volume|part|no)?|(?:book|bk|vol|volume|part|no))\.?\s*#?\s*0*(?<position>\d+(?:\.\d+)?)\b";

        // "Vol 1 - Dominion of Blades", "01 Dominion of Blades", "Book 1: Dominion of Blades"
        private const string SeriesPositionPrefix = @"(?<![\w.])(?:(?:book|bk|vol|volume|part|no)\.?\s*)?#?0*(?<position>\d+(?:\.\d+)?)\s*[-:.,–—]?\s*";

        // "2 Dominion of Blades Books" is a bundle, not book 2
        private const string NotABundle = @"\b(?!\s*(?:books|novels|audiobooks|ebooks|series|collection|set|bundle|boxset)\b)";

        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly ISeriesService _seriesService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public ParsingService(IAuthorService authorService,
                              IBookService bookService,
                              IEditionService editionService,
                              ISeriesService seriesService,
                              IMediaFileService mediaFileService,
                              Logger logger)
        {
            _bookService = bookService;
            _editionService = editionService;
            _seriesService = seriesService;
            _authorService = authorService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public Author GetAuthor(string title)
        {
            var parsedBookInfo = Parser.ParseBookTitle(title);

            if (parsedBookInfo != null && !parsedBookInfo.AuthorName.IsNullOrWhiteSpace())
            {
                title = parsedBookInfo.AuthorName;
            }

            var authorInfo = _authorService.FindByName(title);

            if (authorInfo == null)
            {
                _logger.Debug("Trying inexact author match for {0}", title);
                authorInfo = _authorService.FindByNameInexact(title);
            }

            return authorInfo;
        }

        public RemoteBook Map(ParsedBookInfo parsedBookInfo, SearchCriteriaBase searchCriteria = null)
        {
            var remoteBook = new RemoteBook
            {
                ParsedBookInfo = parsedBookInfo,
            };

            var author = GetAuthor(parsedBookInfo, searchCriteria);

            if (author == null)
            {
                return remoteBook;
            }

            remoteBook.Author = author;
            remoteBook.Books = GetBooks(parsedBookInfo, author, searchCriteria);

            return remoteBook;
        }

        public List<Book> GetBooks(ParsedBookInfo parsedBookInfo, Author author, SearchCriteriaBase searchCriteria = null)
        {
            var bookTitle = parsedBookInfo.BookTitle;
            var result = new List<Book>();

            if (parsedBookInfo.BookTitle == null)
            {
                return new List<Book>();
            }

            Book bookInfo = null;

            if (parsedBookInfo.Discography)
            {
                if (parsedBookInfo.DiscographyStart > 0)
                {
                    return _bookService.AuthorBooksBetweenDates(author,
                        new DateTime(parsedBookInfo.DiscographyStart, 1, 1),
                        new DateTime(parsedBookInfo.DiscographyEnd, 12, 31),
                        false);
                }

                if (parsedBookInfo.DiscographyEnd > 0)
                {
                    return _bookService.AuthorBooksBetweenDates(author,
                        new DateTime(1800, 1, 1),
                        new DateTime(parsedBookInfo.DiscographyEnd, 12, 31),
                        false);
                }

                return _bookService.GetBooksByAuthor(author.Id);
            }

            // A release that names a series position ("The Hobgoblin Riot: Dominion of Blades, Book 2") is that
            // book, even when the searched book's title ("Dominion of Blades") also appears in it as the series name.
            bookInfo = FindBySeriesPosition(author, parsedBookInfo);

            if (bookInfo == null && searchCriteria != null)
            {
                var cleanTitle = Parser.CleanAuthorName(parsedBookInfo.BookTitle);
                bookInfo = searchCriteria.Books.ExclusiveOrDefault(e => e.Title == bookTitle || e.CleanTitle == cleanTitle);
            }

            if (bookInfo == null)
            {
                // TODO: Search by Title and Year instead of just Title when matching
                bookInfo = _bookService.FindByTitle(author.AuthorMetadataId, parsedBookInfo.BookTitle);
            }

            if (bookInfo == null)
            {
                var edition = _editionService.FindByTitle(author.AuthorMetadataId, parsedBookInfo.BookTitle);
                bookInfo = edition?.Book.Value;
            }

            if (bookInfo == null)
            {
                _logger.Debug("Trying inexact book match for {0}", parsedBookInfo.BookTitle);
                bookInfo = _bookService.FindByTitleInexact(author.AuthorMetadataId, parsedBookInfo.BookTitle);
            }

            if (bookInfo == null)
            {
                _logger.Debug("Trying inexact edition match for {0}", parsedBookInfo.BookTitle);
                var edition = _editionService.FindByTitleInexact(author.AuthorMetadataId, parsedBookInfo.BookTitle);
                bookInfo = edition?.Book.Value;
            }

            if (bookInfo != null)
            {
                result.Add(bookInfo);
            }
            else
            {
                _logger.Debug("Unable to find {0}", parsedBookInfo);
            }

            return result;
        }

        private Book FindBySeriesPosition(Author author, ParsedBookInfo parsedBookInfo)
        {
            var title = parsedBookInfo.ReleaseTitle.IsNotNullOrWhiteSpace() ? parsedBookInfo.ReleaseTitle : parsedBookInfo.BookTitle;

            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var series = _seriesService.GetByAuthorMetadataId(author.AuthorMetadataId)?
                .Where(s => s.Title.IsNotNullOrWhiteSpace())
                .OrderByDescending(s => s.Title.Length)
                .ToList();

            if (series == null || series.Empty())
            {
                return null;
            }

            List<Book> authorBooks = null;

            foreach (var s in series)
            {
                var seriesPattern = string.Join(@"\W+", s.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
                var match = Regex.Match(title, @"\b" + seriesPattern + SeriesPositionSuffix, RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    match = Regex.Match(title, SeriesPositionPrefix + seriesPattern + NotABundle, RegexOptions.IgnoreCase);
                }

                if (!match.Success)
                {
                    continue;
                }

                var position = decimal.Parse(match.Groups["position"].Value, CultureInfo.InvariantCulture);

                var link = s.LinkItems.Value.FirstOrDefault(l => decimal.TryParse(l.Position, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) && p == position);

                if (link == null)
                {
                    continue;
                }

                authorBooks ??= _bookService.GetBooksByAuthorMetadataId(author.AuthorMetadataId);
                var book = authorBooks.FirstOrDefault(b => b.Id == link.BookId);

                if (book != null)
                {
                    _logger.Debug("Matched '{0}' to {1} as {2} #{3}", title, book, s.Title, link.Position);
                    return book;
                }
            }

            return null;
        }

        public RemoteBook Map(ParsedBookInfo parsedBookInfo, int authorId, IEnumerable<int> bookIds)
        {
            return new RemoteBook
            {
                ParsedBookInfo = parsedBookInfo,
                Author = _authorService.GetAuthor(authorId),
                Books = _bookService.GetBooks(bookIds)
            };
        }

        private Author GetAuthor(ParsedBookInfo parsedBookInfo, SearchCriteriaBase searchCriteria)
        {
            Author author = null;

            if (searchCriteria != null)
            {
                if (searchCriteria.Author.CleanName == parsedBookInfo.AuthorName.CleanAuthorName())
                {
                    return searchCriteria.Author;
                }
            }

            author = _authorService.FindByName(parsedBookInfo.AuthorName);

            if (author == null)
            {
                _logger.Debug("Trying inexact author match for {0}", parsedBookInfo.AuthorName);
                author = _authorService.FindByNameInexact(parsedBookInfo.AuthorName);
            }

            if (author == null)
            {
                _logger.Debug("No matching author {0}", parsedBookInfo.AuthorName);
                return null;
            }

            return author;
        }

        public ParsedBookInfo ParseBookTitleFuzzy(string title)
        {
            var bestScore = 0.0;

            Author bestAuthor = null;
            Book bestBook = null;

            var possibleAuthors = _authorService.GetReportCandidates(title);

            foreach (var author in possibleAuthors)
            {
                _logger.Trace($"Trying possible author {author}");

                var authorMatch = title.FuzzyMatch(author.Metadata.Value.Name, 0.5);
                var possibleBooks = _bookService.GetCandidates(author.AuthorMetadataId, title);

                foreach (var book in possibleBooks)
                {
                    var bookMatch = title.FuzzyMatch(book.Title, 0.5);
                    var score = (authorMatch.Item3 + bookMatch.Item3) / 2;

                    _logger.Trace($"Book {book} has score {score}");

                    if (score > bestScore)
                    {
                        bestAuthor = author;
                        bestBook = book;
                    }
                }

                var possibleEditions = _editionService.GetCandidates(author.AuthorMetadataId, title);
                foreach (var edition in possibleEditions)
                {
                    var editionMatch = title.FuzzyMatch(edition.Title, 0.5);
                    var score = (authorMatch.Item3 + editionMatch.Item3) / 2;

                    _logger.Trace($"Edition {edition} has score {score}");

                    if (score > bestScore)
                    {
                        bestAuthor = author;
                        bestBook = edition.Book.Value;
                    }
                }
            }

            _logger.Trace($"Best match: {bestAuthor} {bestBook}");

            if (bestAuthor != null)
            {
                return Parser.ParseBookTitleWithSearchCriteria(title, bestAuthor, new List<Book> { bestBook });
            }

            return null;
        }

        public Book GetLocalBook(string filename, Author author)
        {
            if (Path.HasExtension(filename))
            {
                filename = Path.GetDirectoryName(filename);
            }

            var tracksInBook = _mediaFileService.GetFilesByAuthor(author.Id)
                .FindAll(s => Path.GetDirectoryName(s.Path) == filename)
                .DistinctBy(s => s.EditionId)
                .ToList();

            return tracksInBook.Count == 1 ? _bookService.GetBook(tracksInBook.First().EditionId) : null;
        }
    }
}
