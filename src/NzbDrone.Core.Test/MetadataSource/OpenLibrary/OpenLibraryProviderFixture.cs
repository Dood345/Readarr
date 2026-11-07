using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Hits Open Library and Audible for real, like BookInfoProxyFixture did against bookinfo.club.
    /// Assertions are deliberately structural - upstream catalogue data drifts, so exact counts and
    /// orderings beyond the ranking guarantee are not asserted.
    /// </summary>
    [TestFixture]
    [IntegrationTest]
    public class OpenLibraryProviderFixture : CoreTest<OpenLibraryProvider>
    {
        private const string FrankHerbert = "OL79034A";

        [SetUp]
        public void Setup()
        {
            UseRealHttp();

            Mocker.SetConstant<IOpenLibraryProxy>(Mocker.Resolve<OpenLibraryProxy>());
            Mocker.SetConstant<IAudibleProxy>(Mocker.Resolve<AudibleProxy>());
        }

        [TestCase("OL79034A", "Frank Herbert")]
        [TestCase("OL34221A", "Isaac Asimov")]
        [TestCase("OL23919A", "J. K. Rowling")]
        public void should_be_able_to_get_author_detail(string authorId, string name)
        {
            var author = Subject.GetAuthorInfo(authorId);

            author.Should().NotBeNull();
            author.Metadata.Value.ForeignAuthorId.Should().Be(authorId);
            author.Metadata.Value.Name.Should().Be(name);
            author.CleanName.Should().NotBeNullOrWhiteSpace();
            author.Books.Value.Should().NotBeEmpty();
        }

        [Test]
        public void should_rank_the_exact_author_match_first()
        {
            // Open Library's own ranking puts "Frank Herbert Hayward" and "Simonds, Frank Herbert"
            // above the actual Frank Herbert, which is why the provider re-ranks.
            var results = Subject.SearchForNewAuthor("Frank Herbert");

            results.Should().NotBeEmpty();
            results.First().Metadata.Value.ForeignAuthorId.Should().Be(FrankHerbert);
        }

        [Test]
        public void every_book_should_have_exactly_one_monitored_edition()
        {
            var author = Subject.GetAuthorInfo(FrankHerbert);

            foreach (var book in author.Books.Value.Where(x => x.Editions.Value.Any()))
            {
                book.Editions.Value.Count(x => x.Monitored)
                    .Should().Be(1, "'{0}' must have exactly one monitored edition", book.Title);
            }
        }

        [Test]
        public void should_populate_book_identity_and_author_metadata()
        {
            var author = Subject.GetAuthorInfo(FrankHerbert);
            var book = author.Books.Value.First();

            book.ForeignBookId.Should().NotBeNullOrWhiteSpace();
            book.Title.Should().NotBeNullOrWhiteSpace();
            book.CleanTitle.Should().NotBeNullOrWhiteSpace();
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be(FrankHerbert);
            book.AnyEditionOk.Should().BeTrue();
        }

        [Test]
        public void should_get_book_detail_with_text_editions()
        {
            // Dune.
            var result = Subject.GetBookInfo("OL893414W");

            result.Item1.Should().NotBeNullOrWhiteSpace();
            result.Item2.ForeignBookId.Should().Be("OL893414W");
            result.Item3.Should().NotBeEmpty();

            var editions = result.Item2.Editions.Value;
            editions.Should().NotBeEmpty();
            editions.Should().Contain(x => x.IsEbook, "Open Library supplies the text editions");
            editions.Count(x => x.Monitored).Should().Be(1);
        }

        [Test]
        public void audiobook_editions_should_be_distinguishable_from_text_editions()
        {
            var result = Subject.GetBookInfo("OL893414W");
            var audio = result.Item2.Editions.Value.Where(x => !x.IsEbook).ToList();

            // Enrichment is best-effort: if Audible is unreachable the lookup must still succeed,
            // so only assert the shape of what comes back rather than that anything came back.
            foreach (var edition in audio)
            {
                edition.Format.Should().Be("Audiobook");
                edition.Asin.Should().NotBeNullOrWhiteSpace();
            }
        }

        [Test]
        public void should_return_null_for_changed_authors()
        {
            // Signals "no change feed", which makes RefreshAuthorService use its own heuristic.
            Subject.GetChangedAuthors(System.DateTime.UtcNow.AddDays(-1)).Should().BeNull();
        }
    }

    [TestFixture]
    public class OpenLibraryKeyFixture
    {
        [TestCase("/authors/OL79034A", "OL79034A")]
        [TestCase("OL79034A", "OL79034A")]
        [TestCase("/works/OL893414W", "OL893414W")]
        [TestCase("/books/OL7353617M/", "OL7353617M")]
        [TestCase("/languages/eng", "eng")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void should_normalize_open_library_keys(string input, string expected)
        {
            OpenLibraryProxy.NormalizeKey(input).Should().Be(expected);
        }
    }
}
