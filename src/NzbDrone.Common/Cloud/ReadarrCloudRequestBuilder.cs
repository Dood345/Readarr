using Microsoft.Extensions.Options;
using NzbDrone.Common.Http;
using NzbDrone.Common.Options;

namespace NzbDrone.Common.Cloud
{
    public interface IReadarrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
        IHttpRequestBuilderFactory Metadata { get; }
    }

    public class ReadarrCloudRequestBuilder : IReadarrCloudRequestBuilder
    {
        // Readarr's metadata service was shut down when the project was retired, so this default
        // no longer resolves to anything useful. Override with Readarr__Metadata__Source to point
        // at a replacement such as rreading-glasses.
        public const string DefaultMetadataSource = "https://api.bookinfo.club/v1/{route}";

        public ReadarrCloudRequestBuilder(IOptions<MetadataOptions> metadataOptions)
        {
            //TODO: Create Update Endpoint
            Services = new HttpRequestBuilder("https://readarr.servarr.com/v1/")
                .CreateFactory();

            Metadata = new HttpRequestBuilder(ResolveMetadataSource(metadataOptions?.Value?.Source))
                .CreateFactory();
        }

        internal static string ResolveMetadataSource(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return DefaultMetadataSource;
            }

            // Configuring a bare host is the obvious thing to try, so accept it rather than
            // failing with an unhelpful 404 on every lookup. Only "/{route}" is appended, which
            // matches both MetadataRequestBuilder's handling of the ConfigService override and
            // rreading-glasses, which serves /author/{id} and /work/{id} at the root. The dead
            // bookinfo.club's "/v1" prefix was part of that host's layout, not a convention.
            return configured.Contains("{route}")
                ? configured
                : configured.TrimEnd('/') + "/{route}";
        }

        public IHttpRequestBuilderFactory Services { get; }

        public IHttpRequestBuilderFactory Metadata { get; }
    }
}
