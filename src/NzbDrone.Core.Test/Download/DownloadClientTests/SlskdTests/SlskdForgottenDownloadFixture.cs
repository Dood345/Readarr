using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.SlskdTests
{
    /// <summary>
    /// slskd's transfer list is its only record of a download, and soularr clears every completed
    /// transfer at the end of each run. A Readarr download whose import failed used to vanish from
    /// the queue within minutes, leaving its folder orphaned in the download directory. The client
    /// now falls back to Readarr's own download history for grabs slskd has forgotten.
    /// </summary>
    [TestFixture]
    public class SlskdForgottenDownloadFixture : DownloadClientFixtureBase<Slskd>
    {
        private const string Username = "bob";
        private const string RemoteDirectory = @"@@abc\Audiobooks\Hunters of Dune";
        private const string FolderName = "Hunters of Dune";

        private SlskdSettings _settings;
        private string _downloadPath;
        private string _folder;
        private SlskdReleaseLocator _locator;
        private DownloadHistory _grab;
        private List<SlskdTransferUser> _transfers;

        [SetUp]
        public void Setup()
        {
            _downloadPath = @"C:\downloads\music".AsOsAgnostic();
            _folder = Path.Combine(_downloadPath, FolderName);

            _settings = new SlskdSettings
            {
                BaseUrl = "http://localhost:5030",
                ApiKey = "key",
                DownloadPath = _downloadPath
            };

            Subject.Definition = new DownloadClientDefinition
            {
                Id = 7,
                Name = "slskd",
                Settings = _settings
            };

            _locator = new SlskdReleaseLocator
            {
                Username = Username,
                Directory = RemoteDirectory,
                Files = new List<SlskdLocatorFile>
                {
                    new SlskdLocatorFile { Filename = RemoteDirectory + @"\Hunters of Dune.m4b", Size = 500 },
                    new SlskdLocatorFile { Filename = RemoteDirectory + @"\cover.jpg", Size = 20 }
                }
            };

            _grab = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadGrabbed,
                DownloadId = SlskdIds.ForDownload(Username, RemoteDirectory),
                DownloadClientId = 7,
                SourceTitle = "Brian Herbert - Hunters of Dune [M4B]",
                Date = DateTime.UtcNow.AddMinutes(-30),
                Release = new ReleaseInfo { DownloadUrl = _locator.ToDownloadUrl() }
            };

            _transfers = new List<SlskdTransferUser>();

            Mocker.GetMock<ISlskdProxy>()
                  .Setup(p => p.GetTransfers(It.IsAny<ISlskdConnectionSettings>()))
                  .ReturnsAsync(() => _transfers);

            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetRecentGrabs(7, It.IsAny<int>()))
                  .Returns(() => new List<DownloadHistory> { _grab });

            GivenLatestEvent(DownloadHistoryEventType.DownloadGrabbed);
            GivenFolderOnDisk(true);
        }

        private void GivenLatestEvent(DownloadHistoryEventType eventType)
        {
            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetLatestDownloadHistoryItem(_grab.DownloadId))
                  .Returns(new DownloadHistory { EventType = eventType, DownloadId = _grab.DownloadId });
        }

        private void GivenFolderOnDisk(bool exists)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(_folder))
                  .Returns(exists);
        }

        private void GivenSlskdStillListsTheTransfer(string state)
        {
            _transfers.Add(new SlskdTransferUser
            {
                Username = Username,
                Directories = new List<SlskdTransferDirectory>
                {
                    new SlskdTransferDirectory
                    {
                        Directory = RemoteDirectory,
                        Files = new List<SlskdTransferFile>
                        {
                            new SlskdTransferFile { Id = "t1", Filename = RemoteDirectory + @"\Hunters of Dune.m4b", Size = 500, BytesTransferred = 250, State = state }
                        }
                    }
                }
            });
        }

        [Test]
        public void should_list_a_grabbed_folder_slskd_has_forgotten()
        {
            var items = Subject.GetItems().ToList();

            items.Should().HaveCount(1);

            var item = items.Single();

            VerifyIdentifiable(item);
            item.DownloadId.Should().Be(_grab.DownloadId);
            item.Title.Should().Be(FolderName);
            item.Status.Should().Be(DownloadItemStatus.Completed);
            item.OutputPath.Should().Be(new OsPath(_folder));
            item.TotalSize.Should().Be(520);
            item.RemainingSize.Should().Be(0);
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [Test]
        public void should_keep_listing_a_folder_whose_import_failed()
        {
            // The real case: identification failed, history got bookImportIncomplete, and soularr
            // then cleared the transfer. The item has to stay in the queue with its failure.
            GivenLatestEvent(DownloadHistoryEventType.DownloadImportIncomplete);

            Subject.GetItems().Should().ContainSingle(i => i.DownloadId == _grab.DownloadId);
        }

        [TestCase(DownloadHistoryEventType.DownloadImported)]
        [TestCase(DownloadHistoryEventType.DownloadFailed)]
        [TestCase(DownloadHistoryEventType.DownloadIgnored)]
        public void should_not_list_a_folder_readarr_is_done_with(DownloadHistoryEventType latest)
        {
            // Imported, blocklisted or removed from the queue: the folder may still be on disk,
            // but re-offering it would re-import or undo what the user did.
            GivenLatestEvent(latest);

            Subject.GetItems().Should().BeEmpty();
        }

        [Test]
        public void should_not_list_a_grab_whose_folder_is_gone()
        {
            GivenFolderOnDisk(false);

            Subject.GetItems().Should().BeEmpty();
        }

        [Test]
        public void should_list_a_folder_once_while_slskd_still_knows_it()
        {
            // While the transfer is in slskd its state is authoritative, so a folder whose files
            // are still arriving is not reported complete just because it exists on disk.
            GivenSlskdStillListsTheTransfer("InProgress");

            var items = Subject.GetItems().ToList();

            items.Should().HaveCount(1);
            items.Single().DownloadId.Should().Be(_grab.DownloadId);
            items.Single().Status.Should().Be(DownloadItemStatus.Downloading);
        }

        [Test]
        public void should_list_a_grabbed_folder_even_when_ungrabbed_imports_are_off()
        {
            // Adoption governs folders Readarr did not grab; this one it did.
            _settings.ImportUngrabbedDownloads = false;

            var item = Subject.GetItems().Should().ContainSingle().Subject;

            item.DownloadId.Should().Be(_grab.DownloadId);
            item.Category.Should().BeNull();
        }

        [Test]
        public void should_ignore_a_grab_that_did_not_come_from_the_slskd_indexer()
        {
            _grab.Release.DownloadUrl = "http://indexer/release.nzb";

            Subject.GetItems().Should().BeEmpty();
        }

        [Test]
        public void should_not_list_anything_when_slskd_cannot_be_reached()
        {
            // Without the transfer list there is no way to tell a finished folder from one whose
            // files are still arriving, so nothing is offered until slskd is back.
            Mocker.GetMock<ISlskdProxy>()
                  .Setup(p => p.GetTransfers(It.IsAny<ISlskdConnectionSettings>()))
                  .ThrowsAsync(new Exception("connection refused"));

            Subject.GetItems().Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_still_list_slskd_transfers_when_the_history_lookup_fails()
        {
            GivenSlskdStillListsTheTransfer("Completed, Succeeded");

            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetRecentGrabs(It.IsAny<int>(), It.IsAny<int>()))
                  .Throws(new InvalidOperationException("database locked"));

            Subject.GetItems().Should().ContainSingle(i => i.Status == DownloadItemStatus.Completed);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void is_finished_should_only_cover_terminal_events()
        {
            Slskd.IsFinished(DownloadHistoryEventType.DownloadGrabbed).Should().BeFalse();
            Slskd.IsFinished(DownloadHistoryEventType.DownloadImportIncomplete).Should().BeFalse();
            Slskd.IsFinished(DownloadHistoryEventType.FileImported).Should().BeFalse();
            Slskd.IsFinished(DownloadHistoryEventType.DownloadImported).Should().BeTrue();
            Slskd.IsFinished(DownloadHistoryEventType.DownloadFailed).Should().BeTrue();
            Slskd.IsFinished(DownloadHistoryEventType.DownloadIgnored).Should().BeTrue();
        }
    }
}
