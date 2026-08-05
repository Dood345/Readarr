using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.SlskdTests
{
    [TestFixture]
    public class SlskdStatusFixture : CoreTest
    {
        private static List<SlskdTransferFile> Files(params string[] states)
        {
            return states.Select(s => new SlskdTransferFile { State = s }).ToList();
        }

        [Test]
        public void should_report_completed_only_when_every_file_succeeded()
        {
            Core.Download.Clients.Slskd.Slskd.MapStatus(Files("Completed, Succeeded", "Completed, Succeeded"))
                .Should().Be(DownloadItemStatus.Completed);
        }

        [Test]
        public void should_not_report_completed_while_a_file_is_still_transferring()
        {
            // Importing a half-transferred album would leave the release permanently incomplete.
            Core.Download.Clients.Slskd.Slskd.MapStatus(Files("Completed, Succeeded", "InProgress"))
                .Should().Be(DownloadItemStatus.Downloading);
        }

        [Test]
        public void should_report_queued_when_nothing_has_started()
        {
            Core.Download.Clients.Slskd.Slskd.MapStatus(Files("Queued, Remotely", "Queued, Locally"))
                .Should().Be(DownloadItemStatus.Queued);
        }

        [TestCase("Completed, Errored")]
        [TestCase("Completed, Rejected")]
        [TestCase("Completed, TimedOut")]
        public void should_report_failed_for_unrecoverable_states(string state)
        {
            Core.Download.Clients.Slskd.Slskd.MapStatus(Files("Completed, Succeeded", state))
                .Should().Be(DownloadItemStatus.Failed);
        }

        [Test]
        public void should_report_warning_when_a_file_was_cancelled()
        {
            Core.Download.Clients.Slskd.Slskd.MapStatus(Files("Completed, Succeeded", "Completed, Cancelled"))
                .Should().Be(DownloadItemStatus.Warning);
        }

        [Test]
        public void failure_should_take_precedence_over_cancellation()
        {
            Core.Download.Clients.Slskd.Slskd.MapStatus(Files("Completed, Cancelled", "Completed, Errored"))
                .Should().Be(DownloadItemStatus.Failed);
        }
    }
}
