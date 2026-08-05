namespace NzbDrone.Common.Options;

public class MetadataOptions
{
    /// <summary>
    /// Base url of the book metadata service, including the {route} placeholder.
    /// </summary>
    /// <remarks>
    /// Readarr was retired because its own metadata service became unusable, so the built-in
    /// default no longer resolves to anything useful. Making this configurable is what allows a
    /// replacement such as rreading-glasses to be used without patching and rebuilding.
    ///
    /// Set with Readarr__Metadata__Source, or a Metadata/Source entry in config.xml.
    /// </remarks>
    public string Source { get; set; }
}
