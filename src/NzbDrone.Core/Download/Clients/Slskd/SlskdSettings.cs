using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Slskd
{
    public class SlskdSettingsValidator : AbstractValidator<SlskdSettings>
    {
        public SlskdSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
            RuleFor(c => c.DownloadPath).NotEmpty();
        }
    }

    public class SlskdSettings : IProviderConfig, ISlskdConnectionSettings
    {
        private static readonly SlskdSettingsValidator Validator = new SlskdSettingsValidator();

        public SlskdSettings()
        {
            BaseUrl = "http://localhost:5030";
            DownloadPath = "/downloads";
        }

        [FieldDefinition(0, Label = "URL", HelpText = "URL of your slskd instance, for example http://slskd:5030")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "API Key", Privacy = PrivacyLevel.ApiKey, HelpText = "Needs a key with the readwrite or administrator role, a readonly key cannot enqueue downloads")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Label = "Download Path", HelpText = "slskd's completed download directory as Readarr sees it. This is slskd's own downloads path, which is often a subfolder such as /downloads/music - check directories.downloads in slskd.yml")]
        public string DownloadPath { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
