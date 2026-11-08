using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Wikidata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Wikidata
{
    [TestFixture]
    public class WikidataProxyFixture : CoreTest<WikidataProxy>
    {
        private void GivenResponse(string file, HttpStatusCode status = HttpStatusCode.OK)
        {
            var content = file == null ? string.Empty : ReadAllText($@"Files/MetadataSource/Wikidata/{file}");

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => new HttpResponse(request, new HttpHeader(), content, status));
        }

        [Test]
        public void should_resolve_the_image_claim_to_a_commons_file_url()
        {
            GivenResponse("claims_Q711137_brian_herbert.json");

            // Where Special:FilePath/Brian%20Herbert.jpg redirects to.
            Subject.GetImageUrl("Q711137").Should().Be("https://upload.wikimedia.org/wikipedia/commons/b/b8/Brian_Herbert.jpg");
        }

        [Test]
        public void should_return_null_when_the_entity_has_no_image()
        {
            GivenResponse("claims_Q451379_no_image.json");

            Subject.GetImageUrl("Q451379").Should().BeNull();
        }

        [Test]
        public void should_return_null_on_http_error()
        {
            GivenResponse(null, HttpStatusCode.ServiceUnavailable);

            Subject.GetImageUrl("Q711137").Should().BeNull();
        }

        [Test]
        public void should_return_null_when_the_request_throws()
        {
            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Throws(new WebException("unreachable"));

            Subject.GetImageUrl("Q711137").Should().BeNull();
        }

        [Test]
        public void should_return_null_for_a_blank_entity()
        {
            Subject.GetImageUrl(null).Should().BeNull();
            Subject.GetImageUrl(string.Empty).Should().BeNull();

            Mocker.GetMock<ICachedHttpResponseService>()
                .Verify(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [TestCase("Brian Herbert.jpg", "https://upload.wikimedia.org/wikipedia/commons/b/b8/Brian_Herbert.jpg")]
        [TestCase("Brian_Herbert.jpg", "https://upload.wikimedia.org/wikipedia/commons/b/b8/Brian_Herbert.jpg")]
        [TestCase("brian Herbert.jpg", "https://upload.wikimedia.org/wikipedia/commons/b/b8/Brian_Herbert.jpg")]
        [TestCase("Lev grossman 2011.jpg", "https://upload.wikimedia.org/wikipedia/commons/e/eb/Lev_grossman_2011.jpg")]
        [TestCase("Apostolos Doxiadis Athens 2010.jpg", "https://upload.wikimedia.org/wikipedia/commons/4/4a/Apostolos_Doxiadis_Athens_2010.jpg")]
        public void should_build_the_hashed_commons_path(string fileName, string expected)
        {
            WikidataProxy.CommonsFileUrl(fileName).Should().Be(expected);
        }

        [TestCase("Author signature.svg")]
        [TestCase("Portrait.tif")]
        [TestCase("Portrait.pdf")]
        [TestCase("Portrait")]
        [TestCase("")]
        [TestCase(null)]
        public void should_skip_files_the_cover_resizer_cannot_use(string fileName)
        {
            WikidataProxy.CommonsFileUrl(fileName).Should().BeNull();
        }

        [Test]
        public void should_percent_encode_the_file_name()
        {
            var url = WikidataProxy.CommonsFileUrl("Gabriel García Márquez (2009).jpg");

            url.Should().StartWith("https://upload.wikimedia.org/wikipedia/commons/");
            url.Should().EndWith("/Gabriel_Garc%C3%ADa_M%C3%A1rquez_%282009%29.jpg");
        }
    }
}
