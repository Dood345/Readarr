using System.Collections.Generic;
using System.Data;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Datastore.Migration
{
    /// <summary>
    /// Converts protocol from the old DownloadProtocol enum (0 Unknown, 1 Usenet, 2 Torrent) to the
    /// type-name strings used by IDownloadProtocol, and reshapes DelayProfiles from a fixed pair of
    /// usenet/torrent columns into an ordered list of protocol items.
    /// </summary>
    [Migration(041)]
    public class flexible_download_protocols : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("DelayProfiles").AddColumn("Name").AsString().Nullable();
            Alter.Table("DelayProfiles").AddColumn("Items").AsString().WithDefaultValue("[]");

            Execute.WithConnection(MigrateDelayProfiles);

            Delete.Column("EnableUsenet").FromTable("DelayProfiles");
            Delete.Column("EnableTorrent").FromTable("DelayProfiles");
            Delete.Column("PreferredProtocol").FromTable("DelayProfiles");
            Delete.Column("UsenetDelay").FromTable("DelayProfiles");
            Delete.Column("TorrentDelay").FromTable("DelayProfiles");

            // Both columns were created AsInt32, so they need a new string column rather than an
            // in-place alter: SQLite cannot change a column type.
            ConvertProtocolColumn("Blocklist");
            ConvertProtocolColumn("DownloadHistory");
        }

        private void ConvertProtocolColumn(string table)
        {
            if (!Schema.Table(table).Exists())
            {
                return;
            }

            Alter.Table(table).AddColumn("ProtocolString").AsString().Nullable();

            Execute.Sql($"UPDATE \"{table}\" SET \"ProtocolString\" = '{nameof(UsenetDownloadProtocol)}' WHERE \"Protocol\" = 1");
            Execute.Sql($"UPDATE \"{table}\" SET \"ProtocolString\" = '{nameof(TorrentDownloadProtocol)}' WHERE \"Protocol\" = 2");

            Delete.Column("Protocol").FromTable(table);
            Rename.Column("ProtocolString").OnTable(table).To("Protocol");
        }

        private void MigrateDelayProfiles(IDbConnection conn, IDbTransaction tran)
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<DelayProfileProtocolItem041>>());

            var rows = conn.Query<DelayProfile040>("SELECT \"Id\", \"EnableUsenet\", \"EnableTorrent\", \"PreferredProtocol\", \"UsenetDelay\", \"TorrentDelay\" FROM \"DelayProfiles\"", transaction: tran);

            var updated = new List<DelayProfile041>();

            foreach (var row in rows)
            {
                var usenet = new DelayProfileProtocolItem041
                {
                    Name = "Usenet",
                    Protocol = nameof(UsenetDownloadProtocol),
                    Allowed = row.EnableUsenet,
                    Delay = row.UsenetDelay
                };

                var torrent = new DelayProfileProtocolItem041
                {
                    Name = "Torrent",
                    Protocol = nameof(TorrentDownloadProtocol),
                    Allowed = row.EnableTorrent,
                    Delay = row.TorrentDelay
                };

                // PreferredProtocol 2 was Torrent. Order now carries preference.
                var items = row.PreferredProtocol == 2
                    ? new List<DelayProfileProtocolItem041> { torrent, usenet }
                    : new List<DelayProfileProtocolItem041> { usenet, torrent };

                updated.Add(new DelayProfile041
                {
                    Id = row.Id,
                    Name = row.Id == 1 ? "Default" : $"Delay Profile {row.Id}",
                    Items = items
                });
            }

            conn.Execute("UPDATE \"DelayProfiles\" SET \"Name\" = @Name, \"Items\" = @Items WHERE \"Id\" = @Id", updated, transaction: tran);
        }

        private class DelayProfile040 : ModelBase
        {
            public bool EnableUsenet { get; set; }
            public bool EnableTorrent { get; set; }
            public int PreferredProtocol { get; set; }
            public int UsenetDelay { get; set; }
            public int TorrentDelay { get; set; }
        }

        private class DelayProfile041 : ModelBase
        {
            public string Name { get; set; }
            public List<DelayProfileProtocolItem041> Items { get; set; }
        }

        private class DelayProfileProtocolItem041 : IEmbeddedDocument
        {
            public string Name { get; set; }
            public string Protocol { get; set; }
            public bool Allowed { get; set; }
            public int Delay { get; set; }
        }
    }
}
