using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class DistanceCalculatorFixture : TestBase
    {
        private static Edition EditionOf(string title, bool isEbook, string bookTitle = null, string isbn = null)
        {
            return new Edition
            {
                Title = title,
                IsEbook = isEbook,
                Isbn13 = isbn,
                Format = isEbook ? "ebook" : "Audiobook",
                Book = new Book
                {
                    Title = bookTitle ?? title,
                    AuthorMetadata = new AuthorMetadata { Name = "Frank Herbert" },
                    SeriesLinks = new List<SeriesBookLink>()
                }
            };
        }

        private static List<LocalBook> FileOf(string path, string title = "Dune Messiah", string isbn = null)
        {
            return new List<LocalBook>
            {
                new LocalBook
                {
                    Path = path,
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Authors = new List<string> { "Frank Herbert" },
                        BookTitle = title,
                        Isbn = isbn
                    }
                }
            };
        }

        [Test]
        public void a_tag_with_the_series_prefixed_should_match_the_edition_whose_segments_contain_it()
        {
            // Regression: "Dune: House Atreides (Unabridged)" scored worse against
            // "Dune: House Atreides: House Trilogy, Book 1" than against "Dune: House Corrino",
            // because only the whole edition title was compared.
            var file = FileOf(@"C:\books\Brian Herbert\House Atreides.m4a".AsOsAgnostic(), "Dune: House Atreides (Unabridged)");

            var right = DistanceCalculator.BookDistance(file, EditionOf("Dune: House Atreides: House Trilogy, Book 1", false, "House Atreides")).NormalizedDistance();
            var wrong = DistanceCalculator.BookDistance(file, EditionOf("Dune: House Corrino", false, "Dune House Corrino")).NormalizedDistance();

            right.Should().BeLessThan(wrong);
        }

        [Test]
        public void a_long_subtitled_tag_should_match_the_work_named_by_its_title_part()
        {
            var file = FileOf(
                @"C:\books\Howard-Pitney\mlk.epub".AsOsAgnostic(),
                "Martin Luther King Jr., Malcolm X, and the Civil Rights Struggle of the 1950s and 1960s: A Brief History with Documents");

            var right = DistanceCalculator.BookDistance(file, EditionOf("Martin Luther King, Jr., Malcolm X, and the Civil Rights Struggle of the 1950S and 1960S", true)).NormalizedDistance();
            var omnibus = DistanceCalculator.BookDistance(file, EditionOf("Reading the American Past 3E V2 & Martin Luther King, Jr., Malcolm X, and the Civil Rights Struggle of the 1950S & Women's Magazines", true)).NormalizedDistance();

            right.Should().BeLessThan(omnibus);
        }

        [Test]
        public void a_differing_isbn_should_not_outweigh_an_exact_title()
        {
            // Regression: "Master of the Game" with a 2012 ISBN lost to "Easter Parade", which has
            // no ISBN at all, because a differing ISBN weighed more than the whole title.
            var file = FileOf(@"C:\books\Sidney Sheldon\Master of the Game.epub".AsOsAgnostic(), "Master of the Game", "9780062015587");

            var right = DistanceCalculator.BookDistance(file, EditionOf("Master of the Game", true, isbn: "9780007777754")).NormalizedDistance();
            var wrong = DistanceCalculator.BookDistance(file, EditionOf("Easter Parade", true)).NormalizedDistance();

            right.Should().BeLessThan(wrong);
        }

        [Test]
        public void a_matching_isbn_should_still_beat_the_same_title_with_a_different_one()
        {
            var file = FileOf(@"C:\books\Sidney Sheldon\Master of the Game.epub".AsOsAgnostic(), "Master of the Game", "9780062015587");

            var matching = DistanceCalculator.BookDistance(file, EditionOf("Master of the Game", true, isbn: "9780062015587")).NormalizedDistance();
            var differing = DistanceCalculator.BookDistance(file, EditionOf("Master of the Game", true, isbn: "9780007777754")).NormalizedDistance();

            matching.Should().BeLessThan(differing);
        }

        [Test]
        public void an_audio_file_should_prefer_the_audiobook_edition_of_the_same_book()
        {
            // Regression: the ebook and audiobook editions of a book scored the same for an m4b,
            // so the file was filed under the ebook edition and the audiobook stayed "missing".
            var file = FileOf(@"C:\books\Dune Messiah\Dune Messiah.m4b".AsOsAgnostic());

            var audio = DistanceCalculator.BookDistance(file, EditionOf("Dune Messiah", isEbook: false)).NormalizedDistance();
            var ebook = DistanceCalculator.BookDistance(file, EditionOf("Dune Messiah", isEbook: true)).NormalizedDistance();

            audio.Should().BeLessThan(ebook);
        }

        [Test]
        public void a_text_file_should_prefer_the_ebook_edition_of_the_same_book()
        {
            var file = FileOf(@"C:\books\Dune Messiah\Dune Messiah.epub".AsOsAgnostic());

            var audio = DistanceCalculator.BookDistance(file, EditionOf("Dune Messiah", isEbook: false)).NormalizedDistance();
            var ebook = DistanceCalculator.BookDistance(file, EditionOf("Dune Messiah", isEbook: true)).NormalizedDistance();

            ebook.Should().BeLessThan(audio);
        }

        [Test]
        public void an_audio_file_should_still_prefer_the_right_book_over_a_wrong_audiobook()
        {
            // The media type only breaks ties; it must not outweigh the title.
            var file = FileOf(@"C:\books\Dune Messiah\Dune Messiah.m4b".AsOsAgnostic());

            var rightBookEbook = DistanceCalculator.BookDistance(file, EditionOf("Dune Messiah", isEbook: true)).NormalizedDistance();
            var wrongBookAudio = DistanceCalculator.BookDistance(file, EditionOf("The Road to Dune", isEbook: false)).NormalizedDistance();

            rightBookEbook.Should().BeLessThan(wrongBookAudio);
        }

        [Test]
        public void should_reverse_single_reversed_author()
        {
            var input = new List<string> { "Last, First" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain("First Last");
        }

        [Test]
        public void should_reverse_two_reversed_author()
        {
            var input = new List<string>
            {
                "Last, First",
                "Last2, First2"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(4);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("Last, First");
            authors.Should().Contain("Last2, First2");
        }

        [Test]
        public void should_not_reverse_single_author()
        {
            var input = new List<string> { "First Last" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(1);
            authors.Should().Contain("First Last");
        }

        [TestCase("First1 Last1, First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1; First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 & First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 / First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 and First2 Last2", "First1 Last1", "First2 Last2")]
        public void should_split_concatenated_author(string inputString, string first, string second)
        {
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain(first);
            authors.Should().Contain(second);
            authors.Should().HaveCount(3);
        }

        [Test]
        public void should_split_concatenated_with_trailing_and()
        {
            var inputString = "First Last, First2 Last2 & First3 Last3";
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("First3 Last3");
            authors.Should().HaveCount(4);
        }

        [Test]
        public void should_not_split_if_multiple_input()
        {
            var input = new List<string>
            {
                "First Last",
                "Second Third, Fourth Fifth"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(2);
            authors.Should().Contain("First Last");
            authors.Should().Contain("Second Third, Fourth Fifth");
        }
    }
}
