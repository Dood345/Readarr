using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    /// <summary>
    /// Two naming changes for the one-folder-per-title layout. The default format now names the
    /// folder and file after the work rather than the edition, so an ebook and its audiobook share
    /// a folder; an install still on the old default is moved to the new one, a customised format
    /// is left alone. And multi-part books keep their original file names by default, since
    /// untagged chapter rips carry the chapter names nowhere else.
    /// </summary>
    [Migration(045)]
    public class work_title_naming : NzbDroneMigrationBase
    {
        private const string OldDefaultFormat = "{Book Title}/{Author Name} - {Book Title}{ (PartNumber)}";
        private const string NewDefaultFormat = "{Work Title}/{Author Name} - {Work Title}{ (PartNumber)}";

        protected override void MainDbUpgrade()
        {
            Alter.Table("NamingConfig").AddColumn("KeepPartFileNames").AsBoolean().WithDefaultValue(true);

            Execute.Sql($"UPDATE \"NamingConfig\" SET \"StandardBookFormat\" = '{NewDefaultFormat}' WHERE \"StandardBookFormat\" = '{OldDefaultFormat}'");
        }
    }
}
