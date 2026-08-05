using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.Audible
{
    public class AudibleSearchResponse
    {
        [JsonPropertyName("products")]
        public List<AudibleProductResource> Products { get; set; } = new ();

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }

    public class AudibleProductResource
    {
        [JsonPropertyName("asin")]
        public string Asin { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; }

        [JsonPropertyName("authors")]
        public List<AudibleContributor> Authors { get; set; }

        [JsonPropertyName("narrators")]
        public List<AudibleContributor> Narrators { get; set; }

        [JsonPropertyName("series")]
        public List<AudibleSeries> Series { get; set; }

        [JsonPropertyName("runtime_length_min")]
        public int? RuntimeLengthMin { get; set; }

        [JsonPropertyName("release_date")]
        public DateTime? ReleaseDate { get; set; }

        [JsonPropertyName("publisher_name")]
        public string PublisherName { get; set; }

        [JsonPropertyName("merchandising_summary")]
        public string MerchandisingSummary { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("product_images")]
        public Dictionary<string, string> ProductImages { get; set; }
    }

    public class AudibleContributor
    {
        [JsonPropertyName("asin")]
        public string Asin { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class AudibleSeries
    {
        [JsonPropertyName("asin")]
        public string Asin { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("sequence")]
        public string Sequence { get; set; }
    }
}
