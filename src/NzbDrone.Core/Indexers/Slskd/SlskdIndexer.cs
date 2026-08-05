using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class SlskdIndexer : IndexerBase<SlskdIndexerSettings>
    {
        private readonly ISlskdProxy _proxy;

        public override string Name => "Slskd";
        public override string Protocol => nameof(SoulseekDownloadProtocol);

        // Soulseek has no feed to poll; results only exist in response to a search.
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;

        public SlskdIndexer(ISlskdProxy proxy,
                            IIndexerStatusService indexerStatusService,
                            IConfigService configService,
                            IParsingService parsingService,
                            Logger logger)
            : base(indexerStatusService, configService, parsingService, logger)
        {
            _proxy = proxy;
        }

        public override Task<IList<ReleaseInfo>> FetchRecent()
        {
            return Task.FromResult<IList<ReleaseInfo>>(new List<ReleaseInfo>());
        }

        public override Task<IList<ReleaseInfo>> Fetch(BookSearchCriteria searchCriteria)
        {
            var query = $"{searchCriteria.AuthorQuery} {searchCriteria.BookQuery}".Trim();

            return Search(query, searchCriteria.Author?.Name);
        }

        public override Task<IList<ReleaseInfo>> Fetch(AuthorSearchCriteria searchCriteria)
        {
            return Search(searchCriteria.AuthorQuery, searchCriteria.Author?.Name);
        }

        public override HttpRequest GetDownloadRequest(string link)
        {
            // Nothing is fetched over HTTP: the download client enqueues the files with slskd
            // directly using the locator encoded in the release's download url.
            throw new NotSupportedException("Soulseek releases are handed to the download client directly");
        }

        private async Task<IList<ReleaseInfo>> Search(string query, string artistName)
        {
            if (query.IsNullOrWhiteSpace())
            {
                return new List<ReleaseInfo>();
            }

            var responses = await _proxy.Search(query, Settings, CancellationToken.None);

            return BuildReleases(responses, artistName, Settings);
        }

        public static List<ReleaseInfo> BuildReleases(IEnumerable<SlskdSearchResponse> responses, string artistName, SlskdIndexerSettings settings)
        {
            var releases = new List<ReleaseInfo>();

            if (responses == null)
            {
                return releases;
            }

            var allowedExtensions = (settings.AllowedExtensions ?? string.Empty)
                .Split(',')
                .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
                .Where(e => e.IsNotNullOrWhiteSpace())
                .ToHashSet();

            foreach (var response in responses)
            {
                if (response?.Files == null || response.Username.IsNullOrWhiteSpace())
                {
                    continue;
                }

                if (settings.MaximumQueueLength > 0 && response.QueueLength > settings.MaximumQueueLength)
                {
                    continue;
                }

                var candidates = response.Files
                    .Where(f => settings.IncludeLockedResults || !f.IsLocked)
                    .Where(f => allowedExtensions.Empty() || allowedExtensions.Contains(GetExtension(f.Filename)))
                    .ToList();

                // Soulseek returns loose files, not albums. Everything a peer shares from the same
                // folder is treated as one release, which is the closest thing to an album edition
                // that the protocol exposes.
                foreach (var group in candidates.GroupBy(f => GetDirectory(f.Filename)))
                {
                    if (group.Key.IsNullOrWhiteSpace() || group.Count() < settings.MinimumFileCount)
                    {
                        continue;
                    }

                    releases.Add(BuildRelease(response, group.Key, group.ToList(), artistName));
                }
            }

            return releases;
        }

        private static ReleaseInfo BuildRelease(SlskdSearchResponse response, string directory, List<SlskdFile> files, string artistName)
        {
            var locator = new SlskdReleaseLocator
            {
                Username = response.Username,
                Directory = directory,
                Files = files.Select(f => new SlskdLocatorFile { Filename = f.Filename, Size = f.Size }).ToList()
            };

            var folderName = GetLeafName(directory);

            // The folder name alone is often just "1975 - Wish You Were Here", which gives the
            // parser no artist to work with. Prefixing the searched artist keeps titles parseable
            // and readable in the queue and history.
            var title = artistName.IsNotNullOrWhiteSpace() && !folderName.ToLowerInvariant().Contains(artistName.ToLowerInvariant())
                ? $"{artistName} - {folderName}"
                : folderName;

            var extension = GetExtension(files[0].Filename);

            if (extension.IsNotNullOrWhiteSpace() && !title.ToLowerInvariant().Contains(extension))
            {
                title = $"{title} [{extension.ToUpperInvariant()}]";
            }

            return new ReleaseInfo
            {
                Guid = $"Slskd-{response.Username}-{directory.GetHashCode():X8}",
                Title = title,
                Size = files.Sum(f => f.Size),
                DownloadUrl = locator.ToDownloadUrl(),
                InfoUrl = null,
                PublishDate = DateTime.UtcNow,
                DownloadProtocol = nameof(SoulseekDownloadProtocol)
            };
        }

        private static string GetDirectory(string filename)
        {
            if (filename.IsNullOrWhiteSpace())
            {
                return null;
            }

            // Soulseek paths are Windows style regardless of the peer's platform.
            var index = filename.LastIndexOf('\\');

            return index <= 0 ? null : filename.Substring(0, index);
        }

        private static string GetLeafName(string directory)
        {
            var index = directory.LastIndexOf('\\');

            return index < 0 ? directory : directory.Substring(index + 1);
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

        protected override async Task Test(List<ValidationFailure> failures)
        {
            try
            {
                await _proxy.TestConnection(Settings);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to slskd");

                failures.Add(new ValidationFailure(string.Empty, $"Unable to connect to slskd: {ex.Message}"));
            }
        }
    }
}
