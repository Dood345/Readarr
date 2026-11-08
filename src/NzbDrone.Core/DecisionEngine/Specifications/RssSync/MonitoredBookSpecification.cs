using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications.RssSync
{
    public class MonitoredBookSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MonitoredBookSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (searchCriteria != null)
            {
                if (!searchCriteria.MonitoredBooksOnly)
                {
                    _logger.Debug("Skipping monitored check during search");
                    return Decision.Accept();
                }
            }

            if (!subject.Author.Monitored)
            {
                _logger.Debug("{0} is present in the DB but not tracked. Rejecting.", subject.Author);
                return Decision.Reject("Author is not monitored");
            }

            var monitoredCount = subject.Books.Count(book => book.Monitored);
            if (monitoredCount == subject.Books.Count)
            {
                return IsEditionMonitored(subject);
            }

            if (subject.Books.Count == 1)
            {
                _logger.Debug("Book is not monitored. Rejecting", monitoredCount, subject.Books.Count);
                return Decision.Reject("Book is not monitored");
            }

            if (monitoredCount == 0)
            {
                _logger.Debug("No books in the release are monitored. Rejecting", monitoredCount, subject.Books.Count);
            }
            else
            {
                _logger.Debug("Only {0}/{1} books in the release are monitored. Rejecting", monitoredCount, subject.Books.Count);
            }

            return Decision.Reject("Book is not monitored");
        }

        /// <summary>
        /// A book monitors one edition per media type. A release of a type whose editions are all
        /// unmonitored is not wanted: an epub for a book the user only follows as an audiobook, or
        /// the other way round. A book with no edition of that type at all is left alone, as is a
        /// release whose quality says nothing about its type.
        /// </summary>
        private Decision IsEditionMonitored(RemoteBook subject)
        {
            var quality = subject.ParsedBookInfo?.Quality?.Quality;

            if (quality == null || quality == Quality.Unknown)
            {
                return Decision.Accept();
            }

            var mediaType = quality.MediaType();

            foreach (var book in subject.Books)
            {
                var editions = book.Editions?.Value?.Where(e => e.MediaType() == mediaType).ToList();

                if (editions != null && editions.Any() && editions.All(e => !e.Monitored))
                {
                    _logger.Debug("No {0} edition of {1} is monitored. Rejecting", mediaType, book);
                    return Decision.Reject("No {0} edition of the book is monitored", mediaType.ToString().ToLowerInvariant());
                }
            }

            return Decision.Accept();
        }
    }
}
