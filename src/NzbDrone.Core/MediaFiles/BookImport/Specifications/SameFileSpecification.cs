using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class SameFileSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly Logger _logger;

        public SameFileSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            var bookFiles = localBook.Book?.BookFiles?.Value;

            if (bookFiles == null || !bookFiles.Any())
            {
                _logger.Debug("No existing book file, skipping");
                return Decision.Accept();
            }

            foreach (var bookFile in bookFiles)
            {
                if (bookFile == null)
                {
                    var book = localBook.Book;
                    _logger.Trace("Unable to get book file details from the DB. BookId: {0}", book.Id);

                    return Decision.Accept();
                }

                if (bookFile.Size == localBook.Size)
                {
                    // The existing file may be this very file, re-identified by a rescan. When the
                    // scan now puts it on another edition of the book (an m4b that had been filed
                    // under the ebook edition), letting the import through is how the mapping gets
                    // corrected - the file itself is not moved, only its database record replaced.
                    if (localBook.ExistingFile && localBook.Edition != null &&
                        bookFile.Path.PathEquals(localBook.Path) && bookFile.EditionId != localBook.Edition.Id)
                    {
                        _logger.Debug("'{0}' is already imported but now identifies as edition {1} rather than {2}, re-homing", localBook.Path, localBook.Edition.Id, bookFile.EditionId);
                        continue;
                    }

                    _logger.Debug("'{0}' Has the same filesize as existing file", localBook.Path);
                    return Decision.Reject("Has the same filesize as existing file");
                }
            }

            return Decision.Accept();
        }
    }
}
