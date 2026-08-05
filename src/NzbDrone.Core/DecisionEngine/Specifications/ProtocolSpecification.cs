using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class ProtocolSpecification : IDecisionEngineSpecification
    {
        private readonly IDelayProfileService _delayProfileService;
        private readonly Logger _logger;

        public ProtocolSpecification(IDelayProfileService delayProfileService,
                                     Logger logger)
        {
            _delayProfileService = delayProfileService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var delayProfile = _delayProfileService.BestForTags(subject.Author.Tags);

            var protocol = subject.Release.DownloadProtocol;

            if (!delayProfile.IsAllowedProtocol(protocol))
            {
                _logger.Debug("[{0}] {1} is not enabled for this author", subject.Release.Title, protocol);
                return Decision.Reject("{0} is not enabled for this author", protocol);
            }

            return Decision.Accept();
        }
    }
}
