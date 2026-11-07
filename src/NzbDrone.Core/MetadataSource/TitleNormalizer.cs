using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Title comparison across metadata sources that punctuate, case and subtitle differently.
    /// Everything here works on a reduced form: lowercase letters and digits separated by single
    /// spaces, so "Dune: The Butlerian Jihad" and "DUNE - the butlerian jihad!" compare equal.
    /// </summary>
    public static class TitleNormalizer
    {
        private static readonly HashSet<string> LeadingArticles = new HashSet<string> { "the", "a", "an" };

        /// <summary>
        /// Reduces a title to letters, digits and single spaces. With <paramref name="stripSubtitle"/>
        /// anything after the first colon is dropped, which is right for "is this the same product"
        /// questions and wrong for deduplication, where the subtitle may be the whole title.
        /// </summary>
        public static string Normalize(string title, bool stripSubtitle)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var colon = title.IndexOf(':');
            var trimmed = stripSubtitle && colon > 0 ? title.Substring(0, colon) : title;

            var builder = new StringBuilder(trimmed.Length);
            var lastWasSpace = false;

            foreach (var c in trimmed)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                    lastWasSpace = false;
                }
                else if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// The colon-separated parts of a title, each normalised: "Dune: House Atreides: House
        /// Trilogy, Book 1" gives "dune", "house atreides", "house trilogy book 1". A title without
        /// a colon is a single segment.
        /// </summary>
        public static List<string> Segments(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<string>();
            }

            return title.Split(':')
                .Select(x => Normalize(x, stripSubtitle: false))
                .Where(x => x.IsNotNullOrWhiteSpace())
                .ToList();
        }

        /// <summary>
        /// "the butlerian jihad" and "butlerian jihad" are the same title.
        /// </summary>
        public static string StripLeadingArticle(string normalized)
        {
            if (normalized.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var space = normalized.IndexOf(' ');

            if (space > 0 && LeadingArticles.Contains(normalized.Substring(0, space)))
            {
                return normalized.Substring(space + 1);
            }

            return normalized;
        }

        public static int WordCount(string normalized)
        {
            return normalized.IsNullOrWhiteSpace() ? 0 : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
