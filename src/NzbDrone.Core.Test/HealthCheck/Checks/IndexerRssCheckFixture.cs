using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class IndexerRssCheckFixture : CoreTest<IndexerRssCheck>
    {
        private Mock<IIndexer> _indexerMock;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.GetAvailableProviders())
                  .Returns(new List<IIndexer>());

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.RssEnabled(It.IsAny<bool>()))
                  .Returns(new List<IIndexer>());

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.AutomaticSearchEnabled(It.IsAny<bool>()))
                  .Returns(new List<IIndexer>());

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");
        }

        private void GivenIndexer(bool supportsRss, bool supportsSearch)
        {
            _indexerMock = Mocker.GetMock<IIndexer>();
            _indexerMock.SetupGet(s => s.SupportsRss).Returns(supportsRss);
            _indexerMock.SetupGet(s => s.SupportsSearch).Returns(supportsSearch);

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.GetAvailableProviders())
                  .Returns(new List<IIndexer> { _indexerMock.Object });
        }

        private void GivenRssEnabled()
        {
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.RssEnabled(It.IsAny<bool>()))
                  .Returns(new List<IIndexer> { _indexerMock.Object });
        }

        private void GivenAutomaticSearchEnabled(bool blocked = false)
        {
            // A blocked indexer is listed only when blocked ones are not filtered out.
            var indexers = new List<IIndexer> { _indexerMock.Object };

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.AutomaticSearchEnabled(false))
                  .Returns(indexers);

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.AutomaticSearchEnabled(true))
                  .Returns(blocked ? new List<IIndexer>() : indexers);
        }

        private void GivenRssFiltered()
        {
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.RssEnabled(false))
                  .Returns(new List<IIndexer> { _indexerMock.Object });

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("recent indexer errors");
        }

        [Test]
        public void should_return_error_when_no_indexer_present()
        {
            Subject.Check().ShouldBeError();
        }

        [Test]
        public void should_return_error_when_no_rss_supported_indexer_present()
        {
            GivenIndexer(false, true);

            Subject.Check().ShouldBeError();
        }

        [Test]
        public void should_return_ok_when_a_search_only_indexer_has_automatic_search_enabled()
        {
            // slskd: Soulseek has no RSS, the scheduled searches grab new releases instead.
            GivenIndexer(false, true);
            GivenAutomaticSearchEnabled();

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_filter_warning_when_the_search_only_indexer_is_blocked()
        {
            GivenIndexer(false, true);
            GivenAutomaticSearchEnabled(blocked: true);

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("recent indexer errors");

            Subject.Check().ShouldBeWarning("recent indexer errors");
        }

        [Test]
        public void should_return_error_when_only_an_rss_capable_indexer_has_automatic_search()
        {
            // RSS is supported but switched off: that is still a misconfiguration.
            GivenIndexer(true, true);
            GivenAutomaticSearchEnabled();

            Subject.Check().ShouldBeError();
        }

        [Test]
        public void should_return_ok_when_rss_is_enabled()
        {
            GivenIndexer(true, false);
            GivenRssEnabled();

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_error_if_rss_is_supported_but_disabled()
        {
            GivenIndexer(true, false);

            Subject.Check().ShouldBeError();
        }

        [Test]
        public void should_return_filter_warning_if_rss_is_enabled_but_filtered()
        {
            GivenIndexer(true, false);
            GivenRssFiltered();

            Subject.Check().ShouldBeWarning("recent indexer errors");
        }
    }
}
