using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    /// <summary>
    /// Renaming is now on by default. The book folder only exists inside StandardBookFormat, so
    /// with renaming off every import lands loose in the author folder and Audiobookshelf shows an
    /// author's whole catalogue as a single book. Existing installs are switched on too: nothing
    /// on disk moves until the user runs a rename, and new imports stop piling up loose.
    /// </summary>
    [Migration(043)]
    public class rename_books_by_default : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.Sql("UPDATE \"NamingConfig\" SET \"RenameBooks\" = 1");
        }
    }
}
