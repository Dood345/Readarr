using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators
{
    /// <summary>
    /// Fills in the author and book title from the folder structure when the file's tags do not
    /// carry them.
    /// </summary>
    /// <remarks>
    /// Audiobooks are frequently untaggable in practice. A chapter-per-file rip often has no tags
    /// at all - every file in a 219 file Hobbit can be blank - and identification then has nothing
    /// to compare candidates against, so it cannot tell "The Fellowship of the Ring" from the
    /// "The Lord of the Rings" omnibus even when both are offered.
    ///
    /// In a library laid out as &lt;root&gt;/&lt;Author&gt;/&lt;Book&gt;/files the path carries
    /// exactly the two facts that are missing. Only empty fields are filled, so a file that does
    /// have real tags keeps them.
    /// </remarks>
    public class AggregatePathInfo : IAggregate<LocalBook>
    {
        /// <summary>
        /// Matches the "&lt;Series&gt; &lt;position&gt; - &lt;Title&gt;" folder convention, which this
        /// library uses throughout ("Dune 0.1 - The Butlerian Jihad"). A digit before the separator
        /// is what distinguishes it from a title that merely contains a dash.
        /// </summary>
        private static readonly Regex SeriesPrefixRegex = new Regex(@"^.*\d[\d.]*\s*-\s*(?<title>.+)$", RegexOptions.Compiled);

        public LocalBook Aggregate(LocalBook localTrack, bool otherFiles)
        {
            if (localTrack.Path.IsNullOrWhiteSpace())
            {
                return localTrack;
            }

            localTrack.FileTrackInfo ??= new ParsedTrackInfo();

            var bookFolder = Path.GetDirectoryName(localTrack.Path);
            var authorFolder = bookFolder.IsNotNullOrWhiteSpace() ? Path.GetDirectoryName(bookFolder) : null;

            if (localTrack.FileTrackInfo.BookTitle.IsNullOrWhiteSpace())
            {
                var name = Path.GetFileName(bookFolder);

                if (name.IsNotNullOrWhiteSpace())
                {
                    var match = SeriesPrefixRegex.Match(name);
                    localTrack.FileTrackInfo.BookTitle = match.Success ? match.Groups["title"].Value.Trim() : name;
                }
            }

            // The author folder is *added* to the list rather than only filling an empty one.
            // Audiobooks routinely tag the narrator as the artist - this library's Harry Potter set
            // reads "Jim Dale" - and distance scoring takes the best matching variant, so offering
            // the folder alongside the tag can only help. Filling only when empty left those files
            // scoring 60% against an 80% threshold and rejected despite matching the right book.
            var authorName = Path.GetFileName(authorFolder);

            if (authorName.IsNotNullOrWhiteSpace())
            {
                localTrack.FileTrackInfo.Authors ??= new List<string>();

                if (!localTrack.FileTrackInfo.Authors.Contains(authorName, StringComparer.InvariantCultureIgnoreCase))
                {
                    localTrack.FileTrackInfo.Authors.Add(authorName);
                }
            }

            return localTrack;
        }
    }
}
