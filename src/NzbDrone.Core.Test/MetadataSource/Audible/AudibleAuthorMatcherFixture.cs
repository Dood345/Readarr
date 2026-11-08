using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Audible
{
    [TestFixture]
    public class AudibleAuthorMatcherFixture : TestBase
    {
        private static readonly JsonSerializerOptions Options = new ()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private static AudibleProductResource Product(params (string Asin, string Name)[] authors)
        {
            var contributors = new List<AudibleContributor>();

            foreach (var (asin, name) in authors)
            {
                contributors.Add(new AudibleContributor { Asin = asin, Name = name });
            }

            return new AudibleProductResource { Asin = "B000000000", Title = "A Book", Authors = contributors };
        }

        [TestCase("Brian Herbert", "Brian Herbert")]
        [TestCase("Kevin J. Anderson", "Kevin J Anderson")]
        [TestCase("kevin j anderson", "Kevin J. Anderson")]
        [TestCase("Sophia Turner", "Sophia Ava Turner")]
        [TestCase("J. R. R. Tolkien", "J.R.R. Tolkien")]
        [TestCase("Gabriel García Márquez", "Gabriel Garcia Marquez")]
        [TestCase("Charles H. Kahn", "Charles Kahn")]
        public void should_match_the_same_person(string author, string candidate)
        {
            AudibleAuthorMatcher.NameMatches(author, candidate).Should().BeTrue();
        }

        [TestCase("Brian Herbert", "Frank Herbert")]
        [TestCase("Brian Herbert", "Kevin J. Anderson")]
        [TestCase("Brian Herbert", "Brian Herbert Chappel")]
        [TestCase("Sophia Ava Turner", "Sophia Turner")]
        [TestCase("Gareth Brown", "TEA TREE THERAPY TEA TREE ANTISEPTIC CREAM, 4 OZ, EA-1")]
        [TestCase("Brian Herbert", "")]
        [TestCase("Brian Herbert", null)]
        [TestCase("", "Brian Herbert")]
        public void should_not_match_a_different_person(string author, string candidate)
        {
            AudibleAuthorMatcher.NameMatches(author, candidate).Should().BeFalse();
        }

        [Test]
        public void should_pick_the_credited_author_by_name_not_by_count()
        {
            // Every Brian Herbert audiobook also credits Kevin J. Anderson, so the two ASINs tie on
            // frequency and only the name can decide.
            var products = new List<AudibleProductResource>
            {
                Product(("B0028DEF0S", "Brian Herbert"), ("B000AQ0072", "Kevin J. Anderson")),
                Product(("B0028DEF0S", "Brian Herbert"), ("B000AQ0072", "Kevin J. Anderson")),
                Product(("B000AQ0072", "Kevin J. Anderson"))
            };

            AudibleAuthorMatcher.FindAsin("Brian Herbert", products).Should().Be("B0028DEF0S");
            AudibleAuthorMatcher.FindAsin("Kevin J. Anderson", products).Should().Be("B000AQ0072");
        }

        [Test]
        public void should_prefer_the_most_frequent_asin_when_several_spellings_match()
        {
            var products = new List<AudibleProductResource>
            {
                Product(("B000000001", "Sophia Turner")),
                Product(("B000000002", "Sophia Ava Turner")),
                Product(("B000000002", "Sophia Ava Turner"))
            };

            AudibleAuthorMatcher.FindAsin("Sophia Turner", products).Should().Be("B000000002");
        }

        [Test]
        public void should_ignore_contributors_without_an_asin()
        {
            // Audible leaves the ASIN off some credits ("Dune: The Butlerian Jihad" credits both
            // authors by name only).
            var products = new List<AudibleProductResource>
            {
                Product((null, "Brian Herbert")),
                Product(("", "Brian Herbert"))
            };

            AudibleAuthorMatcher.FindAsin("Brian Herbert", products).Should().BeNull();
        }

        [Test]
        public void should_return_null_without_products_or_a_name()
        {
            AudibleAuthorMatcher.FindAsin("Brian Herbert", null).Should().BeNull();
            AudibleAuthorMatcher.FindAsin("Brian Herbert", new List<AudibleProductResource>()).Should().BeNull();
            AudibleAuthorMatcher.FindAsin(null, new List<AudibleProductResource> { Product(("B1", "Brian Herbert")) }).Should().BeNull();
            AudibleAuthorMatcher.FindAsin("Brian Herbert", new List<AudibleProductResource> { new AudibleProductResource() }).Should().BeNull();
        }

        [Test]
        public void should_find_brian_herbert_in_a_recorded_audible_author_search()
        {
            var response = JsonSerializer.Deserialize<AudibleSearchResponse>(
                ReadAllText(@"Files/MetadataSource/Audible/search_author_brian_herbert.json"), Options);

            AudibleAuthorMatcher.FindAsin("Brian Herbert", response.Products).Should().Be("B0028DEF0S");
            AudibleAuthorMatcher.FindAsin("Frank Herbert", response.Products).Should().NotBe("B0028DEF0S");
        }
    }
}
