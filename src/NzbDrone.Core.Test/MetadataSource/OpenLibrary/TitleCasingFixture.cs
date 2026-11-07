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
        [TestCase("Carl’s Doomsday Scenario", "Carl's Doomsday Scenario")]
        [TestCase("the dungeon anarchist’s cookbook", "The Dungeon Anarchist's Cookbook")]
        [TestCase("DUNE", "DUNE")]
        [TestCase("Hunters Of Dune", "Hunters of Dune")]
        [TestCase("Martin Luther King, Jr., Malcolm X, and the Civil Rights Struggle of the 1950S and 1960S", "Martin Luther King, Jr., Malcolm X, and the Civil Rights Struggle of the 1950s and 1960s")]
        [TestCase("Reading the American Past 3E V2", "Reading the American Past 3E V2")]
        [TestCase("Martin Luther King, Jr., Malcolm X, and the Civil Rights Struggle of the 1950s and 1960s", "Martin Luther King, Jr., Malcolm X, and the Civil Rights Struggle of the 1950s and 1960s")]
        [TestCase("the 20th century", "The 20th Century")]
        [TestCase("3rd rock from the sun", "3rd Rock from the Sun")]
        [TestCase("(un)happy days", "(Un)happy Days")]
        [TestCase("DUNE OF THE DEAD", "DUNE OF THE DEAD")]
        [TestCase("Of Mice and Men", "Of Mice and Men")]
        [TestCase("Dune: Of Sand", "Dune: Of Sand")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void should_capitalise_lowercase_words_and_leave_the_rest(string title, string expected)
        {
            TitleCasing.Normalize(title).Should().Be(expected);
        }
    }
}
