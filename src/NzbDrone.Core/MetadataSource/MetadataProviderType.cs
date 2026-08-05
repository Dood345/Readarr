namespace NzbDrone.Core.MetadataSource
{
    public enum MetadataProviderType
    {
        /// <summary>
        /// Open Library for author identity, bibliography and editions, enriched with Audible for
        /// audiobook editions, narrators and series sequence. Runs in-process and needs no sidecar.
        /// </summary>
        OpenLibrary = 0,

        /// <summary>
        /// The original bookinfo.club schema. That service is dead, so this only works when
        /// MetadataSource points at a compatible replacement such as rreading-glasses.
        /// </summary>
        BookInfo = 1
    }
}
