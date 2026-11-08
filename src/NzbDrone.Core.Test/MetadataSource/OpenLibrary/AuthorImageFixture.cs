using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.MetadataSource.Wikidata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Open Library has a photo for under half of a typical library's authors. These drive
    /// GetAuthorInfo through mocked proxies to check the order the other sources are tried in and
    /// that each one is only trusted when it really names the author.
    /// </summary>
    [TestFixture]
    public class AuthorImageFixture : CoreTest<OpenLibraryProvider>
    {
        private const string AudiblePortrait = "https://images-na.ssl-images-amazon.com/images/S/amzn-author-media-prod/vo28n4k69rta6vidij2gl40f0m.jpg";
        private const string WikidataPortrait = "https://upload.wikimedia.org/wikipedia/commons/b/b8/Brian_Herbert.jpg";

        private OpenLibraryAuthorResource _author;

        [SetUp]
        public void Setup()
        {
            _author = new OpenLibraryAuthorResource { Key = "/authors/OL1194290A", Name = "Brian Herbert" };

            Mocker.GetMock<IOpenLibraryProxy>()
                .Setup(x => x.GetAuthor(It.IsAny<string>()))
                .Returns(() => _author);

            Mocker.GetMock<IOpenLibraryProxy>()
                .Setup(x => x.GetWorksByAuthor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns(new List<OpenLibrarySearchDoc>());

            Mocker.GetMock<IAudibleProxy>()
                .Setup(x => x.SearchByAuthor(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new List<AudibleProductResource>());

            Mocker.GetMock<IAudnexusProxy>()
                .Setup(x => x.GetAuthor(It.IsAny<string>()))
                .Returns((AudnexusAuthorResource)null);

            Mocker.GetMock<IWikidataProxy>()
                .Setup(x => x.GetImageUrl(It.IsAny<string>()))
                .Returns((string)null);
        }

        private void GivenAudibleCredits(params (string Asin, string Name)[] authors)
        {
            var product = new AudibleProductResource
            {
                Asin = "B0007S5CXE",
                Title = "Hunters of Dune",
                Authors = authors.Select(x => new AudibleContributor { Asin = x.Asin, Name = x.Name }).ToList()
            };

            Mocker.GetMock<IAudibleProxy>()
                .Setup(x => x.SearchByAuthor(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new List<AudibleProductResource> { product });
        }

        private void GivenAudnexusAuthor(string asin, string name, string image)
        {
            Mocker.GetMock<IAudnexusProxy>()
                .Setup(x => x.GetAuthor(asin))
                .Returns(new AudnexusAuthorResource { Asin = asin, Name = name, Image = image });
        }

        private void GivenWikidataLink(string entityId, string image)
        {
            _author.RemoteIds = new Dictionary<string, string> { { "wikidata", entityId }, { "viaf", "66590377" } };

            Mocker.GetMock<IWikidataProxy>()
                .Setup(x => x.GetImageUrl(entityId))
                .Returns(image);
        }

        private List<MediaCover.MediaCover> Images()
        {
            return Subject.GetAuthorInfo("OL1194290A").Metadata.Value.Images;
        }

        [Test]
        public void should_keep_the_open_library_photo_and_ask_nobody_else()
        {
            _author.Photos = new List<int> { -1, 14852808 };
            GivenAudibleCredits(("B0028DEF0S", "Brian Herbert"));
            GivenAudnexusAuthor("B0028DEF0S", "Brian Herbert", AudiblePortrait);
            GivenWikidataLink("Q711137", WikidataPortrait);

            var images = Images();

            images.Should().ContainSingle();
            images[0].CoverType.Should().Be(MediaCoverTypes.Poster);
            images[0].Url.Should().Be("https://covers.openlibrary.org/a/id/14852808-L.jpg");

            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetAuthor(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IWikidataProxy>().Verify(x => x.GetImageUrl(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_use_the_audible_portrait_when_open_library_has_no_photo()
        {
            GivenAudibleCredits(("B0028DEF0S", "Brian Herbert"), ("B000AQ0072", "Kevin J. Anderson"));
            GivenAudnexusAuthor("B0028DEF0S", "Brian Herbert", AudiblePortrait);
            GivenWikidataLink("Q711137", WikidataPortrait);

            var images = Images();

            images.Should().ContainSingle();
            images[0].CoverType.Should().Be(MediaCoverTypes.Poster);
            images[0].Url.Should().Be(AudiblePortrait);
            images[0].Extension.Should().Be(".jpg");

            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetAuthor("B000AQ0072"), Times.Never());
            Mocker.GetMock<IWikidataProxy>().Verify(x => x.GetImageUrl(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_fall_back_to_wikidata_when_the_audible_author_page_has_no_portrait()
        {
            GivenAudibleCredits(("B0028DEF0S", "Brian Herbert"));
            GivenAudnexusAuthor("B0028DEF0S", "Brian Herbert", string.Empty);
            GivenWikidataLink("Q711137", WikidataPortrait);

            var images = Images();

            images.Should().ContainSingle();
            images[0].Url.Should().Be(WikidataPortrait);
        }

        [Test]
        public void should_not_trust_an_audnexus_record_that_names_somebody_else()
        {
            // Audnexus answers 200 with a record for almost any ASIN.
            GivenAudibleCredits(("B0028DEF0S", "Brian Herbert"));
            GivenAudnexusAuthor("B0028DEF0S", "TEA TREE THERAPY TEA TREE ANTISEPTIC CREAM, 4 OZ, EA-1", "https://example.com/cream.jpg");
            GivenWikidataLink("Q711137", WikidataPortrait);

            var images = Images();

            images.Should().ContainSingle();
            images[0].Url.Should().Be(WikidataPortrait);
        }

        [Test]
        public void should_use_wikidata_when_audible_has_never_heard_of_the_author()
        {
            GivenWikidataLink("Q711137", WikidataPortrait);

            var images = Images();

            images.Should().ContainSingle();
            images[0].Url.Should().Be(WikidataPortrait);
            images[0].Extension.Should().Be(".jpg");

            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetAuthor(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_ask_wikidata_without_a_link_from_open_library()
        {
            _author.RemoteIds = new Dictionary<string, string> { { "viaf", "66590377" } };

            Images().Should().BeEmpty();

            Mocker.GetMock<IWikidataProxy>().Verify(x => x.GetImageUrl(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_have_no_image_when_no_source_has_one()
        {
            GivenAudibleCredits(("B0028DEF0S", "Brian Herbert"));
            GivenWikidataLink("Q711137", null);

            Images().Should().BeEmpty();
        }

        [Test]
        public void should_ignore_a_co_author_that_is_not_the_author()
        {
            GivenAudibleCredits(("B000AQ0072", "Kevin J. Anderson"));
            GivenAudnexusAuthor("B000AQ0072", "Kevin J. Anderson", "https://example.com/anderson.jpg");

            Images().Should().BeEmpty();

            Mocker.GetMock<IAudnexusProxy>().Verify(x => x.GetAuthor(It.IsAny<string>()), Times.Never());
        }
    }
}
