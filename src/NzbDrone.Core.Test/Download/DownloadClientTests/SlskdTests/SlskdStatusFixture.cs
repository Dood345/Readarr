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

        private static List<SlskdTransferFile> Named(params string[] filenames)
        {
            return filenames.Select(f => new SlskdTransferFile { Filename = f, State = "Completed, Succeeded" }).ToList();
        }

        [Test]
        public void download_ids_should_be_stable_and_distinct()
        {
            // Regression: ids came from string.GetHashCode(), which .NET randomises per process, so
            // every restart orphaned every tracked download.
            SlskdIds.ForDownload("bob", @"@@abc\Audiobooks\Dune").Should().Be(SlskdIds.ForDownload("bob", @"@@abc\Audiobooks\Dune"));
            SlskdIds.ForDownload("bob", @"@@abc\Audiobooks\Dune").Should().Be("Slskd-bob-" + SlskdIds.ForDownload("bob", @"@@abc\Audiobooks\Dune").Split('-').Last());
            SlskdIds.ForDownload("bob", @"@@abc\Audiobooks\Dune").Should().NotBe(SlskdIds.ForDownload("bob", @"@@abc\Audiobooks\Dune Messiah"));
            SlskdIds.ForDownload("bob", @"@@abc\Audiobooks\Dune").Split('-').Last().Should().HaveLength(8);
        }

        [Test]
        public void should_adopt_an_ungrabbed_folder_only_when_it_holds_a_book_file()
        {
            var settings = new Core.Download.Clients.Slskd.SlskdSettings();

            Core.Download.Clients.Slskd.Slskd.ShouldAdopt(Named(@"Music\X&Y\01 Square One.mp3", @"Music\X&Y\02 What If.mp3"), settings).Should().BeFalse();
            Core.Download.Clients.Slskd.Slskd.ShouldAdopt(Named(@"Audiobooks\Dune\Dune.m4b"), settings).Should().BeTrue();
            Core.Download.Clients.Slskd.Slskd.ShouldAdopt(Named(@"Books\Dune\Dune.epub", @"Books\Dune\cover.jpg"), settings).Should().BeTrue();
        }

        [Test]
        public void should_not_adopt_anything_when_ungrabbed_imports_are_off()
        {
            var settings = new Core.Download.Clients.Slskd.SlskdSettings { ImportUngrabbedDownloads = false };

            Core.Download.Clients.Slskd.Slskd.ShouldAdopt(Named(@"Audiobooks\Dune\Dune.m4b"), settings).Should().BeFalse();
        }

        [Test]
        public void should_adopt_every_folder_when_no_extensions_are_listed()
        {
            var settings = new Core.Download.Clients.Slskd.SlskdSettings { BookExtensions = "" };

            Core.Download.Clients.Slskd.Slskd.ShouldAdopt(Named(@"Music\X&Y\01 Square One.mp3"), settings).Should().BeTrue();
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
