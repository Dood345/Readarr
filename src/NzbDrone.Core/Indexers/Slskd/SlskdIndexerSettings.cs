using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class SlskdIndexerSettingsValidator : AbstractValidator<SlskdIndexerSettings>
    {
        public SlskdIndexerSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
            RuleFor(c => c.SearchTimeout).InclusiveBetween(5, 300);
            RuleFor(c => c.MinimumFileCount).GreaterThanOrEqualTo(1);
            RuleFor(c => c.MaximumQueueLength).GreaterThanOrEqualTo(0);
        }
    }

    public class SlskdIndexerSettings : IIndexerSettings, ISlskdConnectionSettings
    {
        private static readonly SlskdIndexerSettingsValidator Validator = new SlskdIndexerSettingsValidator();

        public SlskdIndexerSettings()
        {
            BaseUrl = "http://localhost:5030";
            SearchTimeout = 30;
            MinimumFileCount = 1;
            MaximumQueueLength = 100;
            IncludeLockedResults = false;
            AllowedExtensions = "epub,mobi,azw3,pdf,m4b,mp3,m4a,flac";
        }

        [FieldDefinition(0, Label = "URL", HelpText = "URL of your slskd instance, for example http://slskd:5030")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "API Key", Privacy = PrivacyLevel.ApiKey, HelpText = "An API key from the slskd web.authentication.api_keys section of slskd.yml")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Type = FieldType.Number, Label = "Search Timeout", Unit = "seconds", HelpText = "How long to wait for peers to respond before returning results", Advanced = true)]
        public int SearchTimeout { get; set; }

        [FieldDefinition(3, Type = FieldType.Number, Label = "Minimum Files", HelpText = "Ignore folders with fewer matching files than this. Set to 1 for ebooks, which are usually a single file", Advanced = true)]
        public int MinimumFileCount { get; set; }

        [FieldDefinition(4, Type = FieldType.Number, Label = "Maximum Queue Length", HelpText = "Ignore peers with a longer upload queue than this. 0 disables the check", Advanced = true)]
        public int MaximumQueueLength { get; set; }

        [FieldDefinition(5, Type = FieldType.Checkbox, Label = "Include Locked Results", HelpText = "Locked files require permission from the peer and usually cannot be downloaded", Advanced = true)]
        public bool IncludeLockedResults { get; set; }

        [FieldDefinition(6, Label = "Allowed Extensions", HelpText = "Comma separated list of book and audiobook extensions to consider", Advanced = true)]
        public string AllowedExtensions { get; set; }

        // Not meaningful for Soulseek: peers either have the files or they don't, there is no
        // release date embargo to respect. Present because IIndexerSettings requires it.
        public int? EarlyReleaseLimit { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
