using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Blocklisting
{
    [TestFixture]
    public class BlocklistServiceFixture : CoreTest<BlocklistService>
    {
        private DownloadFailedEvent _event;

        [SetUp]
        public void Setup()
        {
            _event = new DownloadFailedEvent
            {
                AuthorId = 12345,
                BookIds = new List<int> { 1 },
                Quality = new QualityModel(Quality.MP3),
                SourceTitle = "author.name.book.title",
                DownloadClient = "SabnzbdClient",
                DownloadId = "Sabnzbd_nzo_2dfh73k"
            };

            _event.Data.Add("publishedDate", DateTime.UtcNow.ToString("s") + "Z");
            _event.Data.Add("size", "1000");
            _event.Data.Add("indexer", "nzbs.org");
            _event.Data.Add("protocol", "1");
            _event.Data.Add("message", "Marked as failed");
        }

        [Test]
        public void should_add_to_repository()
        {
            Subject.Handle(_event);

            Mocker.GetMock<IBlocklistRepository>()
                .Verify(v => v.Insert(It.Is<Blocklist>(b => b.BookIds == _event.BookIds)), Times.Once());
        }

        private ReleaseInfo GivenSoulseekRelease()
        {
            return new ReleaseInfo
            {
                Title = "Brian Herbert - 3 House Corrino [M4B]",
                Indexer = "slskd",
                Size = 1381540167,
                DownloadProtocol = nameof(SoulseekDownloadProtocol),
                PublishDate = DateTime.UtcNow
            };
        }

        private void GivenBlocklisted(string protocol, string indexer = "slskd", long? size = null)
        {
            Mocker.GetMock<IBlocklistRepository>()
                .Setup(s => s.BlocklistedByTitle(12345, It.IsAny<string>()))
                .Returns(new List<Blocklist>
                {
                    new Blocklist { AuthorId = 12345, SourceTitle = "Brian Herbert - 3 House Corrino [M4B]", Protocol = protocol, Indexer = indexer, Size = size, Date = DateTime.UtcNow.AddMinutes(-5) }
                });
        }

        [Test]
        public void should_report_a_soulseek_release_blocklisted_by_title_and_indexer()
        {
            GivenBlocklisted(nameof(SoulseekDownloadProtocol));

            Subject.Blocklisted(12345, GivenSoulseekRelease()).Should().BeTrue();
        }

        [Test]
        public void should_not_match_a_soulseek_release_against_a_usenet_blocklist_entry()
        {
            GivenBlocklisted(nameof(UsenetDownloadProtocol));

            Subject.Blocklisted(12345, GivenSoulseekRelease()).Should().BeFalse();
        }

        [Test]
        public void should_not_match_a_soulseek_release_of_a_different_size()
        {
            GivenBlocklisted(nameof(SoulseekDownloadProtocol), size: 500_000_000);

            Subject.Blocklisted(12345, GivenSoulseekRelease()).Should().BeFalse();
        }

        [Test]
        public void should_add_to_repository_missing_size_and_protocol()
        {
            Subject.Handle(_event);

            _event.Data.Remove("size");
            _event.Data.Remove("protocol");

            Mocker.GetMock<IBlocklistRepository>()
                .Verify(v => v.Insert(It.Is<Blocklist>(b => b.BookIds == _event.BookIds)), Times.Once());
        }
    }
}
