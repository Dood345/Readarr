using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.MetadataSource.Audible
{
    public interface IAudnexusProxy
    {
        AudnexusAuthorResource GetAuthor(string asin);
    }

    public class AudnexusAuthorResource
    {
        [JsonPropertyName("asin")]
        public string Asin { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    /// <summary>
    /// Audnexus (api.audnex.us) is the author API Audiobookshelf itself uses. Audible's catalogue
    /// endpoint only names an author and gives an ASIN; the portrait lives on the Audible author
    /// page, which Audnexus scrapes and serves as JSON.
    /// </summary>
    /// <remarks>
    /// Best-effort, like Audible itself: any failure is "no portrait", never a failed lookup.
    /// Audnexus answers <c>200</c> with <em>some</em> record for almost any ASIN - an unknown one
    /// came back as a tube of antiseptic cream - so callers must check the returned name against
    /// the author before trusting the image.
    /// </remarks>
    public class AudnexusProxy : IAudnexusProxy
    {
        private const string BaseUrl = "https://api.audnex.us/authors";

        private static readonly JsonSerializerOptions SerializerSettings = new ()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly Logger _logger;

        public AudnexusProxy(ICachedHttpResponseService cachedHttpClient, Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _logger = logger;
        }

        public AudnexusAuthorResource GetAuthor(string asin)
        {
            if (asin.IsNullOrWhiteSpace())
            {
                return null;
            }

            var url = $"{BaseUrl}/{Uri.EscapeDataString(asin)}";

            try
            {
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Accept", "application/json")
                    .Build();

                request.AllowAutoRedirect = true;
                request.SuppressHttpError = true;

                var response = _cachedHttpClient.Get(request, true, TimeSpan.FromDays(1));

                if (response.HasHttpError)
                {
                    _logger.Debug("Audnexus author lookup unavailable ({0}) for {1}", response.StatusCode, asin);
                    return null;
                }

                return JsonSerializer.Deserialize<AudnexusAuthorResource>(response.Content, SerializerSettings);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Audnexus author lookup failed for {0}", asin);
                return null;
            }
        }
    }
}
