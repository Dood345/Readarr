using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    /// <summary>
    /// The two shapes a book comes in. A single Book can have an edition of each - the case this
    /// fork exists to handle, where one folder holds both an epub and an m4b of the same title.
    /// </summary>
    public enum BookMediaType
    {
        Ebook = 0,
        Audiobook = 1
    }

    public static class BookMediaTypeExtensions
    {
        /// <summary>
        /// Quality ids below 10 are text formats (Unknown Text, PDF, MOBI, EPUB, AZW3); 10 and above
        /// are audio (Unknown Audio, MP3, M4B, FLAC). See Quality.All.
        /// </summary>
        private const int FirstAudioQualityId = 10;

        public static BookMediaType MediaType(this Edition edition)
        {
            return edition.IsEbook ? BookMediaType.Ebook : BookMediaType.Audiobook;
        }

        public static BookMediaType MediaType(this Quality quality)
        {
            return quality != null && quality.Id >= FirstAudioQualityId
                ? BookMediaType.Audiobook
                : BookMediaType.Ebook;
        }

        /// <summary>
        /// Every monitored edition. A book may monitor one edition per media type, so this is not
        /// necessarily a single item - use <see cref="PrimaryEdition"/> where one representative
        /// edition is wanted for display.
        /// </summary>
        public static List<Edition> MonitoredEditions(this IEnumerable<Edition> editions)
        {
            return editions?.Where(x => x.Monitored).ToList() ?? new List<Edition>();
        }

        /// <summary>
        /// One edition to stand for the book in covers, overviews, notifications and search titles.
        /// Prefers a monitored ebook, then any monitored edition, then anything at all, so callers
        /// that used to do Single(x => x.Monitored) keep working when only one edition is monitored.
        /// </summary>
        public static Edition PrimaryEdition(this IEnumerable<Edition> editions)
        {
            if (editions == null)
            {
                return null;
            }

            var all = editions as IList<Edition> ?? editions.ToList();

            return all.FirstOrDefault(x => x.Monitored && x.IsEbook)
                   ?? all.FirstOrDefault(x => x.Monitored)
                   ?? all.FirstOrDefault();
        }

        public static Edition MonitoredEditionFor(this IEnumerable<Edition> editions, BookMediaType mediaType)
        {
            return editions?.FirstOrDefault(x => x.Monitored && x.MediaType() == mediaType);
        }
    }
}
