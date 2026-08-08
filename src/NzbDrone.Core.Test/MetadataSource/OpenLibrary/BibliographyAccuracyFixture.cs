using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Scores the bibliography Open Library yields against a hand-curated list of the novels each
    /// author actually wrote.
    /// </summary>
    /// <remarks>
    /// This exists because the pre-existing fixtures could not have caught the defect it was written
    /// for. <see cref="OpenLibraryDeduplicationFixture"/> builds three or four synthetic works by
    /// hand, so it never sees real junk; <see cref="OpenLibraryProviderFixture"/> hits the live API
    /// but deliberately asserts nothing about quantity. Between them, Frank Herbert could return 104
    /// works for a 23-novel bibliography and every test stayed green.
    ///
    /// So this fixture asserts two numbers per author against committed payloads:
    ///
    /// - <b>Recall</b> - of the books Open Library can supply, how many survive the pipeline. A
    ///   filter that silently eats real books fails here.
    /// - <b>Precision</b> - what fraction of what we return is a real book. The 104-for-23 defect
    ///   fails here.
    ///
    /// Both matter and they pull against each other, which is the point: tightening a filter until
    /// precision looks good will trip the recall floor.
    ///
    /// The floors are deliberately below currently-measured values rather than pinned to them.
    /// Open Library's catalogue drifts, and a test that fails because someone fixed an upstream
    /// record is a test people learn to ignore.
    /// </remarks>
    [TestFixture]
    public class BibliographyAccuracyFixture : CoreTest<OpenLibraryProvider>
    {
        private static readonly JsonSerializerOptions Options = new ()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private static ExpectedBibliographies _expected;

        /// <summary>
        /// Each author's committed <c>search.json</c> payload, and the floors that author's data
        /// must clear. Recall is measured only against books Open Library actually carries, so the
        /// four Brian Herbert novels it lacks do not drag his floor down.
        /// </summary>
        private static IEnumerable<AuthorCase> Cases()
        {
            yield return new AuthorCase("OL79034A", "Frank Herbert", "frank_herbert_works.json", 1.00, 0.20);
            yield return new AuthorCase("OL2629960A", "Trudi Canavan", "trudi_canavan_works.json", 1.00, 0.20);
            yield return new AuthorCase("OL1194290A", "Brian Herbert", "brian_herbert_works.json", 0.70, 0.20);
        }

        [OneTimeSetUp]
        public void LoadExpectations()
        {
            _expected = JsonSerializer.Deserialize<ExpectedBibliographies>(
                ReadAllText(@"Files/MetadataSource/OpenLibrary/expected_bibliographies.json"), Options);
        }

        [Test]
        [TestCaseSource(nameof(Cases))]
        public void should_find_the_books_the_author_actually_wrote(AuthorCase testCase)
        {
            var result = Score(testCase);

            result.Recall.Should().BeGreaterOrEqualTo(
                testCase.MinRecall,
                "{0}: only {1} of {2} available books survived the pipeline. Missing: {3}",
                testCase.Name,
                result.Matched,
                result.Available,
                result.Missing.Any() ? string.Join(" | ", result.Missing) : "(none)");
        }

        [Test]
        [TestCaseSource(nameof(Cases))]
        public void should_not_bury_the_bibliography_in_non_books(AuthorCase testCase)
        {
            var result = Score(testCase);

            result.Precision.Should().BeGreaterOrEqualTo(
                testCase.MinPrecision,
                "{0}: returned {1} works for a {2}-novel bibliography. " +
                "Open Library files calendars, colouring books, magazine issues, box sets, publisher " +
                "SKUs and translations as works; something upstream of here has stopped rejecting them. " +
                "First 10 unrecognised: {3}",
                testCase.Name,
                result.Returned,
                result.Available,
                string.Join(" | ", result.Unrecognised.Take(10)));
        }

        /// <summary>
        /// The sharpest assertion here, and the one worth keeping green: nothing Open Library can
        /// supply may be lost to our own filtering.
        /// </summary>
        /// <remarks>
        /// <c>Missing</c> is computed against the books Open Library actually carries - the four
        /// Brian Herbert novels it lacks are excluded before this runs - so any entry is a book we
        /// dropped ourselves. That distinction is the whole reason the corpus records
        /// <c>unavailable</c> separately: it keeps upstream's gaps from being mistaken for our bugs,
        /// and stops us claiming credit for filtering that is really just absent data.
        /// </remarks>
        [Test]
        [TestCaseSource(nameof(Cases))]
        public void should_not_lose_any_book_open_library_carries(AuthorCase testCase)
        {
            var result = Score(testCase);

            result.Missing.Should().BeEmpty(
                "{0}: Open Library carries these but the pipeline dropped them",
                testCase.Name);
        }

        /// <summary>
        /// Not an assertion - prints the scores so a run shows where accuracy actually stands, and
        /// so the floors above can be re-based deliberately rather than guessed at.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(Cases))]
        public void report_accuracy(AuthorCase testCase)
        {
            var result = Score(testCase);

            TestContext.Out.WriteLine(
                $"{testCase.Name,-16} returned {result.Returned,4}   found {result.Matched,3}/{result.Available,-3}" +
                $"   recall {result.Recall,6:P0}   precision {result.Precision,6:P0}");

            foreach (var junk in result.Unrecognised)
            {
                TestContext.Out.WriteLine($"    not a novel: {junk}");
            }
        }

        private ScoreResult Score(AuthorCase testCase)
        {
            var author = _expected.Authors.Single(x => x.ForeignAuthorId == testCase.ForeignAuthorId);
            var titles = GetBibliography(testCase, author.Name);

            // Recall is measured against what Open Library can actually supply. Holding the pipeline
            // responsible for records that do not exist would make the number meaningless.
            var available = author.Titles.Where(x => !author.Unavailable.Contains(x)).ToList();
            var matched = available.Where(x => titles.Any(t => Matches(t, author.TitleFor(x)))).ToList();
            var recognised = titles.Where(t => author.Titles.Any(x => Matches(t, author.TitleFor(x)))).ToList();

            return new ScoreResult
            {
                Returned = titles.Count,
                Available = available.Count,
                Matched = matched.Count,
                Recall = available.Count == 0 ? 1.0 : (double)matched.Count / available.Count,
                Precision = titles.Count == 0 ? 0.0 : (double)recognised.Count / titles.Count,
                Missing = available.Except(matched).ToList(),
                Unrecognised = titles.Except(recognised).ToList()
            };
        }

        private List<string> GetBibliography(AuthorCase testCase, string name)
        {
            var payload = JsonSerializer.Deserialize<OpenLibrarySearchResponse>(
                ReadAllText($@"Files/MetadataSource/OpenLibrary/{testCase.Payload}"), Options);

            Mocker.GetMock<IOpenLibraryProxy>()
                .Setup(x => x.GetAuthor(It.IsAny<string>()))
                .Returns(new OpenLibraryAuthorResource
                {
                    Key = "/authors/" + testCase.ForeignAuthorId,
                    Name = name
                });

            Mocker.GetMock<IOpenLibraryProxy>()
                .Setup(x => x.GetWorksByAuthor(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(payload.Docs);

            // Audible enrichment is best-effort and irrelevant to which works make the bibliography.
            Mocker.GetMock<IAudibleProxy>()
                .Setup(x => x.SearchByAuthor(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new List<AudibleProductResource>());

            return Subject.GetAuthorInfo(testCase.ForeignAuthorId)
                .Books.Value
                .Select(x => x.Title)
                .ToList();
        }

        /// <summary>
        /// Titles match when they normalise equal, when one is a whole-word prefix of the other, or
        /// when dropping a leading series prefix from one makes them equal.
        /// </summary>
        /// <remarks>
        /// Three shapes have to be absorbed, all of them Open Library's own inconsistency rather than
        /// anything the pipeline did:
        ///
        /// - subtitles it omits - "Dreamer of Dune" for "Dreamer of Dune: The Biography of ...";
        /// - series prefixes it adds - "Dune House Corrino" for "House Corrino";
        /// - series prefixes it drops - "The Butlerian Jihad" for "Dune: The Butlerian Jihad".
        ///
        /// Every rule requires the shorter side to keep at least two words, which is what stops
        /// "Dune" claiming "Dune Messiah" and "Hunters of Dune" claiming "Sandworms of Dune". A
        /// trimmed string is only ever compared against the other side in full, never against
        /// another trimmed string, so two titles cannot meet in the middle on a shared tail.
        /// </remarks>
        private static bool Matches(string left, string right)
        {
            var l = Normalize(left);
            var r = Normalize(right);

            if (l.Length == 0 || r.Length == 0)
            {
                return false;
            }

            return l == r
                   || IsPrefix(l, r)
                   || IsPrefix(r, l)
                   || MatchesWithoutSeriesPrefix(l, r)
                   || MatchesWithoutSeriesPrefix(r, l);
        }

        private static bool IsPrefix(string longer, string shorter)
        {
            return WordCount(shorter) >= 2 && longer.StartsWith(shorter + " ", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// True when dropping one or two leading words from <paramref name="prefixed"/> yields
        /// <paramref name="bare"/> exactly. Two words covers "Dune The Duke of Caladan" against
        /// "The Duke of Caladan", where the article is normalised off the bare side.
        /// </summary>
        private static bool MatchesWithoutSeriesPrefix(string prefixed, string bare)
        {
            if (WordCount(bare) < 2)
            {
                return false;
            }

            var words = prefixed.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

            for (var drop = 1; drop <= 2 && drop < words.Length; drop++)
            {
                var remainder = string.Join(" ", words.Skip(drop));

                if (WordCount(remainder) >= 2 && Normalize(remainder) == bare)
                {
                    return true;
                }
            }

            return false;
        }

        private static int WordCount(string value)
        {
            return value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string Normalize(string title)
        {
            if (title == null)
            {
                return string.Empty;
            }

            var cleaned = new string(title.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
                .ToArray());

            var collapsed = string.Join(" ", cleaned.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));

            foreach (var article in new[] { "the ", "a ", "an " })
            {
                if (collapsed.StartsWith(article, System.StringComparison.Ordinal))
                {
                    return collapsed.Substring(article.Length);
                }
            }

            return collapsed;
        }

        public class AuthorCase
        {
            public AuthorCase(string foreignAuthorId, string name, string payload, double minRecall, double minPrecision)
            {
                ForeignAuthorId = foreignAuthorId;
                Name = name;
                Payload = payload;
                MinRecall = minRecall;
                MinPrecision = minPrecision;
            }

            public string ForeignAuthorId { get; }
            public string Name { get; }
            public string Payload { get; }
            public double MinRecall { get; }
            public double MinPrecision { get; }

            public override string ToString() => Name;
        }

        private class ScoreResult
        {
            public int Returned { get; set; }
            public int Available { get; set; }
            public int Matched { get; set; }
            public double Recall { get; set; }
            public double Precision { get; set; }
            public List<string> Missing { get; set; }
            public List<string> Unrecognised { get; set; }
        }

        private class ExpectedBibliographies
        {
            [JsonPropertyName("authors")]
            public List<ExpectedAuthor> Authors { get; set; } = new ();
        }

        private class ExpectedAuthor
        {
            [JsonPropertyName("foreignAuthorId")]
            public string ForeignAuthorId { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("titles")]
            public List<string> Titles { get; set; } = new ();

            /// <summary>Title Open Library stores, where it differs from the published title.</summary>
            [JsonPropertyName("aliases")]
            public Dictionary<string, string> Aliases { get; set; } = new ();

            [JsonPropertyName("unavailable")]
            public List<string> Unavailable { get; set; } = new ();

            public string TitleFor(string publishedTitle)
            {
                return Aliases.TryGetValue(publishedTitle, out var alias) ? alias : publishedTitle;
            }
        }
    }
}
