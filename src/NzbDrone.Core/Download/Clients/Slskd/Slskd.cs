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
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.Slskd
{
    public class Slskd : DownloadClientBase<SlskdSettings>
    {
        private readonly ISlskdProxy _proxy;

        public override string Name => "Slskd";
        public override string Protocol => nameof(SoulseekDownloadProtocol);

        public Slskd(ISlskdProxy proxy,
                     IConfigService configService,
                     IDiskProvider diskProvider,
                     IRemotePathMappingService remotePathMappingService,
                     Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
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

            foreach (var user in transfers)
            {
                foreach (var directory in user.Directories ?? new List<SlskdTransferDirectory>())
                {
                    var item = BuildItem(user, directory);

                    if (item != null)
                    {
                        yield return item;
                    }
                }
            }
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

            var outputPath = _remotePathMappingService.RemapRemoteToLocal(
                Settings.BaseUrl,
                new OsPath(Path.Combine(Settings.DownloadPath, GetLeafName(directory.Directory))));

            return new DownloadClientItem
            {
                DownloadId = BuildDownloadId(user.Username, directory.Directory),
                Title = GetLeafName(directory.Directory),

                // Non-empty so CompletedDownloadService will still consider imports that Readarr
                // did not grab itself, matching what the blackhole clients do.
                Category = "Readarr",
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
            return $"Slskd-{username}-{directory.GetHashCode():X8}";
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
