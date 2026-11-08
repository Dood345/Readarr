using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Aggregation.Aggregators
{
    [TestFixture]
    public class AggregatePathInfoFixture : CoreTest<AggregatePathInfo>
    {
        [SetUp]
        public void Setup()
        {
            // C:\books is the library; anything else (a download client's folder) is not
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.Is<string>(p => p.StartsWith(@"C:\books".AsOsAgnostic()))))
                .Returns(new RootFolder { Path = @"C:\books".AsOsAgnostic() });
        }

        private static LocalBook File(string path, string bookTag = null, string authorTag = null)
        {
            return new LocalBook
            {
                Path = path.AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo
                {
                    BookTitle = bookTag,
                    Authors = authorTag.IsNullOrWhiteSpace() ? new List<string>() : new List<string> { authorTag }
                }
            };
        }

        [Test]
        public void should_take_author_and_book_from_the_folders_when_tags_are_empty()
        {
            // A chapter-per-file audiobook rip commonly has no tags at all.
            var track = Subject.Aggregate(File(@"C:\books\J.R.R. Tolkien\The Hobbit\H01-01 Introduction.mp3"), false);

            track.FileTrackInfo.BookTitle.Should().Be("The Hobbit");
            track.FileTrackInfo.Authors.Should().BeEquivalentTo("J.R.R. Tolkien");
        }

        [Test]
        public void should_strip_a_series_position_prefix_from_the_book_folder()
        {
            // Otherwise "The Lord of the Rings 1 - ..." scores against the omnibus rather than the
            // individual book.
            var track = Subject.Aggregate(File(@"C:\books\J.R.R. Tolkien\The Lord of the Rings 1 - The Fellowship of the Ring\L01-02.mp3"), false);

            track.FileTrackInfo.BookTitle.Should().Be("The Fellowship of the Ring");
        }

        [TestCase(@"C:\books\Frank Herbert\Dune 0.1 - The Butlerian Jihad\a.m4b", "The Butlerian Jihad")]
        [TestCase(@"C:\books\Trudi Canavan\The Black Magician 2 - The Novice\a.epub", "The Novice")]
        [TestCase(@"C:\books\Lev Grossman\The Magicians\a.epub", "The Magicians")]
        public void should_handle_the_series_naming_conventions_in_use(string path, string expected)
        {
            Subject.Aggregate(File(path), false).FileTrackInfo.BookTitle.Should().Be(expected);
        }

        [Test]
        public void should_not_overwrite_a_populated_book_title()
        {
            var track = Subject.Aggregate(
                File(@"C:\books\J.R.R. Tolkien\The Hobbit\x.mp3", "Real Tagged Title", "Real Tagged Author"), false);

            track.FileTrackInfo.BookTitle.Should().Be("Real Tagged Title");
        }

        [Test]
        public void should_offer_the_folder_author_alongside_a_tagged_one()
        {
            // Audiobooks routinely tag the narrator as the artist. Distance scoring picks the best
            // matching variant, so the folder is added rather than deferring to the tag - filling
            // only when empty left these files scoring 60% against an 80% threshold.
            var track = Subject.Aggregate(
                File(@"C:\books\J.K. Rowling\Harry Potter 1 - The Philosopher's Stone\x.mp3", null, "Jim Dale"), false);

            track.FileTrackInfo.Authors.Should().BeEquivalentTo("Jim Dale", "J.K. Rowling");
        }

        [Test]
        public void should_not_take_the_download_folder_as_the_author_outside_a_root_folder()
        {
            // slskd puts a finished download at <share>/<Book>/file, so the "author" folder is the
            // share itself. Offering "music" as an author made the tag list two entries long, which
            // stopped the real "Kevin J Anderson, Brian Herbert" entry being split into its authors,
            // and Hunters of Dune failed to import at 79.1% against the 80% threshold.
            var track = Subject.Aggregate(
                File(@"C:\downloads\music\Hunters of Dune\Hunters of Dune.m4b", null, "Kevin J Anderson, Brian Herbert"), false);

            track.FileTrackInfo.Authors.Should().BeEquivalentTo("Kevin J Anderson, Brian Herbert");
            track.FileTrackInfo.PathBookTitle.Should().Be("Hunters of Dune");
        }

        [Test]
        public void should_leave_a_file_with_no_path_alone()
        {
            var track = Subject.Aggregate(new LocalBook { Path = null }, false);

            track.Should().NotBeNull();
        }
    }
}
