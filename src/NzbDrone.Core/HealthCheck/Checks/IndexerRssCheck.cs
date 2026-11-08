using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(ProviderAddedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderUpdatedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderDeletedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderStatusChangedEvent<IIndexer>))]
    public class IndexerRssCheck : HealthCheckBase
    {
        private readonly IIndexerFactory _indexerFactory;

        public IndexerRssCheck(IIndexerFactory indexerFactory, ILocalizationService localizationService)
            : base(localizationService)
        {
            _indexerFactory = indexerFactory;
        }

        public override HealthCheck Check()
        {
            var enabled = _indexerFactory.RssEnabled(false);

            if (enabled.Empty())
            {
                // Soulseek has no RSS, so the slskd indexer cannot have it enabled. New releases
                // are still grabbed automatically there - by the scheduled Missing and Cutoff
                // Unmet searches - so an indexer that lacks RSS but is enabled for automatic
                // search satisfies this check rather than failing it.
                var searchOnly = SearchOnly(_indexerFactory.AutomaticSearchEnabled(false));

                if (searchOnly.Empty())
                {
                    return new HealthCheck(GetType(), HealthCheckResult.Error, _localizationService.GetLocalizedString("IndexerRssHealthCheckNoIndexers"), "#no-indexers-available-with-rss-sync-enabled-readarr-will-not-grab-new-releases-automatically");
                }

                if (SearchOnly(_indexerFactory.AutomaticSearchEnabled(true)).Empty())
                {
                    return new HealthCheck(GetType(), HealthCheckResult.Warning, _localizationService.GetLocalizedString("IndexerRssHealthCheckNoAvailableIndexers"), "#indexers-are-unavailable-due-to-failures");
                }

                return new HealthCheck(GetType());
            }

            var active = _indexerFactory.RssEnabled(true);

            if (active.Empty())
            {
                return new HealthCheck(GetType(), HealthCheckResult.Warning, _localizationService.GetLocalizedString("IndexerRssHealthCheckNoAvailableIndexers"), "#indexers-are-unavailable-due-to-failures");
            }

            return new HealthCheck(GetType());
        }

        private static List<IIndexer> SearchOnly(IEnumerable<IIndexer> indexers)
        {
            return indexers.Where(i => !i.SupportsRss && i.SupportsSearch).ToList();
        }
    }
}
