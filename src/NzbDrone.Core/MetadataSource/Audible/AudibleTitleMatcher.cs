using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.Audible
{
    /// <summary>
    /// Decides which Open Library work each Audible product belongs to. Matching used to key both
    /// sides on the title before the first colon, which made every "Dune: Something" audiobook
    /// collapse to "dune": the Battle of Corrin audiobook landed on the "Dune: Red Plague" work and
    /// the work called "Dune" collected every product in the series as its editions.
    ///
    /// Each product is now assigned to at most one work, the one it matches best:
    /// the whole title first, then a colon segment against the other side's whole title
    /// ("Dune: House Atreides: House Trilogy, Book 1" against "House Atreides"), and only as a
    /// last resort the old head-of-title comparison.
    /// </summary>
    public class AudibleTitleMatcher
    {
        private const int WholeTitle = 4;
        private const int SegmentAgainstWhole = 3;
        private const int SegmentMatch = 2;
        private const int HeadOnly = 1;

        private readonly Dictionary<string, List<AudibleProductResource>> _assigned = new Dictionary<string, List<AudibleProductResource>>();

        public AudibleTitleMatcher(IEnumerable<AudibleProductResource> products, IEnumerable<string> workTitles)
        {
            var works = (workTitles ?? Enumerable.Empty<string>())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct()
                .Select(x => new TitleKeys(x))
                .Where(x => x.Whole.IsNotNullOrWhiteSpace())
                .ToList();

            foreach (var work in works)
            {
                _assigned[work.Title] = new List<AudibleProductResource>();
            }

            foreach (var product in products ?? Enumerable.Empty<AudibleProductResource>())
            {
                var keys = new TitleKeys(product.Title);

                if (keys.Whole.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var best = works
                    .Select(work => new { Work = work, Score = Score(keys, work) })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                if (best != null)
                {
                    _assigned[best.Work.Title].Add(product);
                }
            }
        }

        /// <summary>
        /// The products assigned to a work. Titles not given to the constructor get nothing, so a
        /// product can never be claimed by two works.
        /// </summary>
        public List<AudibleProductResource> For(string workTitle)
        {
            return workTitle != null && _assigned.TryGetValue(workTitle, out var products)
                ? products
                : new List<AudibleProductResource>();
        }

        private static int Score(TitleKeys product, TitleKeys work)
        {
            if (product.Whole == work.Whole)
            {
                return WholeTitle;
            }

            // "Dune: House Atreides: ..." names "House Atreides" in a segment, and the work
            // "Dune: The Butlerian Jihad: Book One..." names the product "The Butlerian Jihad".
            // The first segment of a multi-segment title is the series name and never counts: a
            // bare "dune" on either side is what used to mis-file the whole series.
            var productTail = product.Segments.Skip(1).ToList();
            var workTail = work.Segments.Skip(1).ToList();

            // A segment equal to the other side's whole title outranks two tails merely sharing a
            // segment, so "Dune: The Butlerian Jihad" goes to the work "The Butlerian Jihad" rather
            // than to "Dune: The Butlerian Jihad: Book One of the Legends of Dune Trilogy".
            if (productTail.Contains(work.Whole) || workTail.Contains(product.Whole))
            {
                return SegmentAgainstWhole;
            }

            if (productTail.Intersect(workTail).Any())
            {
                return SegmentMatch;
            }

            // Head of the product title against the whole work title - a product "Dune Messiah:
            // Book Two" against the work "Dune Messiah". Never the other way round, so a work that
            // is just "Dune" does not inherit every "Dune: X" product.
            if (product.Segments.Count > 1 && product.Segments[0] == work.Whole && work.Segments.Count == 1 && TitleNormalizer.WordCount(work.Whole) >= 2)
            {
                return HeadOnly;
            }

            return 0;
        }

        private sealed class TitleKeys
        {
            public TitleKeys(string title)
            {
                Title = title;
                Whole = TitleNormalizer.StripLeadingArticle(TitleNormalizer.Normalize(title, stripSubtitle: false));
                Segments = TitleNormalizer.Segments(title).Select(TitleNormalizer.StripLeadingArticle).ToList();
            }

            public string Title { get; }
            public string Whole { get; }
            public List<string> Segments { get; }
        }
    }
}
