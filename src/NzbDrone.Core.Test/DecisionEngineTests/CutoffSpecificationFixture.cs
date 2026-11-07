using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class CutoffSpecificationFixture : CoreTest<UpgradableSpecification>
    {
        [Test]
        public void should_return_true_if_current_book_is_less_than_cutoff()
        {
            Subject.CutoffNotMet(
             new QualityProfile
             {
                 Cutoff = Quality.MP3.Id,
                 Items = Qualities.QualityFixture.GetDefaultQualities(),
                 UpgradeAllowed = true
             },
             new List<QualityModel> { new QualityModel(Quality.Unknown, new Revision(version: 2)) },
             new List<CustomFormat>()).Should().BeTrue();
        }

        [Test]
        public void should_measure_an_ebook_against_the_best_allowed_text_quality_when_cutoff_is_audio()
        {
            // Cutoff MP3 belongs to the audio side; the text side is cut off at its best allowed
            // quality (AZW3), so an EPUB still wants an upgrade and an AZW3 does not.
            var profile = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = true
            };

            Subject.CutoffNotMet(profile, new List<QualityModel> { new QualityModel(Quality.EPUB) }, new List<CustomFormat>()).Should().BeTrue();
            Subject.CutoffNotMet(profile, new List<QualityModel> { new QualityModel(Quality.AZW3) }, new List<CustomFormat>()).Should().BeFalse();
        }

        [Test]
        public void should_measure_an_audiobook_against_the_best_allowed_audio_quality_when_cutoff_is_text()
        {
            var profile = new QualityProfile
            {
                Cutoff = Quality.EPUB.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = true
            };

            Subject.CutoffNotMet(profile, new List<QualityModel> { new QualityModel(Quality.MP3) }, new List<CustomFormat>()).Should().BeTrue();
            Subject.CutoffNotMet(profile, new List<QualityModel> { new QualityModel(Quality.FLAC) }, new List<CustomFormat>()).Should().BeFalse();
        }

        [Test]
        public void should_use_the_first_allowed_quality_of_the_media_type_when_upgrades_are_off()
        {
            var profile = new QualityProfile
            {
                Cutoff = Quality.FLAC.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.EPUB, Quality.AZW3, Quality.MP3, Quality.FLAC),
                UpgradeAllowed = false
            };

            // EPUB is the first allowed text quality, so a MOBI is below it and an EPUB meets it.
            Subject.CutoffNotMet(profile, new List<QualityModel> { new QualityModel(Quality.MOBI) }, new List<CustomFormat>()).Should().BeTrue();
            Subject.CutoffNotMet(profile, new List<QualityModel> { new QualityModel(Quality.EPUB) }, new List<CustomFormat>()).Should().BeFalse();
            Subject.CutoffNotMet(profile, new List<QualityModel> { new QualityModel(Quality.MP3) }, new List<CustomFormat>()).Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_current_book_is_equal_to_cutoff()
        {
            Subject.CutoffNotMet(
            new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = true
            },
            new List<QualityModel> { new QualityModel(Quality.MP3, new Revision(version: 2)) },
            new List<CustomFormat>()).Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_current_book_is_greater_than_cutoff()
        {
            Subject.CutoffNotMet(
            new QualityProfile
            {
                Cutoff = Quality.EPUB.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = true
            },
            new List<QualityModel> { new QualityModel(Quality.AZW3, new Revision(version: 2)) },
            new List<CustomFormat>()).Should().BeFalse();
        }

        [Test]
        public void should_return_true_when_new_book_is_proper_but_existing_is_not()
        {
            Subject.CutoffNotMet(
            new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = true
            },
            new List<QualityModel> { new QualityModel(Quality.MP3, new Revision(version: 1)) },
            new List<CustomFormat>(),
            new QualityModel(Quality.MP3, new Revision(version: 2))).Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_cutoff_is_met_and_quality_is_higher()
        {
            Subject.CutoffNotMet(
            new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = true
            },
            new List<QualityModel> { new QualityModel(Quality.MP3, new Revision(version: 2)) },
            new List<CustomFormat>(),
            new QualityModel(Quality.FLAC, new Revision(version: 2))).Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_cutoffs_are_met_but_is_a_revision_upgrade()
        {
            var profile = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = true
            };

            Subject.CutoffNotMet(
                profile,
                new List<QualityModel> { new QualityModel(Quality.FLAC, new Revision(version: 1)) },
                new List<CustomFormat>(),
                new QualityModel(Quality.FLAC, new Revision(version: 2))).Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_quality_profile_does_not_allow_upgrades_but_cutoff_is_set_to_highest_quality()
        {
            var profile = new QualityProfile
            {
                Cutoff = Quality.FLAC.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                UpgradeAllowed = false
            };

            Subject.CutoffNotMet(
                profile,
                new List<QualityModel> { new QualityModel(Quality.Unknown, new Revision(version: 1)) },
                new List<CustomFormat>(),
                new QualityModel(Quality.MP3, new Revision(version: 2))).Should().BeFalse();
        }
    }
}
