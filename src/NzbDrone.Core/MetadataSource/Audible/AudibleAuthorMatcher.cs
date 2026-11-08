using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.Audible
{
    /// <summary>
    /// Picks the Audible contributor that is the Open Library author. An author search on Audible
    /// returns the co-authors too - every Brian Herbert product also credits Kevin J. Anderson, with
    /// the same count - so the name has to decide, not the frequency alone.
    /// </summary>
    public static class AudibleAuthorMatcher
    {
        /// <summary>
        /// The ASIN of the credited author whose name is <paramref name="authorName"/>, preferring
        /// the one credited most often when several spellings qualify. Null when no product credits
        /// a matching name with an ASIN.
        /// </summary>
        public static string FindAsin(string authorName, IEnumerable<AudibleProductResource> products)
        {
            if (authorName.IsNullOrWhiteSpace() || products == null)
            {
                return null;
            }

            var counts = new Dictionary<string, int>();

            foreach (var contributor in products.SelectMany(x => x.Authors ?? new List<AudibleContributor>()))
            {
                if (contributor.Asin.IsNullOrWhiteSpace() || !NameMatches(authorName, contributor.Name))
                {
                    continue;
                }

                counts.TryGetValue(contributor.Asin, out var count);
                counts[contributor.Asin] = count + 1;
            }

            return counts.OrderByDescending(x => x.Value).Select(x => x.Key).FirstOrDefault();
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> names the same person as <paramref name="authorName"/>.
        /// Punctuation, case and accents are ignored ("Kevin J. Anderson" is "Kevin J Anderson").
        /// A candidate may carry extra middle names ("Sophia Ava Turner" for "Sophia Turner") as long
        /// as the first and last names agree and every full word of the author's name is present;
        /// initials in the author's name are not required of the candidate.
        /// </summary>
        public static bool NameMatches(string authorName, string candidate)
        {
            var author = Tokens(authorName);
            var other = Tokens(candidate);

            if (author.Count == 0 || other.Count == 0)
            {
                return false;
            }

            if (author.SequenceEqual(other))
            {
                return true;
            }

            if (author.Count < 2 || other.Count < 2)
            {
                return false;
            }

            if (author[0] != other[0] || author[author.Count - 1] != other[other.Count - 1])
            {
                return false;
            }

            return author.Where(x => x.Length > 1).All(other.Contains);
        }

        private static List<string> Tokens(string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return new List<string>();
            }

            var builder = new StringBuilder(name.Length);

            foreach (var c in name.RemoveAccent())
            {
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }

            return builder.ToString()
                .Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
    }
}
