using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class ProtocolSpecificationFixture : CoreTest<ProtocolSpecification>
    {
        private RemoteBook _remoteBook;
        private DelayProfile _delayProfile;

        [SetUp]
        public void Setup()
        {
            _remoteBook = new RemoteBook();
            _remoteBook.Release = new ReleaseInfo();
            _remoteBook.Author = new Author();

            _delayProfile = new DelayProfile();

            Mocker.GetMock<IDelayProfileService>()
                  .Setup(s => s.BestForTags(It.IsAny<HashSet<int>>()))
                  .Returns(_delayProfile);
        }

        private void GivenProtocol(string downloadProtocol)
        {
            _remoteBook.Release.DownloadProtocol = downloadProtocol;
        }

        private void GivenProtocolAllowed(string protocol, bool allowed)
        {
            _delayProfile.Items.Single(x => x.Protocol == protocol).Allowed = allowed;
        }

        [Test]
        public void should_be_true_if_usenet_and_usenet_is_enabled()
        {
            GivenProtocol(nameof(UsenetDownloadProtocol));
            GivenProtocolAllowed(nameof(UsenetDownloadProtocol), true);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().Be(true);
        }

        [Test]
        public void should_be_true_if_torrent_and_torrent_is_enabled()
        {
            GivenProtocol(nameof(TorrentDownloadProtocol));
            GivenProtocolAllowed(nameof(TorrentDownloadProtocol), true);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().Be(true);
        }

        [Test]
        public void should_be_false_if_usenet_and_usenet_is_disabled()
        {
            GivenProtocol(nameof(UsenetDownloadProtocol));
            GivenProtocolAllowed(nameof(UsenetDownloadProtocol), false);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().Be(false);
        }

        [Test]
        public void should_be_false_if_torrent_and_torrent_is_disabled()
        {
            GivenProtocol(nameof(TorrentDownloadProtocol));
            GivenProtocolAllowed(nameof(TorrentDownloadProtocol), false);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().Be(false);
        }
    }
}
