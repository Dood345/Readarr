using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]

    public class MonitoredBookSpecificationFixture : CoreTest<MonitoredBookSpecification>
    {
        private MonitoredBookSpecification _monitoredBookSpecification;

        private RemoteBook _parseResultMulti;
        private RemoteBook _parseResultSingle;
        private Author _fakeAuthor;
        private Book _firstBook;
        private Book _secondBook;

        [SetUp]
        public void Setup()
        {
            _monitoredBookSpecification = Mocker.Resolve<MonitoredBookSpecification>();

            _fakeAuthor = Builder<Author>.CreateNew()
                .With(c => c.Monitored = true)
                .Build();

            _firstBook = new Book { Monitored = true };
            _secondBook = new Book { Monitored = true };

            var singleBookList = new List<Book> { _firstBook };
            var doubleBookList = new List<Book> { _firstBook, _secondBook };

            _parseResultMulti = new RemoteBook
            {
                Author = _fakeAuthor,
                Books = doubleBookList
            };

            _parseResultSingle = new RemoteBook
            {
                Author = _fakeAuthor,
                Books = singleBookList
            };
        }

        private void WithFirstBookUnmonitored()
        {
            _firstBook.Monitored = false;
        }

        private void WithSecondBookUnmonitored()
        {
            _secondBook.Monitored = false;
        }

        [Test]
        public void setup_should_return_monitored_book_should_return_true()
        {
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeTrue();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void not_monitored_author_should_be_skipped()
        {
            _fakeAuthor.Monitored = false;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void only_book_not_monitored_should_return_false()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void both_books_not_monitored_should_return_false()
        {
            WithFirstBookUnmonitored();
            WithSecondBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void only_first_book_not_monitored_should_return_false()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void only_second_book_not_monitored_should_return_false()
        {
            WithSecondBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_for_single_book_search()
        {
            _fakeAuthor.Monitored = false;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, new BookSearchCriteria()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_book_is_not_monitored_and_monitoredEpisodesOnly_flag_is_false()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, new BookSearchCriteria { MonitoredBooksOnly = false }).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_book_is_not_monitored_and_monitoredEpisodesOnly_flag_is_true()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, new BookSearchCriteria { MonitoredBooksOnly = true }).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_all_books_are_not_monitored_for_discography_pack_release()
        {
            WithSecondBookUnmonitored();
            _parseResultMulti.ParsedBookInfo = new ParsedBookInfo()
            {
                Discography = true
            };

            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        private void GivenEditions(Book book, bool ebookMonitored, bool audioMonitored)
        {
            book.Editions = new List<Edition>
            {
                new Edition { Id = 1, IsEbook = true, Monitored = ebookMonitored },
                new Edition { Id = 2, IsEbook = false, Monitored = audioMonitored }
            };
        }

        private static RemoteBook WithQuality(RemoteBook remoteBook, Quality quality)
        {
            remoteBook.ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) };
            return remoteBook;
        }

        [Test]
        public void should_reject_an_ebook_release_when_only_the_audio_edition_is_monitored()
        {
            GivenEditions(_firstBook, ebookMonitored: false, audioMonitored: true);

            _monitoredBookSpecification.IsSatisfiedBy(WithQuality(_parseResultSingle, Quality.EPUB), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_an_audiobook_release_when_only_the_ebook_edition_is_monitored()
        {
            GivenEditions(_firstBook, ebookMonitored: true, audioMonitored: false);

            _monitoredBookSpecification.IsSatisfiedBy(WithQuality(_parseResultSingle, Quality.M4B), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_release_whose_media_type_edition_is_monitored()
        {
            GivenEditions(_firstBook, ebookMonitored: false, audioMonitored: true);

            _monitoredBookSpecification.IsSatisfiedBy(WithQuality(_parseResultSingle, Quality.M4B), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_release_when_the_book_has_no_edition_of_that_type()
        {
            _firstBook.Editions = new List<Edition> { new Edition { Id = 1, IsEbook = true, Monitored = true } };

            _monitoredBookSpecification.IsSatisfiedBy(WithQuality(_parseResultSingle, Quality.MP3), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_release_of_unknown_quality_whatever_the_editions()
        {
            GivenEditions(_firstBook, ebookMonitored: false, audioMonitored: true);

            _monitoredBookSpecification.IsSatisfiedBy(WithQuality(_parseResultSingle, Quality.Unknown), null).Accepted.Should().BeTrue();
        }
    }
}
