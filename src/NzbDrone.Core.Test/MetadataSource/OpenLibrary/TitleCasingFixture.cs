using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    [TestFixture]
    public class TitleCasingFixture : TestBase
    {
        [TestCase("House atreides", "House Atreides")]
        [TestCase("Easter parade", "Easter Parade")]
        [TestCase("The magician's apprentice", "The Magician's Apprentice")]
        [TestCase("The road to Dune", "The Road to Dune")]
        [TestCase("of mice and men", "Of Mice and Men")]
        [TestCase("iPhone for dummies", "iPhone for Dummies")]
        [TestCase("Dune: the butlerian jihad", "Dune: The Butlerian Jihad")]
        [TestCase("Dune : Red Plague", "Dune : Red Plague")]
        [TestCase("Dune Messiah", "Dune Messiah")]
        [TestCase("DUNE", "DUNE")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void should_capitalise_lowercase_words_and_leave_the_rest(string title, string expected)
        {
            TitleCasing.Normalize(title).Should().Be(expected);
        }
    }
}
