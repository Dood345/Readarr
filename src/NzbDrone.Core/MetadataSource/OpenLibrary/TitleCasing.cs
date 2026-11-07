using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Open Library work titles are user-entered and often arrive as "House atreides" or
    /// "Easter parade". Those titles name folders and files on disk, so the lowercase words are
    /// capitalised, leaving small words alone and never touching a word that already has a
    /// capital in it ("iPhone", "DUNE").
    /// </summary>
    public static class TitleCasing
    {
        private static readonly HashSet<string> SmallWords = new HashSet<string>
        {
            "a", "an", "the", "and", "but", "or", "nor", "for", "so", "yet",
            "at", "by", "in", "of", "on", "to", "up", "as", "if", "vs", "via", "from", "with", "into", "onto", "per"
        };

        public static string Normalize(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return title;
            }

            // Typographic apostrophes and quotes become the plain ones. Titles name files, and the
            // same book arrives as "Carl's" from one source and "Carl’s" from another, which would
            // otherwise have the renamer shuffling folders between the two spellings.
            title = title.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');

            var words = title.Split(' ');
            var changed = false;

            for (var i = 0; i < words.Length; i++)
            {
                var word = words[i];
                var letters = word.Where(char.IsLetter).ToList();

                if (!letters.Any())
                {
                    continue;
                }

                // "1950S" and "3E" are decade and edition markers, not initialisms.
                if (letters.Count == 1 && char.IsUpper(letters[0]) && char.IsDigit(word[0]) && word.EndsWith("S"))
                {
                    words[i] = word.Substring(0, word.Length - 1) + "s";
                    changed = true;
                    continue;
                }

                // "Hunters Of Dune": a capitalised small word in the middle of a title is lowered,
                // unless it opens a clause. Only a word that is nothing but a capital and lowercase
                // letters, so "OF" in an all-caps title is left alone.
                if (i > 0 && SmallWords.Contains(word.ToLowerInvariant()) && !EndsClause(words[i - 1]) &&
                    char.IsUpper(word[0]) && word.Skip(1).All(char.IsLower))
                {
                    words[i] = word.ToLowerInvariant();
                    changed = true;
                    continue;
                }

                // Only a word that opens with a letter is capitalised: "1950s", "20th" and "3rd"
                // would otherwise come out as "1950S", "20Th" and "3Rd".
                if (letters.Any(char.IsUpper) || !char.IsLetter(word.First(c => char.IsLetterOrDigit(c))))
                {
                    continue;
                }

                // Small words stay lowercase unless they open the title or follow a colon or dash.
                if (i > 0 && SmallWords.Contains(word) && !EndsClause(words[i - 1]))
                {
                    continue;
                }

                words[i] = Capitalize(word);
                changed = true;
            }

            return changed ? string.Join(" ", words) : title;
        }

        /// <summary>
        /// Plain apostrophes and quotes only; applied to edition titles too, so an edition and its
        /// work never differ by typography alone.
        /// </summary>
        public static string PlainQuotes(string title)
        {
            return title?.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
        }

        private static bool EndsClause(string previous)
        {
            return previous.EndsWith(":") || previous.EndsWith("-") || previous.EndsWith("–") || previous.EndsWith("—");
        }

        private static string Capitalize(string word)
        {
            var chars = word.ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsLetter(chars[i]))
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    break;
                }
            }

            return new string(chars);
        }
    }
}
