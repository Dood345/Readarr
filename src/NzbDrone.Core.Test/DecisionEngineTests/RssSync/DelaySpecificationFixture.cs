using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.RssSync
{
    [TestFixture]
    public class DelaySpecificationFixture : CoreTest<DelaySpecification>
    {
        private QualityProfile _profile;
        private DelayProfile _delayProfile;
        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            _profile = Builder<QualityProfile>.CreateNew()
                                       .Build();

            // A fresh DelayProfile already lists Usenet first, which is what makes it preferred.
            _delayProfile = new DelayProfile();

            var author = Builder<Author>.CreateNew()
                                        .With(s => s.QualityProfile = _profile)
                                        .Build();

            _remoteBook = Builder<RemoteBook>.CreateNew()
                                                   .With(r => r.Author = author)
                                                   .Build();

            _profile.Items = new List<QualityProfileQualityItem>();
            _profile.Items.Add(new QualityProfileQualityItem { Allowed = true, Quality = Quality.PDF });
            _profile.Items.Add(new QualityProfileQualityItem { Allowed = true, Quality = Quality.AZW3 });
            _profile.Items.Add(new QualityProfileQualityItem { Allowed = true, Quality = Quality.MP3 });

            _profile.Cutoff = Quality.AZW3.Id;

            _remoteBook.ParsedBookInfo = new ParsedBookInfo();
            _remoteBook.Release = new ReleaseInfo();
            _remoteBook.Release.DownloadProtocol = nameof(UsenetDownloadProtocol);

            _remoteBook.Books = Builder<Book>.CreateListOfSize(1).Build().ToList();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile> { });

            Mocker.GetMock<IDelayProfileService>()
                  .Setup(s => s.BestForTags(It.IsAny<HashSet<int>>()))
                  .Returns(_delayProfile);

            Mocker.GetMock<IPendingReleaseService>()
                  .Setup(s => s.GetPendingRemoteBooks(It.IsAny<int>()))
                  .Returns(new List<RemoteBook>());
        }

        private void GivenExistingFile(QualityModel quality)
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>
                {
                    new BookFile
                    {
                        Quality = quality
                    }
                });
        }

        private void GivenUpgradeForExistingFile()
        {
            Mocker.GetMock<IUpgradableSpecification>()
                  .Setup(s => s.IsUpgradable(It.IsAny<QualityProfile>(), It.IsAny<QualityModel>(), It.IsAny<List<CustomFormat>>(), It.IsAny<QualityModel>(), It.IsAny<List<CustomFormat>>()))
                  .Returns(true);
        }

        private void GivenUsenetDelay(int minutes)
        {
            _delayProfile.Items.Single(x => x.Protocol == nameof(UsenetDownloadProtocol)).Delay = minutes;
        }

        [Test]
        public void should_be_true_when_user_invoked_search()
        {
            Subject.IsSatisfiedBy(new RemoteBook(), new BookSearchCriteria { UserInvokedSearch = true }).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_false_when_system_invoked_search_and_release_is_younger_than_delay()
        {
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.MOBI);
            _remoteBook.Release.PublishDate = DateTime.UtcNow;

            GivenUsenetDelay(720);

            Subject.IsSatisfiedBy(_remoteBook, new BookSearchCriteria()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_true_when_profile_does_not_have_a_delay()
        {
            GivenUsenetDelay(0);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_false_when_quality_is_last_allowed_in_profile_and_bypass_disabled()
        {
            _remoteBook.Release.PublishDate = DateTime.UtcNow;
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.MP3);

            GivenUsenetDelay(720);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_true_when_quality_is_last_allowed_in_profile_and_bypass_enabled()
        {
            GivenUsenetDelay(720);
            _delayProfile.BypassIfHighestQuality = true;

            _remoteBook.Release.PublishDate = DateTime.UtcNow;
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.MP3);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_true_when_release_is_older_than_delay()
        {
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.MOBI);
            _remoteBook.Release.PublishDate = DateTime.UtcNow.AddHours(-10);

            GivenUsenetDelay(60);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_false_when_release_is_younger_than_delay()
        {
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.MOBI);
            _remoteBook.Release.PublishDate = DateTime.UtcNow;

            GivenUsenetDelay(720);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_true_when_release_is_a_proper_for_existing_book()
        {
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.MP3, new Revision(version: 2));
            _remoteBook.Release.PublishDate = DateTime.UtcNow;

            GivenExistingFile(new QualityModel(Quality.MP3));
            GivenUpgradeForExistingFile();

            Mocker.GetMock<IUpgradableSpecification>()
                  .Setup(s => s.IsRevisionUpgrade(It.IsAny<QualityModel>(), It.IsAny<QualityModel>()))
                  .Returns(true);

            GivenUsenetDelay(720);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_true_when_release_is_a_real_for_existing_book()
        {
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.MP3, new Revision(real: 1));
            _remoteBook.Release.PublishDate = DateTime.UtcNow;

            GivenExistingFile(new QualityModel(Quality.MP3));
            GivenUpgradeForExistingFile();

            Mocker.GetMock<IUpgradableSpecification>()
                  .Setup(s => s.IsRevisionUpgrade(It.IsAny<QualityModel>(), It.IsAny<QualityModel>()))
                  .Returns(true);

            GivenUsenetDelay(720);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_false_when_release_is_proper_for_existing_book_of_different_quality()
        {
            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.AZW3, new Revision(version: 2));
            _remoteBook.Release.PublishDate = DateTime.UtcNow;

            GivenExistingFile(new QualityModel(Quality.PDF));

            GivenUsenetDelay(720);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_false_when_custom_format_score_is_above_minimum_but_bypass_disabled()
        {
            _remoteBook.Release.PublishDate = DateTime.UtcNow;
            _remoteBook.CustomFormatScore = 100;

            GivenUsenetDelay(720);
            _delayProfile.MinimumCustomFormatScore = 50;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_false_when_custom_format_score_is_above_minimum_and_bypass_enabled_but_under_minimum()
        {
            _remoteBook.Release.PublishDate = DateTime.UtcNow;
            _remoteBook.CustomFormatScore = 5;

            GivenUsenetDelay(720);
            _delayProfile.BypassIfAboveCustomFormatScore = true;
            _delayProfile.MinimumCustomFormatScore = 50;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_true_when_custom_format_score_is_above_minimum_and_bypass_enabled()
        {
            _remoteBook.Release.PublishDate = DateTime.UtcNow;
            _remoteBook.CustomFormatScore = 100;

            GivenUsenetDelay(720);
            _delayProfile.BypassIfAboveCustomFormatScore = true;
            _delayProfile.MinimumCustomFormatScore = 50;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }
    }
}
