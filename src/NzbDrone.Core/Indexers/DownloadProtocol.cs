namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Marker interface for a download protocol. Protocols are identified by their type name as a
    /// string - nameof(TorrentDownloadProtocol) - rather than by an enum value, so adding one is a
    /// matter of adding a class instead of editing every site that switched on the enum.
    /// </summary>
    /// <remarks>
    /// Implementations are discovered by composition, so a new protocol needs no registration.
    /// This replaced the original `enum DownloadProtocol { Unknown, Usenet, Torrent }`, which is
    /// what made adding Soulseek tractable in the sibling Lidarr fork.
    /// </remarks>
    public interface IDownloadProtocol
    {
    }

    public class UsenetDownloadProtocol : IDownloadProtocol
    {
    }

    public class TorrentDownloadProtocol : IDownloadProtocol
    {
    }
}
