using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class SlskdSearchRequest
    {
        public string Id { get; set; }
        public string SearchText { get; set; }
    }

    public class SlskdSearchState
    {
        public string Id { get; set; }
        public string SearchText { get; set; }
        public bool IsComplete { get; set; }
        public int FileCount { get; set; }
        public int ResponseCount { get; set; }
        public string State { get; set; }
    }

    public class SlskdSearchResponse
    {
        public string Username { get; set; }
        public int FileCount { get; set; }
        public int QueueLength { get; set; }
        public long UploadSpeed { get; set; }
        public bool HasFreeUploadSlot { get; set; }
        public List<SlskdFile> Files { get; set; }
    }

    public class SlskdFile
    {
        public string Filename { get; set; }
        public long Size { get; set; }
        public int Length { get; set; }
        public int? BitDepth { get; set; }
        public int? SampleRate { get; set; }
        public int? BitRate { get; set; }
        public bool IsLocked { get; set; }
    }

    public class SlskdTransferUser
    {
        public string Username { get; set; }
        public List<SlskdTransferDirectory> Directories { get; set; }
    }

    public class SlskdTransferDirectory
    {
        public string Directory { get; set; }
        public int FileCount { get; set; }
        public List<SlskdTransferFile> Files { get; set; }
    }

    public class SlskdTransferFile
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Filename { get; set; }
        public long Size { get; set; }
        public long BytesTransferred { get; set; }
        public double PercentComplete { get; set; }
        public double AverageSpeed { get; set; }

        /// <summary>
        /// Soulseek.NET transfer state, e.g. "Queued, Remotely", "InProgress",
        /// "Completed, Succeeded", "Completed, Errored", "Completed, Cancelled".
        /// </summary>
        public string State { get; set; }

        public string Direction { get; set; }
    }

    /// <summary>
    /// Identifies a grabbed release. slskd has no concept of a "release", only files owned by a
    /// peer, so everything the download client needs to enqueue the album is carried here and
    /// round-tripped through ReleaseInfo.DownloadUrl as base64 JSON. Keeping it a plain string
    /// means it survives being persisted as a pending release and re-read later.
    /// </summary>
    public class SlskdReleaseLocator
    {
        public string Username { get; set; }
        public string Directory { get; set; }
        public List<SlskdLocatorFile> Files { get; set; }

        public string ToDownloadUrl()
        {
            var json = this.ToJson();

            return "slskd://" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        }

        public static SlskdReleaseLocator FromDownloadUrl(string downloadUrl)
        {
            if (downloadUrl == null || !downloadUrl.StartsWith("slskd://", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                var payload = downloadUrl.Substring("slskd://".Length);
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));

                return Json.Deserialize<SlskdReleaseLocator>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public long TotalSize => Files?.Sum(f => f.Size) ?? 0;
    }

    public class SlskdLocatorFile
    {
        public string Filename { get; set; }
        public long Size { get; set; }
    }
}
