using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.SlskdTests
{
    [TestFixture]
    public class SlskdIndexerFixture : CoreTest
    {
        private SlskdIndexerSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new SlskdIndexerSettings { MinimumFileCount = 2 };
        }

        private static SlskdSearchResponse Peer(string username, params string[] filenames)
        {
            return new SlskdSearchResponse
            {
                Username = username,
                QueueLength = 0,
                Files = filenames.Select(f => new SlskdFile { Filename = f, Size = 1000 }).ToList()
            };
        }

        private List<Parser.Model.ReleaseInfo> Build(params SlskdSearchResponse[] responses)
        {
            return SlskdIndexer.BuildReleases(responses, "Pink Floyd", _settings);
        }

        [Test]
        public async Task fetch_should_stamp_the_indexer_onto_every_release()
        {
            // Regression: Search returned BuildReleases directly, skipping CleanupReleases, so every
            // release carried IndexerId 0. Grabbing then failed with no usable error because
            // DownloadService could not resolve which indexer the release came from.
            var subject = Mocker.Resolve<SlskdIndexer>();
            subject.Definition = new IndexerDefinition
            {
                Id = 7,
                Name = "slskd",
                Settings = _settings,
                Priority = 3
            };

            Mocker.GetMock<ISlskdProxy>()
                .Setup(x => x.Search(It.IsAny<string>(), It.IsAny<SlskdIndexerSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SlskdSearchResponse>
                {
                    Peer("bob", @"music\Pink Floyd\Wish You Were Here\01.mp3", @"music\Pink Floyd\Wish You Were Here\02.mp3")
                });

            var criteria = new BookSearchCriteria
            {
                Author = new Author { Metadata = new AuthorMetadata { Name = "Pink Floyd" } },
                Books = new List<Book>(),
                BookTitle = "Wish You Were Here"
            };

            var releases = await subject.Fetch(criteria);

            releases.Should().NotBeEmpty();
            releases.Should().OnlyContain(x => x.IndexerId == 7);
            releases.Should().OnlyContain(x => x.Indexer == "slskd");
            releases.Should().OnlyContain(x => x.DownloadProtocol == nameof(SoulseekDownloadProtocol));
        }

        [Test]
        public void should_group_files_in_the_same_folder_into_one_release()
        {
            // Soulseek returns loose files; an album only exists as "files sharing a folder".
            var releases = Build(Peer("bob",
                @"Music\Pink Floyd\Wish You Were Here\01 Shine On.mp3",
                @"Music\Pink Floyd\Wish You Were Here\02 Have A Cigar.mp3"));

            releases.Should().HaveCount(1);
            releases[0].Size.Should().Be(2000);
        }

        [Test]
        public void should_emit_separate_releases_per_folder()
        {
            var releases = Build(Peer("bob",
                @"Music\Pink Floyd\Wish You Were Here\01 Shine On.mp3",
                @"Music\Pink Floyd\Wish You Were Here\02 Have A Cigar.mp3",
                @"Music\Pink Floyd\Animals\01 Pigs On The Wing.mp3",
                @"Music\Pink Floyd\Animals\02 Dogs.mp3"));

            releases.Should().HaveCount(2);
        }

        [Test]
        public void should_emit_separate_releases_per_peer()
        {
            var releases = Build(
                Peer("bob", @"Music\WYWH\01.mp3", @"Music\WYWH\02.mp3"),
                Peer("alice", @"Music\WYWH\01.mp3", @"Music\WYWH\02.mp3"));

            releases.Should().HaveCount(2);
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_skip_folders_below_minimum_file_count()
        {
            // Filters out loose single tracks that would otherwise look like albums.
            var releases = Build(Peer("bob", @"Music\Singles\one off.mp3"));

            releases.Should().BeEmpty();
        }

        [Test]
        public void should_ignore_non_audio_files()
        {
            var releases = Build(Peer("bob",
                @"Music\WYWH\01.mp3",
                @"Music\WYWH\02.mp3",
                @"Music\WYWH\cover.jpg",
                @"Music\WYWH\notes.txt"));

            releases.Should().HaveCount(1);
            releases[0].Size.Should().Be(2000);
        }

        [Test]
        public void should_not_allow_flac_by_default()
        {
            // flac is a music format in practice; allowing it made a title search return albums and
            // soundtracks that merely share a name with the book.
            var releases = Build(Peer("bob",
                @"Music\WYWH\01.flac",
                @"Music\WYWH\02.flac"));

            releases.Should().BeEmpty();
        }

        [Test]
        public void should_prefer_the_single_file_audiobook_when_a_folder_also_shares_mp3_parts()
        {
            _settings.MinimumFileCount = 1;

            var releases = Build(Peer("bob",
                @"Audiobooks\Dune\Dune.m4b",
                @"Audiobooks\Dune\01 - Chapter 1.mp3",
                @"Audiobooks\Dune\02 - Chapter 2.mp3"));

            releases.Should().HaveCount(1);
            releases[0].Size.Should().Be(1000);
            releases[0].Title.Should().EndWith("[M4B]");
            SlskdReleaseLocator.FromDownloadUrl(releases[0].DownloadUrl).Files.Should().HaveCount(1);
        }

        [Test]
        public void should_keep_mp3_parts_when_the_folder_has_no_single_file_audiobook()
        {
            var releases = Build(Peer("bob",
                @"Audiobooks\Dune\01 - Chapter 1.mp3",
                @"Audiobooks\Dune\02 - Chapter 2.mp3"));

            releases.Should().HaveCount(1);
            releases[0].Size.Should().Be(2000);
        }

        [Test]
        public void should_skip_peers_with_a_queue_longer_than_configured()
        {
            _settings.MaximumQueueLength = 5;

            var response = Peer("bob", @"Music\WYWH\01.mp3", @"Music\WYWH\02.mp3");
            response.QueueLength = 50;

            Build(response).Should().BeEmpty();
        }

        [Test]
        public void should_exclude_locked_files_by_default()
        {
            var response = Peer("bob", @"Music\WYWH\01.mp3", @"Music\WYWH\02.mp3");
            response.Files.ForEach(f => f.IsLocked = true);

            Build(response).Should().BeEmpty();
        }

        [Test]
        public void should_round_trip_the_locator_through_the_download_url()
        {
            var releases = Build(Peer("bob", @"Music\WYWH\01.mp3", @"Music\WYWH\02.mp3"));

            var locator = SlskdReleaseLocator.FromDownloadUrl(releases[0].DownloadUrl);

            locator.Should().NotBeNull();
            locator.Username.Should().Be("bob");
            locator.Directory.Should().Be(@"Music\WYWH");
            locator.Files.Should().HaveCount(2);
            locator.TotalSize.Should().Be(2000);
        }

        [Test]
        public void should_return_null_locator_for_a_foreign_download_url()
        {
            SlskdReleaseLocator.FromDownloadUrl("http://example.com/file.torrent").Should().BeNull();
        }

        [Test]
        public void should_prefix_artist_when_folder_name_lacks_it()
        {
            var releases = Build(Peer("bob", @"Music\Wish You Were Here\01.mp3", @"Music\Wish You Were Here\02.mp3"));

            releases[0].Title.Should().StartWith("Pink Floyd - Wish You Were Here");
        }

        [Test]
        public void should_not_duplicate_artist_already_in_folder_name()
        {
            var releases = Build(Peer("bob", @"Music\Pink Floyd - Animals\01.mp3", @"Music\Pink Floyd - Animals\02.mp3"));

            releases[0].Title.Should().StartWith("Pink Floyd - Animals");
            releases[0].Title.ToLowerInvariant().IndexOf("pink floyd").Should().Be(releases[0].Title.ToLowerInvariant().LastIndexOf("pink floyd"));
        }

        [Test]
        public void should_tag_title_with_the_file_format()
        {
            var releases = Build(Peer("bob", @"Music\WYWH\01.mp3", @"Music\WYWH\02.mp3"));

            releases[0].Title.Should().EndWith("[MP3]");
        }
    }
}
