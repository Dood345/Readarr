using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.MetadataSource.Wikidata
{
    public interface IWikidataProxy
    {
        /// <summary>
        /// The URL of the entity's image (property P18) as a file on Wikimedia Commons, or null when
        /// the entity has none Readarr can use as a cover.
        /// </summary>
        string GetImageUrl(string entityId);
    }

    /// <summary>
    /// Wikidata is only consulted for authors Open Library itself links to it (the
    /// <c>remote_ids.wikidata</c> field), so there is no name matching and no wrong-person risk.
    /// </summary>
    /// <remarks>
    /// Best-effort: any failure is "no image". The image is fetched as the original file under
    /// <c>upload.wikimedia.org</c> rather than a thumbnail, because Commons refuses to upscale and
    /// most author portraits are narrower than a useful thumbnail width, and because the cover
    /// service takes the file extension from the end of the URL.
    /// </remarks>
    public class WikidataProxy : IWikidataProxy
    {
        private const string ClaimsUrl = "https://www.wikidata.org/w/api.php?action=wbgetclaims&format=json&property=P18&entity=";
        private const string CommonsUrl = "https://upload.wikimedia.org/wikipedia/commons";
        private const string ImageProperty = "P18";

        private static readonly HashSet<string> CoverExtensions = new (StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

        private static readonly JsonSerializerOptions SerializerSettings = new ()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly Logger _logger;

        public WikidataProxy(ICachedHttpResponseService cachedHttpClient, Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _logger = logger;
        }

        public string GetImageUrl(string entityId)
        {
            if (entityId.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                var request = new HttpRequestBuilder(ClaimsUrl + Uri.EscapeDataString(entityId))
                    .SetHeader("Accept", "application/json")
                    .Build();

                request.AllowAutoRedirect = true;
                request.SuppressHttpError = true;

                var response = _cachedHttpClient.Get(request, true, TimeSpan.FromDays(1));

                if (response.HasHttpError)
                {
                    _logger.Debug("Wikidata lookup unavailable ({0}) for {1}", response.StatusCode, entityId);
                    return null;
                }

                var parsed = JsonSerializer.Deserialize<WikidataClaimsResponse>(response.Content, SerializerSettings);

                if (parsed?.Claims == null || !parsed.Claims.TryGetValue(ImageProperty, out var claims))
                {
                    return null;
                }

                return claims
                    .Select(x => x.MainSnak?.DataValue?.Value)
                    .Where(x => x.IsNotNullOrWhiteSpace())
                    .Select(CommonsFileUrl)
                    .FirstOrDefault(x => x != null);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Wikidata lookup failed for {0}", entityId);
                return null;
            }
        }

        /// <summary>
        /// Commons stores a file at <c>/a/ab/File_name.jpg</c>, where <c>ab</c> is the start of the
        /// MD5 of the file name with spaces as underscores and the first letter upper-cased. Only
        /// formats the cover resizer handles are returned.
        /// </summary>
        public static string CommonsFileUrl(string fileName)
        {
            if (fileName.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalized = fileName.Trim().Replace(' ', '_');
            normalized = char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);

            if (!CoverExtensions.Contains(Path.GetExtension(normalized)))
            {
                return null;
            }

            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
            var escaped = Uri.EscapeDataString(normalized);

            return $"{CommonsUrl}/{hash[0]}/{hash.Substring(0, 2)}/{escaped}";
        }

        private class WikidataClaimsResponse
        {
            [JsonPropertyName("claims")]
            public Dictionary<string, List<WikidataClaim>> Claims { get; set; }
        }

        private class WikidataClaim
        {
            [JsonPropertyName("mainsnak")]
            public WikidataSnak MainSnak { get; set; }
        }

        private class WikidataSnak
        {
            [JsonPropertyName("datavalue")]
            public WikidataDataValue DataValue { get; set; }
        }

        private class WikidataDataValue
        {
            [JsonPropertyName("value")]
            public string Value { get; set; }
        }
    }
}
