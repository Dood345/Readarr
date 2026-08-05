namespace NzbDrone.Core.MetadataSource
{
    public enum MetadataProviderType
    {
        /// <summary>
        /// Open Library for author identity, bibliography and editions, enriched with Audible for
        /// audiobook editions, narrators and series sequence. Runs in-process and needs no sidecar.
        /// </summary>
        OpenLibrary = 0
    }
}
