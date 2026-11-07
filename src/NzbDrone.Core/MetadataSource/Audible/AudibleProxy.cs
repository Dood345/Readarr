using System;
using System.Collections.Generic;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.MetadataSource.Audible
{
    public interface IAudibleProxy
    {
        List<AudibleProductResource> SearchByAuthor(string author, int limit);
        List<AudibleProductResource> Search(string keywords, int limit);
    }

    /// <summary>
    /// Audible's public catalogue endpoint. Used only to enrich the Open Library spine with the
    /// things Open Library does not carry: audiobook editions, narrators, runtime and series
    /// sequence.
    /// </summary>
    /// <remarks>
    /// This is Audible's own app API rather than a documented public one. It needs no key, but it
    /// can change without notice - every call is best-effort and failures degrade to "no audiobook
    /// data" rather than failing the lookup.
    /// </remarks>
    public class AudibleProxy : IAudibleProxy
    {
        private const string BaseUrl = "https://api.audible.com/1.0/catalog/products";
        private const string ResponseGroups = "product_desc,contributors,series,product_attrs,media";

        private static readonly JsonSerializerOptions SerializerSettings = new ()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly Logger _logger;

        public AudibleProxy(ICachedHttpResponseService cachedHttpClient, Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _logger = logger;
        }

        public List<AudibleProductResource> SearchByAuthor(string author, int limit)
        {
            return Query($"author={Uri.EscapeDataString(author)}", limit);
        }

        public List<AudibleProductResource> Search(string keywords, int limit)
        {
            return Query($"keywords={Uri.EscapeDataString(keywords)}", limit);
        }

        private List<AudibleProductResource> Query(string filter, int limit)
        {
            // Audible caps num_results at 50.
            var count = Math.Min(Math.Max(limit, 1), 50);
            var url = $"{BaseUrl}?{filter}&num_results={count}&products_sort_by=Relevance&response_groups={ResponseGroups}";

            try
            {
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Accept", "application/json")
                    .Build();

                request.AllowAutoRedirect = true;
                request.SuppressHttpError = true;

                var response = _cachedHttpClient.Get(request, true, TimeSpan.FromHours(6));

                if (response.HasHttpError)
                {
                    _logger.Debug("Audible enrichment unavailable ({0}) for {1}", response.StatusCode, filter);
                    return new List<AudibleProductResource>();
                }

                var parsed = JsonSerializer.Deserialize<AudibleSearchResponse>(response.Content, SerializerSettings);

                return parsed?.Products ?? new List<AudibleProductResource>();
            }
            catch (Exception ex)
            {
                // Enrichment only - never fail a lookup because Audible changed or is unreachable.
                _logger.Debug(ex, "Audible enrichment failed for {0}", filter);
                return new List<AudibleProductResource>();
            }
        }
    }
}
