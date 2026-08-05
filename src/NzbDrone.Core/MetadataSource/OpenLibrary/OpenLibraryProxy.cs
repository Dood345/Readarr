using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public interface IOpenLibraryProxy
    {
        List<OpenLibraryAuthorSearchDoc> SearchAuthors(string query);
        OpenLibraryAuthorResource GetAuthor(string authorKey);
        List<OpenLibrarySearchDoc> GetWorksByAuthor(string authorKey, int limit);
        List<OpenLibrarySearchDoc> SearchWorks(string query, int limit);
        OpenLibraryWorkResource GetWork(string workKey);
        List<OpenLibraryEditionResource> GetEditions(string workKey, int limit);
        OpenLibraryEditionResource GetEditionByIsbn(string isbn);
    }

    public class OpenLibraryProxy : IOpenLibraryProxy
    {
        private const string BaseUrl = "https://openlibrary.org";

        /// <summary>
        /// Only the fields actually mapped. Open Library's search index is wide and the default
        /// response carries a great deal that would be discarded.
        /// </summary>
        private const string SearchFields =
            "key,title,subtitle,first_publish_year,cover_i,edition_key,isbn,subject," +
            "author_key,author_name,ratings_average,ratings_count,number_of_pages_median,language";

        private static readonly JsonSerializerOptions SerializerSettings = new ()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly Logger _logger;

        public OpenLibraryProxy(ICachedHttpResponseService cachedHttpClient, Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _logger = logger;
        }

        public List<OpenLibraryAuthorSearchDoc> SearchAuthors(string query)
        {
            var response = Get<OpenLibraryAuthorSearchResponse>(
                $"{BaseUrl}/search/authors.json?q={Uri.EscapeDataString(query)}&limit=20");

            return response?.Docs ?? new List<OpenLibraryAuthorSearchDoc>();
        }

        public OpenLibraryAuthorResource GetAuthor(string authorKey)
        {
            return Get<OpenLibraryAuthorResource>($"{BaseUrl}/authors/{NormalizeKey(authorKey)}.json");
        }

        public List<OpenLibrarySearchDoc> GetWorksByAuthor(string authorKey, int limit)
        {
            var response = Get<OpenLibrarySearchResponse>(
                $"{BaseUrl}/search.json?author_key={NormalizeKey(authorKey)}&fields={SearchFields}&limit={limit}");

            return response?.Docs ?? new List<OpenLibrarySearchDoc>();
        }

        public List<OpenLibrarySearchDoc> SearchWorks(string query, int limit)
        {
            var response = Get<OpenLibrarySearchResponse>(
                $"{BaseUrl}/search.json?q={Uri.EscapeDataString(query)}&fields={SearchFields}&limit={limit}");

            return response?.Docs ?? new List<OpenLibrarySearchDoc>();
        }

        public OpenLibraryWorkResource GetWork(string workKey)
        {
            return Get<OpenLibraryWorkResource>($"{BaseUrl}/works/{NormalizeKey(workKey)}.json");
        }

        public List<OpenLibraryEditionResource> GetEditions(string workKey, int limit)
        {
            var response = Get<OpenLibraryEditionsResponse>(
                $"{BaseUrl}/works/{NormalizeKey(workKey)}/editions.json?limit={limit}");

            return response?.Entries ?? new List<OpenLibraryEditionResource>();
        }

        public OpenLibraryEditionResource GetEditionByIsbn(string isbn)
        {
            return Get<OpenLibraryEditionResource>($"{BaseUrl}/isbn/{isbn}.json");
        }

        /// <summary>
        /// Open Library uses both bare ("OL79034A") and path ("/authors/OL79034A") key forms
        /// depending on the endpoint, and round-trips one into the other.
        /// </summary>
        public static string NormalizeKey(string key)
        {
            if (key.IsNullOrWhiteSpace())
            {
                return key;
            }

            var trimmed = key.Trim('/');
            var slash = trimmed.LastIndexOf('/');

            return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
        }

        private T Get<T>(string url)
            where T : class
        {
            var request = new HttpRequestBuilder(url)
                .SetHeader("User-Agent", "Readarr/1.0 (+https://github.com/Readarr/Readarr)")
                .SetHeader("Accept", "application/json")
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _cachedHttpClient.Get(request, true, TimeSpan.FromHours(2));

            if (response.HasHttpError)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw new OpenLibraryException("Open Library request failed with {0}: {1}", response.StatusCode, url);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(response.Content, SerializerSettings);
            }
            catch (JsonException ex)
            {
                _logger.Warn(ex, "Could not parse Open Library response from {0}", url);
                return null;
            }
        }
    }
}
