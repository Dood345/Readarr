using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormats
{
    [TestFixture]
    public class BitrateSpecificationFixture : CoreTest
    {
        private BitrateSpecification _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new BitrateSpecification { Min = 96, Max = 160 };
        }

        [TestCase(96, true)]
        [TestCase(125, true)]
        [TestCase(160, true)]
        [TestCase(64, false)]
        [TestCase(320, false)]
        public void should_match_bitrates_inside_the_range(int bitrate, bool expected)
        {
            _subject.IsSatisfiedBy(new CustomFormatInput { Bitrate = bitrate }).Should().Be(expected);
        }

        [Test]
        public void should_not_match_when_the_bitrate_is_unknown()
        {
            _subject.IsSatisfiedBy(new CustomFormatInput { Bitrate = 0 }).Should().BeFalse();
        }

        [Test]
        public void should_not_match_an_unknown_bitrate_even_when_negated()
        {
            // Negate means "outside the range", which an unknown bitrate cannot claim either way;
            // the format is simply not applied to it.
            _subject.Negate = true;

            _subject.IsSatisfiedBy(new CustomFormatInput { Bitrate = 0 }).Should().BeTrue();
        }

        [Test]
        public void should_reject_a_maximum_below_the_minimum()
        {
            _subject.Max = 64;

            _subject.Validate().IsValid.Should().BeFalse();
        }
    }
}
