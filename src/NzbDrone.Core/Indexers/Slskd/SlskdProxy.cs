using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Indexers.Slskd
{
    /// <summary>
    /// The connection details the slskd indexer and download client share. They are separate
    /// providers with separate settings, but both talk to the same daemon.
    /// </summary>
    public interface ISlskdConnectionSettings
    {
        string BaseUrl { get; }
        string ApiKey { get; }
    }

    public interface ISlskdProxy
    {
        Task TestConnection(ISlskdConnectionSettings settings);
        Task<List<SlskdSearchResponse>> Search(string query, SlskdIndexerSettings settings, CancellationToken cancellationToken);
        Task Enqueue(string username, IEnumerable<SlskdLocatorFile> files, ISlskdConnectionSettings settings);
        Task<List<SlskdTransferUser>> GetTransfers(ISlskdConnectionSettings settings);
        Task RemoveTransfer(string username, string id, ISlskdConnectionSettings settings);
    }

    public class SlskdProxy : ISlskdProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public SlskdProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task TestConnection(ISlskdConnectionSettings settings)
        {
            var request = BuildRequest("application", settings).Build();

            await _httpClient.ExecuteAsync(request);
        }

        public async Task<List<SlskdSearchResponse>> Search(string query, SlskdIndexerSettings settings, CancellationToken cancellationToken)
        {
            var searchId = Guid.NewGuid().ToString();

            var startRequest = BuildRequest("searches", settings).Post().Build();
            startRequest.SetContent(new SlskdSearchRequest { Id = searchId, SearchText = query }.ToJson());
            startRequest.Headers.ContentType = "application/json";

            await _httpClient.ExecuteAsync(startRequest);

            try
            {
                await WaitForCompletion(searchId, settings, cancellationToken);

                var responsesRequest = BuildRequest($"searches/{searchId}/responses", settings).Build();
                var response = await _httpClient.ExecuteAsync(responsesRequest);

                return Json.Deserialize<List<SlskdSearchResponse>>(response.Content) ?? new List<SlskdSearchResponse>();
            }
            finally
            {
                // slskd keeps completed searches around for its own retention window; drop ours so
                // repeated Lidarr searches don't pile up in the peer's search history view.
                try
                {
                    var deleteRequest = BuildRequest($"searches/{searchId}", settings).Build();
                    deleteRequest.Method = HttpMethod.Delete;

                    await _httpClient.ExecuteAsync(deleteRequest);
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Unable to clean up slskd search {0}", searchId);
                }
            }
        }

        private async Task WaitForCompletion(string searchId, SlskdIndexerSettings settings, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(settings.SearchTimeout);

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                var request = BuildRequest($"searches/{searchId}", settings).Build();
                var response = await _httpClient.ExecuteAsync(request);
                var state = Json.Deserialize<SlskdSearchState>(response.Content);

                if (state != null && state.IsComplete)
                {
                    return;
                }
            }

            _logger.Debug("slskd search {0} did not complete within {1}s, using partial results", searchId, settings.SearchTimeout);
        }

        public async Task Enqueue(string username, IEnumerable<SlskdLocatorFile> files, ISlskdConnectionSettings settings)
        {
            var request = BuildRequest($"transfers/downloads/{Uri.EscapeDataString(username)}", settings).Post().Build();

            request.SetContent(files.ToJson());
            request.Headers.ContentType = "application/json";

            await _httpClient.ExecuteAsync(request);
        }

        public async Task<List<SlskdTransferUser>> GetTransfers(ISlskdConnectionSettings settings)
        {
            var request = BuildRequest("transfers/downloads", settings).Build();
            var response = await _httpClient.ExecuteAsync(request);

            return Json.Deserialize<List<SlskdTransferUser>>(response.Content) ?? new List<SlskdTransferUser>();
        }

        public async Task RemoveTransfer(string username, string id, ISlskdConnectionSettings settings)
        {
            var request = BuildRequest($"transfers/downloads/{Uri.EscapeDataString(username)}/{id}", settings).Build();
            request.Method = HttpMethod.Delete;

            await _httpClient.ExecuteAsync(request);
        }

        private HttpRequestBuilder BuildRequest(string path, ISlskdConnectionSettings settings)
        {
            var baseUrl = settings.BaseUrl.TrimEnd('/');

            return new HttpRequestBuilder($"{baseUrl}/api/v0/{path}")
            {
                LogResponseContent = false
            }
            .SetHeader("X-API-Key", settings.ApiKey)
            .Accept(HttpAccept.Json);
        }
    }
}
