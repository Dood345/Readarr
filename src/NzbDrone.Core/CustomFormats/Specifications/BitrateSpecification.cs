using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public class BitrateSpecificationValidator : AbstractValidator<BitrateSpecification>
    {
        public BitrateSpecificationValidator()
        {
            RuleFor(c => c.Min).GreaterThanOrEqualTo(0);
            RuleFor(c => c.Max).GreaterThan(c => c.Min);
        }
    }

    /// <summary>
    /// Matches on the audio bitrate: of a release when the indexer reports one (slskd does), of a
    /// file from its tags once imported. A release or file whose bitrate is unknown never matches.
    /// </summary>
    public class BitrateSpecification : CustomFormatSpecificationBase
    {
        private static readonly BitrateSpecificationValidator Validator = new BitrateSpecificationValidator();

        public override int Order => 9;
        public override string ImplementationName => "Bitrate";

        [FieldDefinition(1, Label = "Minimum Bitrate", HelpText = "Audio must be at least this bitrate. 120 kbps is about 0.9 MB per minute", Unit = "kbps", Type = FieldType.Number)]
        public int Min { get; set; }

        [FieldDefinition(1, Label = "Maximum Bitrate", HelpText = "Audio must be at most this bitrate", Unit = "kbps", Type = FieldType.Number)]
        public int Max { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            var bitrate = input.Bitrate;

            return bitrate > 0 && bitrate >= Min && bitrate <= Max;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
