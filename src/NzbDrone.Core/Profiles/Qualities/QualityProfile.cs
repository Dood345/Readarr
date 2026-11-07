using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities
{
    public class QualityProfile : ModelBase
    {
        public QualityProfile()
        {
            FormatItems = new List<ProfileFormatItem>();
        }

        public string Name { get; set; }
        public bool UpgradeAllowed { get; set; }
        public int Cutoff { get; set; }
        public int MinFormatScore { get; set; }
        public int CutoffFormatScore { get; set; }
        public List<ProfileFormatItem> FormatItems { get; set; }
        public List<QualityProfileQualityItem> Items { get; set; }

        public Quality FirstAllowedQuality()
        {
            var firstAllowed = Items.First(q => q.Allowed);

            if (firstAllowed.Quality != null)
            {
                return firstAllowed.Quality;
            }

            // Returning any item from the group will work,
            // returning the first because it's the true first quality.
            return firstAllowed.Items.First().Quality;
        }

        public Quality LastAllowedQuality()
        {
            var lastAllowed = Items.Last(q => q.Allowed);

            if (lastAllowed.Quality != null)
            {
                return lastAllowed.Quality;
            }

            // Returning any item from the group will work,
            // returning the last because it's the true last quality.
            return lastAllowed.Items.Last().Quality;
        }

        /// <summary>
        /// The cutoff that applies to files of one media type. A profile stores a single cutoff,
        /// which necessarily belongs to either the text or the audio qualities; for the other type
        /// the best allowed quality of that type stands in, so a profile cut off at M4B still
        /// upgrades an ebook as far as AZW3 and then stops, rather than never or forever.
        /// </summary>
        public int CutoffFor(BookMediaType mediaType)
        {
            var cutoffItem = Items.ElementAtOrDefault(GetIndex(Cutoff).Index);

            if (cutoffItem != null && cutoffItem.GetQualities().Any(q => q.MediaType() == mediaType))
            {
                return Cutoff;
            }

            var best = Items.LastOrDefault(i => i.Allowed && i.GetQualities().Any(q => q.MediaType() == mediaType));

            return best == null ? Cutoff : ItemId(best);
        }

        /// <summary>
        /// The lowest allowed quality of one media type, used as the cutoff when upgrades are off.
        /// Falls back to the profile's overall first allowed quality when the type is not allowed
        /// at all, which keeps the comparison well defined for a file the profile did not ask for.
        /// </summary>
        public Quality FirstAllowedQualityFor(BookMediaType mediaType)
        {
            var firstAllowed = Items.FirstOrDefault(i => i.Allowed && i.GetQualities().Any(q => q.MediaType() == mediaType));

            return firstAllowed?.GetQualities().First(q => q.MediaType() == mediaType) ?? FirstAllowedQuality();
        }

        /// <summary>
        /// The cutoff the decision engine should compare a file of the given type against: the
        /// media-type cutoff when upgrades are allowed, otherwise that type's first allowed quality.
        /// </summary>
        public int EffectiveCutoffFor(BookMediaType mediaType)
        {
            return UpgradeAllowed ? CutoffFor(mediaType) : FirstAllowedQualityFor(mediaType).Id;
        }

        private static int ItemId(QualityProfileQualityItem item)
        {
            return item.Quality?.Id ?? item.Id;
        }

        public QualityIndex GetIndex(Quality quality, bool respectGroupOrder = false)
        {
            return GetIndex(quality.Id, respectGroupOrder);
        }

        public QualityIndex GetIndex(int id, bool respectGroupOrder = false)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                var quality = item.Quality;

                // Quality matches by ID
                if (quality != null && quality.Id == id)
                {
                    return new QualityIndex(i);
                }

                // Group matches by ID
                if (item.Id > 0 && item.Id == id)
                {
                    return new QualityIndex(i);
                }

                for (var g = 0; g < item.Items.Count; g++)
                {
                    var groupItem = item.Items[g];

                    if (groupItem.Quality.Id == id)
                    {
                        return respectGroupOrder ? new QualityIndex(i, g) : new QualityIndex(i);
                    }
                }
            }

            return new QualityIndex();
        }

        public int CalculateCustomFormatScore(List<CustomFormat> formats)
        {
            return FormatItems.Where(x => formats.Contains(x.Format)).Sum(x => x.Score);
        }
    }
}
