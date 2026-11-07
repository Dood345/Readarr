using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Audible
{
    [TestFixture]
    public class AudibleTitleMatcherFixture : TestBase
    {
        private static AudibleProductResource Product(string title)
        {
            return new AudibleProductResource { Asin = "B" + title.GetHashCode().ToString("X8"), Title = title };
        }

        private static List<string> Titles(AudibleTitleMatcher matcher, string work)
        {
            return matcher.For(work).Select(x => x.Title).ToList();
        }

        [Test]
        public void should_match_on_the_whole_title_and_not_on_the_series_name()
        {
            // Regression: both sides were keyed on the title before the colon, so every
            // "Dune: Something" product reduced to "dune" and the Battle of Corrin audiobook
            // was filed under "Dune: Red Plague" while "Dune" collected the whole series.
            var products = new[] { Product("Dune"), Product("Dune Messiah"), Product("Dune: The Battle of Corrin") };
            var works = new[] { "Dune", "Dune Messiah", "Dune: Red Plague", "Dune: The Battle of Corrin" };

            var matcher = new AudibleTitleMatcher(products, works);

            Titles(matcher, "Dune").Should().BeEquivalentTo("Dune");
            Titles(matcher, "Dune Messiah").Should().BeEquivalentTo("Dune Messiah");
            Titles(matcher, "Dune: The Battle of Corrin").Should().BeEquivalentTo("Dune: The Battle of Corrin");
            Titles(matcher, "Dune: Red Plague").Should().BeEmpty();
        }

        [Test]
        public void should_match_a_segment_of_the_product_title_against_the_work()
        {
            var matcher = new AudibleTitleMatcher(
                new[] { Product("Dune: House Atreides: House Trilogy, Book 1") },
                new[] { "Dune", "House atreides" });

            Titles(matcher, "House atreides").Should().BeEquivalentTo("Dune: House Atreides: House Trilogy, Book 1");
            Titles(matcher, "Dune").Should().BeEmpty();
        }

        [Test]
        public void should_match_a_segment_of_the_work_title_against_the_product()
        {
            var matcher = new AudibleTitleMatcher(
                new[] { Product("The Butlerian Jihad") },
                new[] { "Dune: The Butlerian Jihad: Book One of the Legends of Dune Trilogy" });

            Titles(matcher, "Dune: The Butlerian Jihad: Book One of the Legends of Dune Trilogy").Should().HaveCount(1);
        }

        [Test]
        public void should_match_the_head_of_a_subtitled_product_against_a_multi_word_work()
        {
            var matcher = new AudibleTitleMatcher(
                new[] { Product("Dune Messiah: Book Two"), Product("Dune: Part One") },
                new[] { "Dune Messiah", "Dune" });

            Titles(matcher, "Dune Messiah").Should().BeEquivalentTo("Dune Messiah: Book Two");
            Titles(matcher, "Dune").Should().BeEmpty();
        }

        [Test]
        public void should_give_each_product_to_one_work_only()
        {
            var matcher = new AudibleTitleMatcher(
                new[] { Product("Dune: The Butlerian Jihad") },
                new[] { "The Butlerian Jihad", "Dune: The Butlerian Jihad", "Dune" });

            var owners = new[] { "The Butlerian Jihad", "Dune: The Butlerian Jihad", "Dune" }
                .Count(w => matcher.For(w).Any());

            owners.Should().Be(1);
            Titles(matcher, "Dune: The Butlerian Jihad").Should().HaveCount(1);
        }

        [Test]
        public void should_return_nothing_for_an_unknown_work()
        {
            var matcher = new AudibleTitleMatcher(new[] { Product("Dune") }, new[] { "Dune" });

            matcher.For("Children of Dune").Should().BeEmpty();
            matcher.For(null).Should().BeEmpty();
        }

        [TestCase("Dune: The Butlerian Jihad", false, "dune the butlerian jihad")]
        [TestCase("Dune: The Butlerian Jihad", true, "dune")]
        [TestCase("  DUNE -- messiah!!  ", false, "dune messiah")]
        [TestCase("", false, "")]
        public void normalizer_should_reduce_titles(string title, bool stripSubtitle, string expected)
        {
            TitleNormalizer.Normalize(title, stripSubtitle).Should().Be(expected);
        }

        [Test]
        public void normalizer_should_split_segments_and_strip_articles()
        {
            TitleNormalizer.Segments("Dune: House Atreides: House Trilogy, Book 1")
                .Should().Equal("dune", "house atreides", "house trilogy book 1");

            TitleNormalizer.StripLeadingArticle("the butlerian jihad").Should().Be("butlerian jihad");
            TitleNormalizer.StripLeadingArticle("an unexpected party").Should().Be("unexpected party");
            TitleNormalizer.StripLeadingArticle("theory of everything").Should().Be("theory of everything");
        }
    }
}
