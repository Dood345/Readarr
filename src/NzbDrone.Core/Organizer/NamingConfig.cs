using System.IO;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfig : ModelBase
    {
        // Renaming is on by default because the book folder only exists inside StandardBookFormat:
        // with it off every import lands loose in the author folder, and Audiobookshelf then shows
        // an author's whole catalogue as one book.
        public static NamingConfig Default => new NamingConfig
        {
            RenameBooks = true,
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Smart,
            StandardBookFormat = "{Book Title}" + Path.DirectorySeparatorChar + "{Author Name} - {Book Title}{ (PartNumber)}",
            AuthorFolderFormat = "{Author Name}",
        };

        public bool RenameBooks { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string StandardBookFormat { get; set; }
        public string AuthorFolderFormat { get; set; }
    }
}
