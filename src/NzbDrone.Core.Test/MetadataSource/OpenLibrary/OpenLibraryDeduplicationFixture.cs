using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Open Library's bibliographies carry duplicate work records and separate records for
    /// collections. These drive GetAuthorInfo through mocked proxies so the filtering is exercised
    /// without touching the network.
    /// </summary>
    [TestFixture]
    public class OpenLibraryDeduplicationFixture : CoreTest<OpenLibraryProvider>
    {
        private static OpenLibrarySearchDoc Work(string key, string title, int editions = 1, int ratings = 0)
        {
            return new OpenLibrarySearchDoc
            {
                Key = "/works/" + key,
                Title = title,
                EditionKeys = Enumerable.Range(0, editions).Select(i => "OL" + i + "M").ToList(),
                RatingsCount = ratings
            };
        }

        private List<string> TitlesFor(params OpenLibrarySearchDoc[] works)
        {
            Mocker.GetMock<IOpenLibraryProxy>()
                .Setup(x => x.GetAuthor(It.IsAny<string>()))
                .Returns(new OpenLibraryAuthorResource { Key = "/authors/OL1A", Name = "Frank Herbert" });

            Mocker.GetMock<IOpenLibraryProxy>()
                .Setup(x => x.GetWorksByAuthor(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(works.ToList());

            Mocker.GetMock<IAudibleProxy>()
                .Setup(x => x.SearchByAuthor(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new List<AudibleProductResource>());

            return Subject.GetAuthorInfo("OL1A").Books.Value.Select(x => x.Title).ToList();
        }

        [Test]
        public void should_drop_an_omnibus_without_taking_the_books_it_names()
        {
            // The order of the two filters matters: matching contained titles first would collapse
            // Dune Messiah and Children of Dune into the collection that lists them.
            var titles = TitlesFor(
                Work("W1", "Dune Messiah"),
                Work("W2", "Children of Dune"),
                Work("W3", "Dune, Dune Messiah, Children of Dune", editions: 40));

            titles.Should().BeEquivalentTo("Dune Messiah", "Children of Dune");
        }

        [Test]
        public void should_collapse_the_same_book_recorded_under_two_titles()
        {
            var titles = TitlesFor(
                Work("W1", "The Butlerian Jihad", editions: 3),
                Work("W2", "Dune: The Butlerian Jihad", editions: 12));

            titles.Should().HaveCount(1);
        }

        [Test]
        public void should_keep_the_richer_record_when_collapsing()
        {
            var titles = TitlesFor(
                Work("W1", "The Butlerian Jihad", editions: 3),
                Work("W2", "Dune: The Butlerian Jihad", editions: 12));

            // Richer means more editions - that record is likelier to match an indexer and to carry
            // usable covers and dates.
            titles.Single().Should().Be("Dune: The Butlerian Jihad");
        }

        [Test]
        public void should_not_treat_a_one_word_title_as_contained_in_a_longer_one()
        {
            // "Dune" occurs inside "Dune Messiah"; they are different books.
            var titles = TitlesFor(
                Work("W1", "Dune"),
                Work("W2", "Dune Messiah"),
                Work("W3", "Children of Dune"));

            titles.Should().BeEquivalentTo("Dune", "Dune Messiah", "Children of Dune");
        }

        [Test]
        public void should_not_let_a_short_series_title_swallow_the_series()
        {
            // Open Library carries a work called simply "Harry Potter". Every "Harry Potter and
            // the ..." contains it, and a naive containment check collapsed seven books into one.
            var titles = TitlesFor(
                Work("W0", "Harry Potter", editions: 40),
                Work("W1", "Harry Potter and the Philosopher's Stone", editions: 30),
                Work("W2", "Harry Potter and the Chamber of Secrets", editions: 25),
                Work("W3", "Harry Potter and the Goblet of Fire", editions: 20));

            titles.Should().HaveCount(4);
            titles.Should().Contain("Harry Potter and the Goblet of Fire");
        }

        [Test]
        public void should_still_collapse_a_subtitled_variant_of_the_same_book()
        {
            // The shorter title accounts for most of the longer one here, unlike the series case.
            var titles = TitlesFor(
                Work("W1", "The Butlerian Jihad", editions: 3),
                Work("W2", "Dune: The Butlerian Jihad", editions: 12));

            titles.Should().HaveCount(1);
        }

        [Test]
        public void should_collapse_exact_duplicates()
        {
            var titles = TitlesFor(
                Work("W1", "Dune", editions: 3),
                Work("W2", "Dune", editions: 10));

            titles.Should().HaveCount(1);
        }
    }
}
