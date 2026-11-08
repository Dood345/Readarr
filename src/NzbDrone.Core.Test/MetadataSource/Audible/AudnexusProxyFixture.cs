using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audible
{
    [TestFixture]
    public class AudnexusProxyFixture : CoreTest<AudnexusProxy>
    {
        private void GivenResponse(string file, HttpStatusCode status = HttpStatusCode.OK)
        {
            var content = file == null ? string.Empty : ReadAllText($@"Files/MetadataSource/Audnexus/{file}");

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => new HttpResponse(request, new HttpHeader(), content, status));
        }

        [Test]
        public void should_read_the_portrait_from_a_recorded_author()
        {
            GivenResponse("author_B0028DEF0S_brian_herbert.json");

            var author = Subject.GetAuthor("B0028DEF0S");

            author.Asin.Should().Be("B0028DEF0S");
            author.Name.Should().Be("Brian Herbert");
            author.Image.Should().Be("https://images-na.ssl-images-amazon.com/images/S/amzn-author-media-prod/vo28n4k69rta6vidij2gl40f0m.jpg");
        }

        [Test]
        public void should_return_an_empty_image_when_the_author_page_has_none()
        {
            GivenResponse("author_B019AOV6U4_no_image.json");

            var author = Subject.GetAuthor("B019AOV6U4");

            author.Name.Should().Be("Sophia Ava Turner");
            author.Image.Should().BeEmpty();
        }

        [Test]
        public void should_hand_back_whatever_audnexus_says_an_asin_is()
        {
            // Audnexus answers 200 for almost any ASIN. Deciding whether the record is the author is
            // the caller's job, so the proxy must not drop it.
            GivenResponse("author_B000000000_not_an_author.json");

            var author = Subject.GetAuthor("B000000000");

            author.Name.Should().StartWith("TEA TREE");
            author.Image.Should().BeEmpty();
        }

        [Test]
        public void should_return_null_on_http_error()
        {
            GivenResponse(null, HttpStatusCode.NotFound);

            Subject.GetAuthor("B0ZZZZZZZZ").Should().BeNull();
        }

        [Test]
        public void should_return_null_when_the_request_throws()
        {
            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Throws(new WebException("unreachable"));

            Subject.GetAuthor("B0028DEF0S").Should().BeNull();
        }

        [Test]
        public void should_return_null_for_a_blank_asin()
        {
            Subject.GetAuthor(null).Should().BeNull();
            Subject.GetAuthor(" ").Should().BeNull();

            Mocker.GetMock<ICachedHttpResponseService>()
                .Verify(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()), Times.Never());
        }
    }
}
