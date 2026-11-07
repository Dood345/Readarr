using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    /// <summary>
    /// Books used to monitor exactly one edition. Now that a book may monitor one edition per media
    /// type, existing books that have an unmonitored edition of the other type get it monitored, so
    /// a title already tracked as an ebook starts tracking its audiobook too without waiting for a
    /// metadata refresh.
    /// </summary>
    [Migration(042)]
    public class monitor_one_edition_per_media_type : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(MonitorSecondMediaType);
        }

        private void MonitorSecondMediaType(IDbConnection conn, IDbTransaction tran)
        {
            var editions = conn.Query<Edition042>(
                "SELECT \"Id\", \"BookId\", \"Monitored\", \"IsEbook\", \"Isbn13\", \"Asin\" FROM \"Editions\"",
                transaction: tran).ToList();

            var toMonitor = new List<Edition042>();

            foreach (var book in editions.GroupBy(x => x.BookId))
            {
                // Only act where the book is actually being tracked; leave fully unmonitored books
                // alone rather than silently opting them in.
                if (!book.Any(x => x.Monitored))
                {
                    continue;
                }

                foreach (var mediaType in book.GroupBy(x => x.IsEbook))
                {
                    if (mediaType.Any(x => x.Monitored))
                    {
                        continue;
                    }

                    var best = mediaType.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Isbn13))
                               ?? mediaType.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Asin))
                               ?? mediaType.First();

                    best.Monitored = true;
                    toMonitor.Add(best);
                }
            }

            if (toMonitor.Any())
            {
                conn.Execute("UPDATE \"Editions\" SET \"Monitored\" = @Monitored WHERE \"Id\" = @Id", toMonitor, transaction: tran);
            }
        }

        private class Edition042
        {
            public int Id { get; set; }
            public int BookId { get; set; }
            public bool Monitored { get; set; }
            public bool IsEbook { get; set; }
            public string Isbn13 { get; set; }
            public string Asin { get; set; }
        }
    }
}
