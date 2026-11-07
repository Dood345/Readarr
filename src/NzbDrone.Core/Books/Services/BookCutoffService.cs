using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
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
        private readonly ICustomFormatCalculationService _formatCalculator;

        public BookCutoffService(IBookRepository bookRepository,
                                 IQualityProfileService qualityProfileService,
                                 ICustomFormatCalculationService formatCalculator)
        {
            _bookRepository = bookRepository;
            _qualityProfileService = qualityProfileService;
            _formatCalculator = formatCalculator;
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec)
        {
            var profiles = _qualityProfileService.All();
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();
            var belowQualityCutoff = new Dictionary<int, HashSet<int>>();
            var formatProfiles = new Dictionary<int, QualityProfile>();

            foreach (var profile in profiles)
            {
                var belowCutoff = QualitiesBelowCutoff(profile);
                belowQualityCutoff[profile.Id] = belowCutoff.ToHashSet();

                var candidates = belowCutoff.ToList();

                // A file at or above the quality cutoff can still be below the profile's format
                // cutoff. Scores live outside the database, so pull every allowed quality of the
                // cutoff's media type and judge the format cutoff in memory.
                if (HasFormatCutoff(profile))
                {
                    formatProfiles[profile.Id] = profile;

                    var mediaType = CutoffMediaType(profile);

                    candidates.AddRange(profile.Items
                        .Where(i => i.Allowed)
                        .SelectMany(i => i.GetQualities())
                        .Where(q => q.MediaType() == mediaType)
                        .Select(q => q.Id));
                }

                if (candidates.Any())
                {
                    qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, candidates.Distinct()));
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Book>();

                return pagingSpec;
            }

            if (formatProfiles.Empty())
            {
                return _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff);
            }

            var all = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = int.MaxValue,
                SortKey = pagingSpec.SortKey,
                SortDirection = pagingSpec.SortDirection,
                FilterExpressions = pagingSpec.FilterExpressions
            };

            var records = _bookRepository.BooksWhereCutoffUnmet(all, qualitiesBelowCutoff).Records
                .DistinctBy(b => b.Id)
                .Where(b => IsCutoffUnmet(b, belowQualityCutoff, formatProfiles))
                .ToList();

            pagingSpec.TotalRecords = records.Count;
            pagingSpec.Records = records
                .Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize)
                .Take(pagingSpec.PageSize)
                .ToList();

            return pagingSpec;
        }

        // The cutoff is resolved per media type, so an ebook is only "below cutoff" against the
        // text qualities and an audiobook against the audio ones; otherwise every epub in a profile
        // cut off at M4B would be wanted forever.
        private static List<int> QualitiesBelowCutoff(QualityProfile profile)
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

            return belowCutoff;
        }

        private static bool HasFormatCutoff(QualityProfile profile)
        {
            return profile.UpgradeAllowed &&
                   profile.CutoffFormatScore > profile.MinFormatScore &&
                   profile.FormatItems.Any(f => f.Score > 0);
        }

        /// <summary>
        /// The format cutoff is judged only for files of the media type the profile's cutoff
        /// quality belongs to. A bitrate format can never match an epub, so applying the cutoff
        /// score to both types would list every ebook of an audiobook-cutoff profile as unmet.
        /// </summary>
        private static BookMediaType CutoffMediaType(QualityProfile profile)
        {
            var cutoffItem = profile.Items.ElementAtOrDefault(profile.GetIndex(profile.Cutoff).Index);

            return cutoffItem?.GetQualities().FirstOrDefault()?.MediaType() ?? BookMediaType.Audiobook;
        }

        private bool IsCutoffUnmet(Book book, Dictionary<int, HashSet<int>> belowQualityCutoff, Dictionary<int, QualityProfile> formatProfiles)
        {
            var author = book.Author.Value;

            if (author == null)
            {
                return false;
            }

            var monitoredEditions = book.Editions.Value.Where(e => e.Monitored).Select(e => e.Id).ToHashSet();
            var files = book.BookFiles.Value.Where(f => monitoredEditions.Contains(f.EditionId)).ToList();

            if (belowQualityCutoff.TryGetValue(author.QualityProfileId, out var belowCutoff) &&
                files.Any(f => belowCutoff.Contains(f.Quality.Quality.Id)))
            {
                return true;
            }

            if (!formatProfiles.TryGetValue(author.QualityProfileId, out var profile))
            {
                return false;
            }

            var mediaType = CutoffMediaType(profile);

            return files
                .Where(f => f.MediaType() == mediaType)
                .Any(f => profile.CalculateCustomFormatScore(_formatCalculator.ParseCustomFormat(f, author)) < profile.CutoffFormatScore);
        }
    }
}
