using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.Slskd
{
    public class Slskd : DownloadClientBase<SlskdSettings>
    {
        private readonly ISlskdProxy _proxy;
        private readonly IDownloadHistoryService _downloadHistoryService;

        public override string Name => "Slskd";
        public override string Protocol => nameof(SoulseekDownloadProtocol);

        public Slskd(ISlskdProxy proxy,
                     IDownloadHistoryService downloadHistoryService,
                     IConfigService configService,
                     IDiskProvider diskProvider,
                     IRemotePathMappingService remotePathMappingService,
                     Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
            _downloadHistoryService = downloadHistoryService;
        }

        public override async Task<string> Download(RemoteBook remoteBook, IIndexer indexer)
        {
            var locator = SlskdReleaseLocator.FromDownloadUrl(remoteBook.Release.DownloadUrl);

            if (locator == null)
            {
                throw new DownloadClientException("Release did not come from the Slskd indexer, so it cannot be sent to slskd");
            }

            await _proxy.Enqueue(locator.Username, locator.Files, Settings);

            // slskd has no per-grab identifier, so downloads are tracked by the peer and folder the
            // files came from. GetItems reconstructs the same id from the transfer list.
            return BuildDownloadId(locator.Username, locator.Directory);
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            List<SlskdTransferUser> transfers;

            try
            {
                transfers = _proxy.GetTransfers(Settings).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to retrieve transfers from slskd");

                yield break;
            }

            var listed = new HashSet<string>();

            foreach (var user in transfers)
            {
                foreach (var directory in user.Directories ?? new List<SlskdTransferDirectory>())
                {
                    var item = BuildItem(user, directory);

                    if (item != null)
                    {
                        listed.Add(item.DownloadId);
                        yield return item;
                    }
                }
            }

            foreach (var item in GetForgottenItems(listed))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Lists completed folders that slskd no longer knows about. The transfer list is slskd's
        /// only record of a download and anything sharing the daemon can clear it - soularr calls
        /// "remove completed downloads" at the end of every run. A Readarr download whose import
        /// failed therefore vanished from the queue within minutes while its files sat orphaned in
        /// the download folder, and the only way back was Manual Import. Readarr's own download
        /// history stands in for the record slskd lost: a recent grab sent to this client whose
        /// folder is still on disk, and which was never imported, failed or ignored, is reported as
        /// a completed download. A grab slskd still lists is left to the transfer list, so a folder
        /// whose files are still arriving is never offered as complete.
        /// </summary>
        private List<DownloadClientItem> GetForgottenItems(HashSet<string> listedIds)
        {
            var items = new List<DownloadClientItem>();

            try
            {
                var grabs = _downloadHistoryService.GetRecentGrabs(Definition.Id, _configService.DownloadClientHistoryLimit);

                foreach (var grab in grabs)
                {
                    // Add also drops a second grab of the same peer folder, which shares the id.
                    if (grab.DownloadId.IsNullOrWhiteSpace() || !listedIds.Add(grab.DownloadId))
                    {
                        continue;
                    }

                    var locator = SlskdReleaseLocator.FromDownloadUrl(grab.Release?.DownloadUrl);

                    if (locator == null)
                    {
                        continue;
                    }

                    var outputPath = GetOutputPath(locator.Directory);

                    if (!_diskProvider.FolderExists(outputPath.FullPath))
                    {
                        continue;
                    }

                    var latest = _downloadHistoryService.GetLatestDownloadHistoryItem(grab.DownloadId);

                    if (latest == null || IsFinished(latest.EventType))
                    {
                        continue;
                    }

                    _logger.Trace("slskd no longer lists '{0}', offering the folder {1} for import", grab.SourceTitle, outputPath);

                    items.Add(new DownloadClientItem
                    {
                        DownloadId = grab.DownloadId,
                        Title = GetLeafName(locator.Directory),
                        Category = null,
                        TotalSize = locator.TotalSize,
                        RemainingSize = 0,
                        Status = DownloadItemStatus.Completed,
                        CanBeRemoved = true,
                        CanMoveFiles = true,
                        OutputPath = outputPath,
                        DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to check the slskd download folder for downloads the daemon has forgotten");
            }

            return items;
        }

        /// <summary>
        /// Whether a download's most recent history event means Readarr is done with it. A grab
        /// or an incomplete import is still outstanding; an imported, failed or ignored download
        /// is not re-offered even when its folder is still on disk.
        /// </summary>
        internal static bool IsFinished(DownloadHistoryEventType eventType)
        {
            return eventType == DownloadHistoryEventType.DownloadImported ||
                   eventType == DownloadHistoryEventType.DownloadFailed ||
                   eventType == DownloadHistoryEventType.DownloadIgnored;
        }

        private DownloadClientItem BuildItem(SlskdTransferUser user, SlskdTransferDirectory directory)
        {
            var files = directory.Files?.Where(f => f.Direction == null || f.Direction.EqualsIgnoreCase("Download")).ToList();

            if (files == null || files.Empty())
            {
                return null;
            }

            var totalSize = files.Sum(f => f.Size);
            var transferred = files.Sum(f => f.BytesTransferred);

            var outputPath = GetOutputPath(directory.Directory);

            return new DownloadClientItem
            {
                DownloadId = BuildDownloadId(user.Username, directory.Directory),
                Title = GetLeafName(directory.Directory),

                // A download Readarr grabbed is matched through its history and imported whatever
                // the category. A non-empty category is what makes CompletedDownloadService also
                // consider folders Readarr did not grab, so it is set only for folders that look
                // like books - see ShouldAdopt.
                Category = ShouldAdopt(files, Settings) ? "Readarr" : null,
                TotalSize = totalSize,
                RemainingSize = Math.Max(0, totalSize - transferred),
                Status = MapStatus(files),
                CanBeRemoved = true,
                CanMoveFiles = true,
                OutputPath = outputPath,
                DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false)
            };
        }

        internal static DownloadItemStatus MapStatus(List<SlskdTransferFile> files)
        {
            var states = files.Select(f => f.State ?? string.Empty).ToList();

            if (states.Any(s => s.ContainsIgnoreCase("Errored") || s.ContainsIgnoreCase("Rejected") || s.ContainsIgnoreCase("TimedOut")))
            {
                return DownloadItemStatus.Failed;
            }

            if (states.Any(s => s.ContainsIgnoreCase("Cancelled")))
            {
                return DownloadItemStatus.Warning;
            }

            // Only complete once every file in the folder is, otherwise Readarr would try to import
            // a partially transferred album.
            if (states.All(s => s.ContainsIgnoreCase("Succeeded")))
            {
                return DownloadItemStatus.Completed;
            }

            if (states.Any(s => s.ContainsIgnoreCase("InProgress")))
            {
                return DownloadItemStatus.Downloading;
            }

            return DownloadItemStatus.Queued;
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            var transfers = _proxy.GetTransfers(Settings).GetAwaiter().GetResult();

            foreach (var user in transfers)
            {
                foreach (var directory in user.Directories ?? new List<SlskdTransferDirectory>())
                {
                    if (BuildDownloadId(user.Username, directory.Directory) != item.DownloadId)
                    {
                        continue;
                    }

                    foreach (var file in directory.Files ?? new List<SlskdTransferFile>())
                    {
                        _proxy.RemoveTransfer(user.Username, file.Id, Settings).GetAwaiter().GetResult();
                    }
                }
            }

            if (deleteData)
            {
                DeleteItemData(item);
            }
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = Settings.BaseUrl.Contains("localhost") || Settings.BaseUrl.Contains("127.0.0.1"),
                OutputRootFolders = new List<OsPath> { _remotePathMappingService.RemapRemoteToLocal(Settings.BaseUrl, new OsPath(Settings.DownloadPath)) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                _proxy.TestConnection(Settings).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to slskd");

                failures.Add(new ValidationFailure("BaseUrl", $"Unable to connect to slskd: {ex.Message}"));

                return;
            }

            failures.AddIfNotNull(TestFolder(Settings.DownloadPath, "DownloadPath"));
        }

        private static string BuildDownloadId(string username, string directory)
        {
            return SlskdIds.ForDownload(username, directory);
        }

        /// <summary>
        /// Where slskd puts a finished folder: its download directory plus the leaf of the peer's
        /// folder, as Readarr sees it after remote path mapping.
        /// </summary>
        private OsPath GetOutputPath(string directory)
        {
            return _remotePathMappingService.RemapRemoteToLocal(
                Settings.BaseUrl,
                new OsPath(Path.Combine(Settings.DownloadPath, GetLeafName(directory))));
        }

        /// <summary>
        /// Whether a finished folder Readarr did not grab itself should be offered for import.
        /// slskd's download folder is shared with whatever else uses the daemon - here Lidarr's
        /// music via soularr - and adopting everything meant a full tag scan and a queue warning
        /// for every album that landed there (300 of them in two months). A folder is adopted only
        /// when the user asked for it and it holds at least one file in a book format.
        /// </summary>
        internal static bool ShouldAdopt(List<SlskdTransferFile> files, SlskdSettings settings)
        {
            if (!settings.ImportUngrabbedDownloads)
            {
                return false;
            }

            var bookExtensions = (settings.BookExtensions ?? string.Empty)
                .Split(',')
                .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
                .Where(e => e.IsNotNullOrWhiteSpace())
                .ToHashSet();

            if (bookExtensions.Empty())
            {
                return true;
            }

            return files.Any(f => bookExtensions.Contains(GetExtension(f.Filename)));
        }

        private static string GetExtension(string filename)
        {
            if (filename.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var index = filename.LastIndexOf('.');

            return index < 0 ? string.Empty : filename.Substring(index + 1).ToLowerInvariant();
        }

        private static string GetLeafName(string directory)
        {
            if (directory.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var index = directory.LastIndexOf('\\');

            return index < 0 ? directory : directory.Substring(index + 1);
        }
    }
}
