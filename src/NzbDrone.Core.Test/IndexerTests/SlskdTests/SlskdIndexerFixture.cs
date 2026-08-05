using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Slskd;
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
        public void should_group_files_in_the_same_folder_into_one_release()
        {
            // Soulseek returns loose files; an album only exists as "files sharing a folder".
            var releases = Build(Peer("bob",
                @"Music\Pink Floyd\Wish You Were Here\01 Shine On.flac",
                @"Music\Pink Floyd\Wish You Were Here\02 Have A Cigar.flac"));

            releases.Should().HaveCount(1);
            releases[0].Size.Should().Be(2000);
        }

        [Test]
        public void should_emit_separate_releases_per_folder()
        {
            var releases = Build(Peer("bob",
                @"Music\Pink Floyd\Wish You Were Here\01 Shine On.flac",
                @"Music\Pink Floyd\Wish You Were Here\02 Have A Cigar.flac",
                @"Music\Pink Floyd\Animals\01 Pigs On The Wing.flac",
                @"Music\Pink Floyd\Animals\02 Dogs.flac"));

            releases.Should().HaveCount(2);
        }

        [Test]
        public void should_emit_separate_releases_per_peer()
        {
            var releases = Build(
                Peer("bob", @"Music\WYWH\01.flac", @"Music\WYWH\02.flac"),
                Peer("alice", @"Music\WYWH\01.flac", @"Music\WYWH\02.flac"));

            releases.Should().HaveCount(2);
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_skip_folders_below_minimum_file_count()
        {
            // Filters out loose single tracks that would otherwise look like albums.
            var releases = Build(Peer("bob", @"Music\Singles\one off.flac"));

            releases.Should().BeEmpty();
        }

        [Test]
        public void should_ignore_non_audio_files()
        {
            var releases = Build(Peer("bob",
                @"Music\WYWH\01.flac",
                @"Music\WYWH\02.flac",
                @"Music\WYWH\cover.jpg",
                @"Music\WYWH\notes.txt"));

            releases.Should().HaveCount(1);
            releases[0].Size.Should().Be(2000);
        }

        [Test]
        public void should_skip_peers_with_a_queue_longer_than_configured()
        {
            _settings.MaximumQueueLength = 5;

            var response = Peer("bob", @"Music\WYWH\01.flac", @"Music\WYWH\02.flac");
            response.QueueLength = 50;

            Build(response).Should().BeEmpty();
        }

        [Test]
        public void should_exclude_locked_files_by_default()
        {
            var response = Peer("bob", @"Music\WYWH\01.flac", @"Music\WYWH\02.flac");
            response.Files.ForEach(f => f.IsLocked = true);

            Build(response).Should().BeEmpty();
        }

        [Test]
        public void should_round_trip_the_locator_through_the_download_url()
        {
            var releases = Build(Peer("bob", @"Music\WYWH\01.flac", @"Music\WYWH\02.flac"));

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
            var releases = Build(Peer("bob", @"Music\Wish You Were Here\01.flac", @"Music\Wish You Were Here\02.flac"));

            releases[0].Title.Should().StartWith("Pink Floyd - Wish You Were Here");
        }

        [Test]
        public void should_not_duplicate_artist_already_in_folder_name()
        {
            var releases = Build(Peer("bob", @"Music\Pink Floyd - Animals\01.flac", @"Music\Pink Floyd - Animals\02.flac"));

            releases[0].Title.Should().StartWith("Pink Floyd - Animals");
            releases[0].Title.ToLowerInvariant().IndexOf("pink floyd").Should().Be(releases[0].Title.ToLowerInvariant().LastIndexOf("pink floyd"));
        }

        [Test]
        public void should_tag_title_with_the_file_format()
        {
            var releases = Build(Peer("bob", @"Music\WYWH\01.flac", @"Music\WYWH\02.flac"));

            releases[0].Title.Should().EndWith("[FLAC]");
        }
    }
}
