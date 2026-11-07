using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class UpgradeDiskSpecification : IDecisionEngineSpecification
    {
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly Logger _logger;

        public UpgradeDiskSpecification(UpgradableSpecification qualityUpgradableSpecification,
                                        ICacheManager cacheManager,
                                        ICustomFormatCalculationService formatService,
                                        Logger logger)
        {
            _upgradableSpecification = qualityUpgradableSpecification;
            _formatService = formatService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            // Only files of the same media type compete: an audiobook release is never held back
            // by an existing epub, nor an ebook by an existing m4b.
            var mediaType = subject.ParsedBookInfo.Quality.Quality.MediaType();

            foreach (var file in subject.Books.SelectMany(c => c.BookFiles.Value.OfMediaType(mediaType)))
            {
                var customFormats = _formatService.ParseCustomFormat(file);

                if (!_upgradableSpecification.IsUpgradable(subject.Author.QualityProfile,
                                                           file.Quality,
                                                           customFormats,
                                                           subject.ParsedBookInfo.Quality,
                                                           subject.CustomFormats))
                {
                    return Decision.Reject("Existing files on disk is of equal or higher preference: {0}", file.Quality.Quality.Name);
                }
            }

            return Decision.Accept();
        }
    }
}
