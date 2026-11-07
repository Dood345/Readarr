using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]
    public class WorkTitleNamingFixture : CoreTest<FileNameBuilder>
    {
        private Author _author;
        private Book _book;
        private Edition _audiobook;
        private Edition _ebook;
        private BookFile _bookFile;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>
                .CreateNew()
                .With(s => s.Name = "Brian Herbert")
                .With(s => s.Metadata = new AuthorMetadata { Name = "Brian Herbert" })
                .Build();

            _book = Builder<Book>
                .CreateNew()
                .With(s => s.Title = "House Atreides")
                .With(s => s.AuthorMetadata = _author.Metadata.Value)
                .With(s => s.SeriesLinks = new List<SeriesBookLink>())
                .Build();

            _audiobook = Builder<Edition>
                .CreateNew()
                .With(s => s.Title = "Dune: House Atreides: House Trilogy, Book 1")
                .With(s => s.IsEbook = false)
                .With(s => s.Book = _book)
                .Build();

            _ebook = Builder<Edition>
                .CreateNew()
                .With(s => s.Title = "House Atreides")
                .With(s => s.IsEbook = true)
                .With(s => s.Book = _book)
                .Build();

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameBooks = true;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).Returns(_namingConfig);

            _bookFile = Builder<BookFile>.CreateNew()
                .With(e => e.Part = 1)
                .With(e => e.PartCount = 1)
                .With(e => e.Quality = new QualityModel(Quality.M4B))
                .With(e => e.Path = @"C:\books\Brian Herbert\Dune House Atreides (Unabridged).m4b".AsOsAgnostic())
                .Build();

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));
        }

        [Test]
        public void default_format_should_put_both_editions_in_the_same_folder()
        {
            // The audiobook edition carries Audible's long title; the folder must still be the
            // work's title so the epub and the m4b of one book sit together.
            Subject.BuildBookFileName(_author, _audiobook, _bookFile)
                .Should().Be(@"House Atreides\Brian Herbert - House Atreides".AsOsAgnostic());

            Subject.BuildBookFileName(_author, _ebook, _bookFile)
                .Should().Be(@"House Atreides\Brian Herbert - House Atreides".AsOsAgnostic());
        }

        [Test]
        public void work_title_tokens_should_use_the_book_title()
        {
            _namingConfig.StandardBookFormat = "{Work Title} _ {Work CleanTitle} _ {Work TitleThe} _ {Book Title}";
            _book.Title = "The Butlerian Jihad: Legends!";

            Subject.BuildBookFileName(_author, _audiobook, _bookFile)
                .Should().Be("The Butlerian Jihad - Legends! _ The Butlerian Jihad Legends! _ Butlerian Jihad - Legends!, The _ Dune - House Atreides - House Trilogy, Book 1");
        }

        [Test]
        public void work_title_should_fall_back_to_the_edition_title_when_the_book_has_none()
        {
            _namingConfig.StandardBookFormat = "{Work Title}";
            _book.Title = null;

            Subject.BuildBookFileName(_author, _ebook, _bookFile).Should().Be("House Atreides");
        }

        [Test]
        public void multi_part_books_should_keep_their_original_file_names_by_default()
        {
            _bookFile.Part = 2;
            _bookFile.PartCount = 219;
            _bookFile.Path = @"C:\books\J.R.R. Tolkien\The Hobbit\H01-02 Chapter01 An Unexpected Party 1 2m56s.mp3".AsOsAgnostic();

            Subject.BuildBookFileName(_author, _audiobook, _bookFile)
                .Should().Be(@"House Atreides\H01-02 Chapter01 An Unexpected Party 1 2m56s".AsOsAgnostic());
        }

        [Test]
        public void multi_part_books_should_follow_the_format_when_keeping_names_is_off()
        {
            _namingConfig.KeepPartFileNames = false;
            _bookFile.Part = 2;
            _bookFile.PartCount = 219;
            _bookFile.Path = @"C:\books\J.R.R. Tolkien\The Hobbit\H01-02 Chapter01 An Unexpected Party 1 2m56s.mp3".AsOsAgnostic();

            Subject.BuildBookFileName(_author, _audiobook, _bookFile)
                .Should().Be(@"House Atreides\Brian Herbert - House Atreides (2)".AsOsAgnostic());
        }

        [Test]
        public void single_file_books_should_follow_the_format_even_when_keeping_part_names()
        {
            _bookFile.Path = @"C:\books\Brian Herbert\whatever the peer called it.m4b".AsOsAgnostic();

            Subject.BuildBookFileName(_author, _audiobook, _bookFile)
                .Should().Be(@"House Atreides\Brian Herbert - House Atreides".AsOsAgnostic());
        }
    }
}
