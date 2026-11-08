using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class GetBooksBySeriesPositionFixture : CoreTest<ParsingService>
    {
        private Author _author;
        private Book _bookOne;
        private Book _bookTwo;
        private BookSearchCriteria _criteria;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                .With(a => a.Name = "Matt Dinniman")
                .With(a => a.AuthorMetadataId = 7)
                .Build();

            _bookOne = GivenBook(1, "Dominion of Blades");
            _bookTwo = GivenBook(2, "The Hobgoblin Riot");

            var series = Builder<Series>.CreateNew()
                .With(s => s.Id = 5)
                .With(s => s.Title = "Dominion of Blades")
                .With(s => s.LinkItems = new List<SeriesBookLink>
                {
                    new SeriesBookLink { SeriesId = 5, BookId = _bookOne.Id, Position = "1" },
                    new SeriesBookLink { SeriesId = 5, BookId = _bookTwo.Id, Position = "2" }
                })
                .Build();

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.GetByAuthorMetadataId(_author.AuthorMetadataId))
                .Returns(new List<Series> { series });

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthorMetadataId(_author.AuthorMetadataId))
                .Returns(new List<Book> { _bookOne, _bookTwo });

            _criteria = new BookSearchCriteria
            {
                Author = _author,
                Books = new List<Book> { _bookOne }
            };
        }

        private static Book GivenBook(int id, string title)
        {
            return Builder<Book>.CreateNew()
                .With(b => b.Id = id)
                .With(b => b.Title = title)
                .With(b => b.CleanTitle = Parser.Parser.CleanAuthorName(title))
                .With(b => b.Editions = new List<Edition> { new Edition { Title = title, Monitored = true } })
                .Build();
        }

        // What DownloadDecisionMaker does: the plain parser first, the search-criteria parser when it fails.
        private ParsedBookInfo Parse(string releaseTitle)
        {
            return Parser.Parser.ParseBookTitle(releaseTitle) ?? Parser.Parser.ParseBookTitleWithSearchCriteria(releaseTitle, _author, _criteria.Books);
        }

        [TestCase("Matt Dinniman - The Hobgoblin Riot Dominion of Blades, Book 2 (Unabridged) [M4B]")]
        [TestCase("Matt Dinniman - The Hobgoblin Riot: Dominion of Blades, Book 2")]
        [TestCase("Matt Dinniman - The Hobgoblin Riot (Dominion of Blades #2)")]
        [TestCase("Matt Dinniman - Dominion of Blades Vol. 2 - The Hobgoblin Riot")]
        [TestCase("Matt Dinniman - Book 2 - Dominion of Blades - The Hobgoblin Riot")]
        public void should_map_release_naming_a_series_position_to_that_book_not_the_searched_one(string releaseTitle)
        {
            var parsed = Parse(releaseTitle);

            Subject.GetBooks(parsed, _author, _criteria).Should().BeEquivalentTo(new List<Book> { _bookTwo });
        }

        [TestCase("Matt Dinniman - Vol 1 - Dominion of Blades A LitRPG Adventure [M4B]")]
        [TestCase("Matt Dinniman - 01 Dominion of Blades [M4B]")]
        [TestCase("Matt Dinniman - 1 Dominion of Blades [M4B]")]
        [TestCase("Matt Dinniman - Dominion of Blades, Book 1 [M4B]")]
        public void should_map_release_naming_the_first_position_to_the_first_book(string releaseTitle)
        {
            var parsed = Parse(releaseTitle);

            Subject.GetBooks(parsed, _author, _criteria).Should().BeEquivalentTo(new List<Book> { _bookOne });
        }

        [TestCase("Matt Dinniman - Dominion of Blades [MP3]")]
        [TestCase("Matt Dinniman - Dominion of Blades (223) [EPUB]")]
        public void should_fall_back_to_search_criteria_when_no_position_is_named(string releaseTitle)
        {
            var parsed = Parse(releaseTitle);

            Subject.GetBooks(parsed, _author, _criteria).Should().BeEquivalentTo(new List<Book> { _bookOne });
        }

        [TestCase("Matt Dinniman - 2 Dominion of Blades Books [M4B]")]
        [TestCase("Matt Dinniman - Dominion of Blades Books 1-2 [M4B]")]
        [TestCase("Matt Dinniman - Dominion of Blades 2 [M4B]")]
        public void should_not_read_a_bundle_or_a_bare_number_as_a_position(string releaseTitle)
        {
            Subject.GetBooks(Parse(releaseTitle), _author, _criteria).Should().NotContain(_bookTwo);
        }

        [Test]
        public void should_ignore_a_position_the_series_does_not_have()
        {
            var parsed = Parse("Matt Dinniman - Dominion of Blades, Book 9 [M4B]");

            Subject.GetBooks(parsed, _author, _criteria).Should().NotContain(_bookTwo);
        }

        [Test]
        public void should_use_the_release_title_when_the_search_criteria_parser_found_the_searched_title()
        {
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "The Hobgoblin Riot Dominion of Blades, Book 2 (Unabridged) Matt Dinniman M4B",
                _author,
                _criteria.Books);

            parsed.BookTitle.Should().Be("Dominion of Blades");

            Subject.GetBooks(parsed, _author, _criteria).Should().BeEquivalentTo(new List<Book> { _bookTwo });
        }

        [Test]
        public void should_map_an_edition_title_segment_found_by_the_search_criteria_parser_to_the_searched_book()
        {
            var corrino = GivenBook(3, "Dune House Corrino");
            corrino.Editions.Value.Add(new Edition { Title = "Dune: House Corrino" });
            var criteria = new BookSearchCriteria { Author = _author, Books = new List<Book> { corrino } };

            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria("Brian Herbert - 3 House Corrino [M4B]", new Author { Name = "Brian Herbert", AuthorMetadataId = 7 }, criteria.Books);
            parsed.BookTitle.Should().Be("House Corrino");

            Subject.GetBooks(parsed, _author, criteria).Should().BeEquivalentTo(new List<Book> { corrino });
        }

        [Test]
        public void should_not_consult_series_when_the_author_has_none()
        {
            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.GetByAuthorMetadataId(It.IsAny<int>()))
                .Returns(new List<Series>());

            var parsed = Parse("Matt Dinniman - Dominion of Blades, Book 2 [M4B]");

            Subject.GetBooks(parsed, _author, _criteria).Should().NotContain(_bookTwo);

            Mocker.GetMock<IBookService>()
                .Verify(s => s.GetBooksByAuthorMetadataId(It.IsAny<int>()), Times.Never());
        }
    }
}
