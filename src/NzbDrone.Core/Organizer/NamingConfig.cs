using System.IO;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfig : ModelBase
    {
        // Renaming is on by default because the book folder only exists inside StandardBookFormat:
        // with it off every import lands loose in the author folder, and Audiobookshelf then shows
        // an author's whole catalogue as one book.
        //
        // The folder and file are named after the work rather than the edition: an ebook and an
        // audiobook of the same book carry different edition titles ("House Atreides" against
        // "Dune: House Atreides: House Trilogy, Book 1") and would otherwise end up in different
        // folders, which is the opposite of the one-folder-per-title layout Audiobookshelf wants.
        public static NamingConfig Default => new NamingConfig
        {
            RenameBooks = true,
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Smart,
            StandardBookFormat = "{Work Title}" + Path.DirectorySeparatorChar + "{Author Name} - {Work Title}{ (PartNumber)}",
            AuthorFolderFormat = "{Author Name}",
            KeepPartFileNames = true,
        };

        public bool RenameBooks { get; set; }

        /// <summary>
        /// A chapter-per-file audiobook keeps its original file names when renamed, only the folder
        /// follows the format. Untagged rips carry the chapter names nowhere else, and
        /// Audiobookshelf reads them from the file names.
        /// </summary>
        public bool KeepPartFileNames { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string StandardBookFormat { get; set; }
        public string AuthorFolderFormat { get; set; }
    }
}
