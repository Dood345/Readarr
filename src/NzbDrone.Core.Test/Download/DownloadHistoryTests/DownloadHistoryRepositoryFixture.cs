using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadHistoryTests
{
    [TestFixture]
    public class DownloadHistoryRepositoryFixture : DbTest<DownloadHistoryRepository, DownloadHistory>
    {
        private DownloadHistory Event(DownloadHistoryEventType eventType, int downloadClientId, string downloadId, int minutesAgo)
        {
            return new DownloadHistory
            {
                EventType = eventType,
                DownloadClientId = downloadClientId,
                DownloadId = downloadId,
                SourceTitle = downloadId,
                Date = DateTime.UtcNow.AddMinutes(-minutesAgo),
                Protocol = "Soulseek"
            };
        }

        [Test]
        public void find_grabs_should_return_the_newest_grabs_for_one_client_only()
        {
            Subject.Insert(Event(DownloadHistoryEventType.DownloadGrabbed, 1, "oldest", 40));
            Subject.Insert(Event(DownloadHistoryEventType.DownloadGrabbed, 1, "middle", 30));
            Subject.Insert(Event(DownloadHistoryEventType.DownloadImported, 1, "middle", 20));
            Subject.Insert(Event(DownloadHistoryEventType.DownloadGrabbed, 2, "other-client", 15));
            Subject.Insert(Event(DownloadHistoryEventType.DownloadGrabbed, 1, "newest", 10));

            var grabs = Subject.FindGrabs(1, 2);

            grabs.Select(g => g.DownloadId).Should().Equal("newest", "middle");
            grabs.Should().OnlyContain(g => g.EventType == DownloadHistoryEventType.DownloadGrabbed && g.DownloadClientId == 1);
        }

        [Test]
        public void find_grabs_should_return_everything_when_fewer_than_the_limit()
        {
            Subject.Insert(Event(DownloadHistoryEventType.DownloadGrabbed, 1, "a", 10));

            Subject.FindGrabs(1, 60).Should().HaveCount(1);
            Subject.FindGrabs(2, 60).Should().BeEmpty();
        }
    }
}
