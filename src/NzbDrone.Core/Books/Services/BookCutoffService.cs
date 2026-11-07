using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public interface IBookCutoffService
    {
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec);
    }

    public class BookCutoffService : IBookCutoffService
    {
        private readonly IBookRepository _bookRepository;
        private readonly IQualityProfileService _qualityProfileService;

        public BookCutoffService(IBookRepository bookRepository, IQualityProfileService qualityProfileService)
        {
            _bookRepository = bookRepository;
            _qualityProfileService = qualityProfileService;
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec)
        {
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();
            var profiles = _qualityProfileService.All();

            // Get all items less than the cutoff. The cutoff is resolved per media type, so an
            // ebook is only "below cutoff" against the text qualities and an audiobook against the
            // audio ones; otherwise every epub in a profile cut off at M4B would be wanted forever.
            foreach (var profile in profiles)
            {
                var belowCutoff = new List<int>();

                foreach (var mediaType in new[] { BookMediaType.Ebook, BookMediaType.Audiobook })
                {
                    var cutoffIndex = profile.GetIndex(profile.EffectiveCutoffFor(mediaType));

                    belowCutoff.AddRange(profile.Items
                        .Take(cutoffIndex.Index)
                        .SelectMany(i => i.GetQualities())
                        .Where(q => q.MediaType() == mediaType)
                        .Select(q => q.Id));
                }

                if (belowCutoff.Any())
                {
                    qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, belowCutoff.Distinct()));
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Book>();

                return pagingSpec;
            }

            return _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff);
        }
    }
}
