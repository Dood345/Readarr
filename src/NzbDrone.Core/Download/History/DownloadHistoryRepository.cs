using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History
{
    public interface IDownloadHistoryRepository : IBasicRepository<DownloadHistory>
    {
        List<DownloadHistory> FindByDownloadId(string downloadId);
        List<DownloadHistory> FindGrabs(int downloadClientId, int count);
        void DeleteByAuthorId(int authorId);
    }

    public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
    {
        public DownloadHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<DownloadHistory> FindByDownloadId(string downloadId)
        {
            return Query(h => h.DownloadId == downloadId)
                .OrderByDescending(h => h.Date)
                .ToList();
        }

        /// <summary>
        /// The most recent <paramref name="count"/> grabs sent to one download client, newest first.
        /// </summary>
        public List<DownloadHistory> FindGrabs(int downloadClientId, int count)
        {
            var builder = Builder()
                .Where<DownloadHistory>(h => h.DownloadClientId == downloadClientId && h.EventType == DownloadHistoryEventType.DownloadGrabbed)
                .OrderBy($"\"DownloadHistory\".\"Date\" DESC LIMIT {count}");

            return Query(builder);
        }

        public void DeleteByAuthorId(int authorId)
        {
            Delete(r => r.AuthorId == authorId);
        }
    }
}
