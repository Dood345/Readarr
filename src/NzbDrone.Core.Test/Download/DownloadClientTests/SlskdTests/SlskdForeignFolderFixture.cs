using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.SlskdTests
{
    /// <summary>
    /// The slskd daemon is shared with Lidarr (through soularr), so most of its transfers are not
    /// Readarr's. Those used to be returned with a null category, which kept them out of the import
    /// step but still put them in Readarr's queue as unknown-author items. The client now returns
    /// only folders Readarr grabbed itself or would adopt.
    /// </summary>
    [TestFixture]
    public class SlskdForeignFolderFixture : DownloadClientFixtureBase<Slskd>
    {
        private const string Username = "LinusParkourTips";
        private const string MusicDirectory = @"@@music\Brand New\(2006) The Devil and God Are Raging Inside Me";
        private const string BookDirectory = @"@@books\Frank Herbert\Dune Messiah";

        private SlskdSettings _settings;
        private List<SlskdTransferUser> _transfers;

        [SetUp]
        public void Setup()
        {
            _settings = new SlskdSettings
            {
                BaseUrl = "http://localhost:5030",
                ApiKey = "key",
                DownloadPath = @"C:\downloads\music".AsOsAgnostic()
            };

            Subject.Definition = new DownloadClientDefinition
            {
                Id = 7,
                Name = "slskd",
                Settings = _settings
            };

            _transfers = new List<SlskdTransferUser>();

            Mocker.GetMock<ISlskdProxy>()
                  .Setup(p => p.GetTransfers(It.IsAny<ISlskdConnectionSettings>()))
                  .ReturnsAsync(() => _transfers);

            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetRecentGrabs(7, It.IsAny<int>()))
                  .Returns(new List<DownloadHistory>());
        }

        private void GivenTransfer(string directory, params string[] files)
        {
            _transfers.Add(new SlskdTransferUser
            {
                Username = Username,
                Directories = new List<SlskdTransferDirectory>
                {
                    new SlskdTransferDirectory
                    {
                        Directory = directory,
                        Files = files.Select((f, i) => new SlskdTransferFile
                        {
                            Id = $"t{i}",
                            Filename = directory + @"\" + f,
                            Size = 100,
                            BytesTransferred = 50,
                            State = "InProgress"
                        }).ToList()
                    }
                }
            });
        }

        private void GivenReadarrGrabbed(string directory)
        {
            var downloadId = SlskdIds.ForDownload(Username, directory);

            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetLatestGrab(downloadId))
                  .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed, DownloadId = downloadId });
        }

        [Test]
        public void should_not_list_a_music_folder_readarr_did_not_grab()
        {
            // Lidarr's album, downloading through the shared daemon.
            GivenTransfer(MusicDirectory, "01 Sowing Season.flac", "02 Millstone.flac");

            Subject.GetItems().Should().BeEmpty();
        }

        [Test]
        public void should_list_a_music_folder_readarr_grabbed()
        {
            // A grab is matched through history whatever the folder holds, so an audiobook ripped
            // to flac is still Readarr's.
            GivenTransfer(MusicDirectory, "01 Sowing Season.flac");
            GivenReadarrGrabbed(MusicDirectory);

            var item = Subject.GetItems().Should().ContainSingle().Subject;

            VerifyIdentifiable(item);
            item.DownloadId.Should().Be(SlskdIds.ForDownload(Username, MusicDirectory));
            item.Category.Should().BeNull();
            item.Status.Should().Be(DownloadItemStatus.Downloading);
        }

        [Test]
        public void should_list_an_adoptable_book_folder_readarr_did_not_grab()
        {
            GivenTransfer(BookDirectory, "Dune Messiah.m4b", "cover.jpg");

            var item = Subject.GetItems().Should().ContainSingle().Subject;

            item.Category.Should().Be("Readarr");
        }

        [Test]
        public void should_not_list_a_book_folder_readarr_did_not_grab_when_adoption_is_off()
        {
            _settings.ImportUngrabbedDownloads = false;

            GivenTransfer(BookDirectory, "Dune Messiah.m4b");

            Subject.GetItems().Should().BeEmpty();
        }

        [Test]
        public void should_only_keep_readarrs_folders_out_of_a_mixed_transfer_list()
        {
            GivenTransfer(MusicDirectory, "01 Sowing Season.flac");
            GivenTransfer(BookDirectory, "Dune Messiah.m4b");

            var items = Subject.GetItems().ToList();

            items.Should().ContainSingle();
            items.Single().DownloadId.Should().Be(SlskdIds.ForDownload(Username, BookDirectory));
        }

        [Test]
        public void should_keep_a_folder_when_the_history_lookup_fails()
        {
            // Fail open: a database hiccup must not hide a download Readarr may own.
            GivenTransfer(MusicDirectory, "01 Sowing Season.flac");

            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetLatestGrab(It.IsAny<string>()))
                  .Throws(new InvalidOperationException("database locked"));

            Subject.GetItems().Should().ContainSingle();
        }
    }
}
