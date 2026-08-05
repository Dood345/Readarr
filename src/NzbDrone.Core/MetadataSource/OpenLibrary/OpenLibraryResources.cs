using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Open Library returns free-text fields either as a bare string or as
    /// { "type": "/type/text", "value": "..." }, inconsistently and per-record.
    /// </summary>
    public class OpenLibraryTextConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return doc.RootElement.TryGetProperty("value", out var value) ? value.GetString() : null;
            }

            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public class OpenLibraryAuthorSearchResponse
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; set; }

        [JsonPropertyName("docs")]
        public List<OpenLibraryAuthorSearchDoc> Docs { get; set; } = new ();
    }

    public class OpenLibraryAuthorSearchDoc
    {
        /// <summary>Bare key, e.g. "OL79034A" - not the "/authors/..." path form.</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("alternate_names")]
        public List<string> AlternateNames { get; set; }

        [JsonPropertyName("birth_date")]
        public string BirthDate { get; set; }

        [JsonPropertyName("death_date")]
        public string DeathDate { get; set; }

        [JsonPropertyName("top_work")]
        public string TopWork { get; set; }

        [JsonPropertyName("work_count")]
        public int WorkCount { get; set; }
    }

    public class OpenLibraryAuthorResource
    {
        /// <summary>Path form, e.g. "/authors/OL79034A".</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("personal_name")]
        public string PersonalName { get; set; }

        [JsonPropertyName("alternate_names")]
        public List<string> AlternateNames { get; set; }

        [JsonPropertyName("bio")]
        [JsonConverter(typeof(OpenLibraryTextConverter))]
        public string Bio { get; set; }

        [JsonPropertyName("birth_date")]
        public string BirthDate { get; set; }

        [JsonPropertyName("death_date")]
        public string DeathDate { get; set; }

        [JsonPropertyName("photos")]
        public List<int> Photos { get; set; }
    }

    public class OpenLibrarySearchResponse
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; set; }

        [JsonPropertyName("docs")]
        public List<OpenLibrarySearchDoc> Docs { get; set; } = new ();
    }

    /// <summary>
    /// A work as returned by /search.json. This is the bulk path: one request yields a whole
    /// bibliography with edition counts, ISBNs, covers and ratings, where walking
    /// /authors/{k}/works.json and then each work's editions would be one request per work.
    /// </summary>
    public class OpenLibrarySearchDoc
    {
        /// <summary>Path form, e.g. "/works/OL893414W".</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; }

        [JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; set; }

        [JsonPropertyName("cover_i")]
        public int? CoverId { get; set; }

        [JsonPropertyName("edition_key")]
        public List<string> EditionKeys { get; set; }

        [JsonPropertyName("isbn")]
        public List<string> Isbn { get; set; }

        [JsonPropertyName("subject")]
        public List<string> Subject { get; set; }

        [JsonPropertyName("author_key")]
        public List<string> AuthorKeys { get; set; }

        [JsonPropertyName("author_name")]
        public List<string> AuthorNames { get; set; }

        [JsonPropertyName("ratings_average")]
        public double? RatingsAverage { get; set; }

        [JsonPropertyName("ratings_count")]
        public int? RatingsCount { get; set; }

        [JsonPropertyName("number_of_pages_median")]
        public int? NumberOfPagesMedian { get; set; }

        [JsonPropertyName("language")]
        public List<string> Language { get; set; }
    }

    public class OpenLibraryWorkResource
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("description")]
        [JsonConverter(typeof(OpenLibraryTextConverter))]
        public string Description { get; set; }

        [JsonPropertyName("covers")]
        public List<int> Covers { get; set; }

        [JsonPropertyName("subjects")]
        public List<string> Subjects { get; set; }

        [JsonPropertyName("authors")]
        public List<OpenLibraryWorkAuthor> Authors { get; set; }

        [JsonPropertyName("series")]
        public List<OpenLibraryWorkSeries> Series { get; set; }
    }

    public class OpenLibraryWorkAuthor
    {
        [JsonPropertyName("author")]
        public OpenLibraryKeyRef Author { get; set; }
    }

    public class OpenLibraryWorkSeries
    {
        [JsonPropertyName("series")]
        public OpenLibraryKeyRef Series { get; set; }

        [JsonPropertyName("position")]
        public string Position { get; set; }
    }

    public class OpenLibraryKeyRef
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }
    }

    public class OpenLibraryEditionsResponse
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("entries")]
        public List<OpenLibraryEditionResource> Entries { get; set; } = new ();
    }

    public class OpenLibraryEditionResource
    {
        /// <summary>Path form, e.g. "/books/OL7353617M".</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; }

        [JsonPropertyName("isbn_13")]
        public List<string> Isbn13 { get; set; }

        [JsonPropertyName("isbn_10")]
        public List<string> Isbn10 { get; set; }

        [JsonPropertyName("publishers")]
        public List<string> Publishers { get; set; }

        [JsonPropertyName("publish_date")]
        public string PublishDate { get; set; }

        [JsonPropertyName("number_of_pages")]
        public int? NumberOfPages { get; set; }

        [JsonPropertyName("covers")]
        public List<int> Covers { get; set; }

        [JsonPropertyName("physical_format")]
        public string PhysicalFormat { get; set; }

        [JsonPropertyName("languages")]
        public List<OpenLibraryKeyRef> Languages { get; set; }

        [JsonPropertyName("works")]
        public List<OpenLibraryKeyRef> Works { get; set; }

        [JsonPropertyName("description")]
        [JsonConverter(typeof(OpenLibraryTextConverter))]
        public string Description { get; set; }
    }
}
