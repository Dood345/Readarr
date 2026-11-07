using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class SameFileSpecificationFixture : CoreTest<SameFileSpecification>
    {
        private readonly string _path = @"C:\books\Frank Herbert\Dune Messiah\Dune Messiah.m4b".AsOsAgnostic();

        private LocalBook GivenLocalBook(bool existingFile, int editionId, int existingEditionId, string existingPath = null)
        {
            return new LocalBook
            {
                Path = _path,
                Size = 1000,
                ExistingFile = existingFile,
                Edition = new Edition { Id = editionId },
                Book = new Book
                {
                    BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile>
                    {
                        new BookFile { Path = existingPath ?? _path, Size = 1000, EditionId = existingEditionId }
                    })
                }
            };
        }

        [Test]
        public void should_reject_a_new_file_the_same_size_as_an_existing_one()
        {
            Subject.IsSatisfiedBy(GivenLocalBook(false, 2, 1, @"C:\downloads\other.m4b".AsOsAgnostic()), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_an_existing_file_that_still_identifies_as_the_same_edition()
        {
            Subject.IsSatisfiedBy(GivenLocalBook(true, 1, 1), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_let_an_existing_file_move_to_another_edition_of_the_book()
        {
            // A rescan now puts the m4b on the audiobook edition instead of the ebook one it was
            // first filed under; the import is how the database record gets corrected.
            Subject.IsSatisfiedBy(GivenLocalBook(true, 2, 1), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_still_reject_an_existing_file_when_another_file_of_the_same_size_exists()
        {
            Subject.IsSatisfiedBy(GivenLocalBook(true, 2, 1, @"C:\books\Frank Herbert\Dune Messiah\other.m4b".AsOsAgnostic()), null).Accepted.Should().BeFalse();
        }
    }
}
