using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class BookCutoffServiceFixture : CoreTest<BookCutoffService>
    {
        private CustomFormat _goodBitrate;
        private QualityProfile _profile;
        private Author _author;
        private List<Book> _repositoryRecords;

        [SetUp]
        public void Setup()
        {
            _goodBitrate = new CustomFormat { Id = 1, Name = "Audiobook ~120 kbps" };

            _profile = new QualityProfile
            {
                Id = 3,
                Cutoff = Quality.M4B.Id,
                UpgradeAllowed = true,
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MOBI, Quality.AZW3, Quality.EPUB, Quality.MP3, Quality.M4B),
                MinFormatScore = 0,
                CutoffFormatScore = 100,
                FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = _goodBitrate, Score = 100 } }
            };

            _author = new Author { Id = 5, QualityProfileId = _profile.Id, QualityProfile = _profile };

            _repositoryRecords = new List<Book>();

            Mocker.GetMock<IQualityProfileService>()
                .Setup(s => s.All())
                .Returns(new List<QualityProfile> { _profile });

            Mocker.GetMock<IBookRepository>()
                .Setup(s => s.BooksWhereCutoffUnmet(It.IsAny<PagingSpec<Book>>(), It.IsAny<List<QualitiesBelowCutoff>>()))
                .Returns((PagingSpec<Book> spec, List<QualitiesBelowCutoff> q) =>
                {
                    spec.Records = _repositoryRecords;
                    spec.TotalRecords = _repositoryRecords.Count;
                    return spec;
                });

            // Only files tagged "good" carry the ~120 kbps format
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(It.IsAny<BookFile>(), It.IsAny<Author>()))
                .Returns((BookFile f, Author a) => f.Path.Contains("good") ? new List<CustomFormat> { _goodBitrate } : new List<CustomFormat>());
        }

        private Book GivenBook(int id, Quality quality, bool good, bool editionMonitored = true)
        {
            var edition = new Edition { Id = id * 10, BookId = id, Monitored = editionMonitored };
            var file = new BookFile
            {
                Id = id * 100,
                EditionId = edition.Id,
                Path = $"/books/{id}/{(good ? "good" : "poor")}.file",
                Quality = new QualityModel(quality)
            };

            var book = new Book
            {
                Id = id,
                Title = $"Book {id}",
                Author = _author,
                Editions = new List<Edition> { edition },
                BookFiles = new List<BookFile> { file }
            };

            _repositoryRecords.Add(book);

            return book;
        }

        private static PagingSpec<Book> Spec(int page = 1, int pageSize = 100)
        {
            return new PagingSpec<Book> { Page = page, PageSize = pageSize, SortKey = "Id" };
        }

        [Test]
        public void should_list_an_audiobook_at_the_quality_cutoff_whose_format_score_is_below_the_format_cutoff()
        {
            var book = GivenBook(1, Quality.M4B, good: false);

            var result = Subject.BooksWhereCutoffUnmet(Spec());

            result.Records.Should().ContainSingle(b => b.Id == book.Id);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void should_not_list_an_audiobook_that_meets_the_format_cutoff()
        {
            GivenBook(1, Quality.M4B, good: true);

            Subject.BooksWhereCutoffUnmet(Spec()).Records.Should().BeEmpty();
        }

        [Test]
        public void should_still_list_an_audiobook_below_the_quality_cutoff_whatever_its_format_score()
        {
            GivenBook(1, Quality.MP3, good: true);

            Subject.BooksWhereCutoffUnmet(Spec()).Records.Should().HaveCount(1);
        }

        [Test]
        public void should_not_hold_an_ebook_to_a_format_cutoff_that_belongs_to_the_audio_side()
        {
            // EPUB is the best allowed text quality here, and a bitrate format can never match an epub
            GivenBook(1, Quality.EPUB, good: false);

            Subject.BooksWhereCutoffUnmet(Spec()).Records.Should().BeEmpty();
        }

        [Test]
        public void should_ignore_files_on_unmonitored_editions()
        {
            GivenBook(1, Quality.M4B, good: false, editionMonitored: false);

            Subject.BooksWhereCutoffUnmet(Spec()).Records.Should().BeEmpty();
        }

        [Test]
        public void should_page_in_memory_when_a_format_cutoff_is_in_play()
        {
            for (var i = 1; i <= 5; i++)
            {
                GivenBook(i, Quality.M4B, good: i == 3);
            }

            var page2 = Subject.BooksWhereCutoffUnmet(Spec(page: 2, pageSize: 2));

            page2.TotalRecords.Should().Be(4);
            page2.Records.Select(b => b.Id).Should().Equal(4, 5);
        }

        [Test]
        public void should_ask_the_repository_for_every_allowed_audio_quality_when_a_format_cutoff_is_in_play()
        {
            GivenBook(1, Quality.M4B, good: false);

            Subject.BooksWhereCutoffUnmet(Spec());

            Mocker.GetMock<IBookRepository>()
                .Verify(s => s.BooksWhereCutoffUnmet(
                    It.Is<PagingSpec<Book>>(p => p.PageSize == int.MaxValue),
                    It.Is<List<QualitiesBelowCutoff>>(q => q.Single().QualityIds.Contains(Quality.M4B.Id) && q.Single().QualityIds.Contains(Quality.MP3.Id) && !q.Single().QualityIds.Contains(Quality.EPUB.Id))),
                    Times.Once());
        }

        [Test]
        public void should_leave_paging_to_the_database_when_no_profile_has_a_format_cutoff()
        {
            _profile.CutoffFormatScore = 0;
            GivenBook(1, Quality.M4B, good: false);

            var spec = Spec();
            Subject.BooksWhereCutoffUnmet(spec);

            Mocker.GetMock<IBookRepository>()
                .Verify(s => s.BooksWhereCutoffUnmet(spec, It.Is<List<QualitiesBelowCutoff>>(q => !q.Single().QualityIds.Contains(Quality.M4B.Id))), Times.Once());
        }
    }
}
