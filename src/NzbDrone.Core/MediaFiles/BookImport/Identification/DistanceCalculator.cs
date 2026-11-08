using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public static class DistanceCalculator
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DistanceCalculator));

        public static readonly List<string> VariousAuthorIds = new List<string> { "89ad4ac3-39f7-470e-963a-56509c546377" };

        private static readonly RegexReplace StripSeriesRegex = new RegexReplace(@"\([^\)].+?\)$", string.Empty, RegexOptions.Compiled);

        private static readonly RegexReplace CleanTitleCruft = new RegexReplace(@"\((?:unabridged)\)", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly List<string> EbookFormats = new List<string> { "Kindle Edition", "Nook", "ebook" };

        private static readonly List<string> AudiobookFormats = new List<string> { "Audiobook", "Audio CD", "Audio Cassette", "Audible Audio", "CD-ROM", "MP3 CD" };

        public static Distance BookDistance(List<LocalBook> localTracks, Edition edition)
        {
            var dist = new Distance();

            // the most common list of authors reported by a file
            var fileAuthors = localTracks.Select(x => x.FileTrackInfo.Authors.Where(a => a.IsNotNullOrWhiteSpace()).ToList())
                .GroupBy(x => x.ConcatToString())
                .OrderByDescending(x => x.Count())
                .First()
                .First();

            var authors = GetAuthorVariants(fileAuthors);

            dist.AddString("author", authors, edition.Book.Value.AuthorMetadata.Value.Name);
            Logger.Trace("author: '{0}' vs '{1}'; {2}", authors.ConcatToString("' or '"), edition.Book.Value.AuthorMetadata.Value.Name, dist.NormalizedDistance());

            var title = localTracks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "";
            var titleOptions = new List<string> { edition.Title };
            if (titleOptions[0].Contains("#"))
            {
                titleOptions.Add(StripSeriesRegex.Replace(titleOptions[0]));
            }

            var (maintitle, _) = edition.Title.SplitBookTitle(edition.Book.Value.AuthorMetadata.Value.Name);
            if (!titleOptions.Contains(maintitle))
            {
                titleOptions.Add(maintitle);
            }

            // Audible piles the series and the sequence onto the title: "Dune: House Atreides:
            // House Trilogy, Book 1". A file tagged "Dune: House Atreides" is an exact match for
            // that edition, but against the whole string it scored worse than the sibling "Dune:
            // House Corrino" and was filed there. Offer each leading run of segments beyond the
            // first, the second segment on its own, and the work's own title.
            var segments = edition.Title.Split(':').Select(x => x.Trim()).Where(x => x.IsNotNullOrWhiteSpace()).ToList();

            for (var i = 2; i < segments.Count; i++)
            {
                AddTitleOption(titleOptions, string.Join(": ", segments.Take(i)));
            }

            if (segments.Count > 1)
            {
                AddTitleOption(titleOptions, segments[1]);
            }

            AddTitleOption(titleOptions, edition.Book.Value.Title);

            if (edition.Book.Value.SeriesLinks?.Value?.Any() ?? false)
            {
                foreach (var l in edition.Book.Value.SeriesLinks.Value)
                {
                    if (l.Series?.Value?.Title?.IsNotNullOrWhiteSpace() ?? false)
                    {
                        titleOptions.Add($"{l.Series.Value.Title} {l.Position} {edition.Title}");
                        titleOptions.Add($"{l.Series.Value.Title} Book {l.Position} {edition.Title}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} {l.Position}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} Book {l.Position}");
                    }
                }
            }

            // The folder name is offered alongside the album tag and the best variant wins, the same
            // way author variants are handled. Without it a series-tagged album makes every book in
            // that series score identically.
            var pathTitle = localTracks.MostCommon(x => x.FileTrackInfo.PathBookTitle) ?? "";

            var fileTitles = new[] { title, CleanTitleCruft.Replace(title), pathTitle, CleanTitleCruft.Replace(pathTitle) }
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            // Tags carry subtitles and series the way publishers wrote them: "Dune: House Atreides
            // (Unabridged)", "Martin Luther King Jr., Malcolm X, and the Civil Rights Struggle of
            // the 1950s and 1960s: A Brief History with Documents". Offer the part before the
            // first colon when it is long enough not to be a bare series name, and the part after
            // it, so the title the catalogue knows the book by can match exactly.
            foreach (var fileTitle in fileTitles.ToList())
            {
                var fileSegments = fileTitle.Split(':').Select(x => CleanTitleCruft.Replace(x).Trim()).Where(x => x.IsNotNullOrWhiteSpace()).ToList();

                if (fileSegments.Count < 2)
                {
                    continue;
                }

                if (fileSegments[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3)
                {
                    AddTitleOption(fileTitles, fileSegments[0]);
                }

                if (fileSegments[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                {
                    AddTitleOption(fileTitles, fileSegments[1]);
                }
            }

            dist.AddString("book", fileTitles, titleOptions);
            Logger.Trace("book: '{0}' vs '{1}'; {2}", fileTitles.ConcatToString("' or '"), titleOptions.ConcatToString("' or '"), dist.NormalizedDistance());

            // A matching identifier is near-conclusive and pulls the whole distance down. A
            // differing one is weak evidence: a work has many editions with their own ISBNs and
            // the catalogue only carries one of them, so "Master of the Game" with a 2012 ISBN
            // must still match the work whose synthetic edition carries the 1982 ISBN, rather than
            // losing to an unrelated book that happens to have no ISBN at all.
            var isbn = localTracks.MostCommon(x => x.FileTrackInfo.Isbn);
            if (isbn.IsNotNullOrWhiteSpace() && edition.Isbn13.IsNotNullOrWhiteSpace())
            {
                if (isbn == edition.Isbn13)
                {
                    dist.Add("isbn", 0.0);
                }
                else
                {
                    dist.AddBool("isbn_mismatch", true);
                }

                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }
            else if (isbn.IsNullOrWhiteSpace() != edition.Isbn13.IsNullOrWhiteSpace())
            {
                dist.AddBool("isbn_missing", true);
                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }

            var asin = localTracks.MostCommon(x => x.FileTrackInfo.Asin);
            if (asin.IsNotNullOrWhiteSpace() && edition.Asin.IsNotNullOrWhiteSpace())
            {
                if (asin == edition.Asin)
                {
                    dist.Add("asin", 0.0);
                }
                else
                {
                    dist.AddBool("asin_mismatch", true);
                }

                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }
            else if (asin.IsNullOrWhiteSpace() != edition.Asin.IsNullOrWhiteSpace())
            {
                dist.AddBool("asin_missing", true);
                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }

            // Year
            var localYear = localTracks.MostCommon(x => x.FileTrackInfo.Year);
            if (localYear > 0 && edition.ReleaseDate.HasValue)
            {
                var bookYear = edition.ReleaseDate?.Year ?? 0;
                if (localYear == bookYear)
                {
                    dist.Add("year", 0.0);
                }
                else
                {
                    var remoteYear = bookYear;
                    var diff = Math.Abs(localYear - remoteYear);
                    var diff_max = Math.Abs(DateTime.Now.Year - remoteYear);
                    dist.AddRatio("year", diff, diff_max);
                }

                Logger.Trace($"year: {localYear} vs {edition.ReleaseDate?.Year}; {dist.NormalizedDistance()}");
            }

            // Language - only if set for both the local book and remote edition
            var localLanguage = localTracks.MostCommon(x => x.FileTrackInfo.Language).CanonicalizeLanguage();
            var editionLanguage = edition.Language.CanonicalizeLanguage();
            if (localLanguage.IsNotNullOrWhiteSpace() && editionLanguage.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("language", localLanguage != editionLanguage);
                Logger.Trace($"language: {localLanguage} vs {editionLanguage}; {dist.NormalizedDistance()}");
            }

            // Publisher - only if set for both the local book and remote edition
            var localPublisher = localTracks.MostCommon(x => x.FileTrackInfo.Publisher);
            var editionPublisher = edition.Publisher;
            if (localPublisher.IsNotNullOrWhiteSpace() && editionPublisher.IsNotNullOrWhiteSpace())
            {
                dist.AddString("publisher", localPublisher, editionPublisher);
                Logger.Trace($"publisher: {localPublisher} vs {editionPublisher}; {dist.NormalizedDistance()}");
            }

            // try to tilt it towards the correct "type" of release
            var isAudio = MediaFileExtensions.AudioExtensions.Contains(localTracks.First().Path.GetPathExtension());

            // A book may carry an ebook edition and an audiobook edition with the same title. The
            // format checks below only ever penalised text files landing on audio entries, so an
            // m4b scored identically against both and was filed under the ebook edition - which
            // then left the audiobook edition "missing" and up for download. Weighted below the
            // title, so a book that only has an ebook edition still wins over a different book.
            dist.AddBool("media_type", isAudio == edition.IsEbook);
            Logger.Trace("media type: {0} vs {1}; {2}", isAudio ? "audio" : "text", edition.IsEbook ? "ebook" : "audiobook", dist.NormalizedDistance());

            if (edition.Format.IsNotNullOrWhiteSpace())
            {
                if (!isAudio)
                {
                    // text books should prefer ebook formats
                    dist.AddBool("ebook_format", !EbookFormats.Contains(edition.Format));

                    // text books should not match audio entries
                    dist.AddBool("wrong_format", AudiobookFormats.Contains(edition.Format));
                }
                else
                {
                    // audio books should prefer audio formats
                    dist.AddBool("audio_format", !AudiobookFormats.Contains(edition.Format));
                }
            }

            return dist;
        }

        private static void AddTitleOption(List<string> titleOptions, string title)
        {
            if (title.IsNotNullOrWhiteSpace() && !titleOptions.Contains(title))
            {
                titleOptions.Add(title);
            }
        }

        public static List<string> GetAuthorVariants(List<string> fileAuthors)
        {
            var authors = new List<string>(fileAuthors);

            // Every entry is split, not only a lone one. A co-authored audiobook is tagged
            // "Kevin J Anderson, Brian Herbert" and usually has a second entry as well (the
            // narrator, or a folder name), and the whole list scored 21% away from "Brian Herbert"
            // while a split would have matched exactly. Splitting only adds variants and the
            // distance takes the best of them, so nothing that matched before matches worse.
            foreach (var author in fileAuthors)
            {
                authors.AddRange(SplitAuthor(author).Where(a => !authors.Contains(a)));
            }

            foreach (var author in fileAuthors)
            {
                if (author.Contains(','))
                {
                    var split = author.Split(',', 2).Select(x => x.Trim());
                    if (!split.First().Contains(' '))
                    {
                        authors.Add(split.Reverse().ConcatToString(" "));
                    }
                }
            }

            return authors;
        }

        private static List<string> SplitAuthor(string input)
        {
            var seps = new[] { ';', '/' };
            foreach (var sep in seps)
            {
                if (input.Contains(sep))
                {
                    return input.Split(sep).Select(x => x.Trim()).ToList();
                }
            }

            var andSeps = new List<string> { " and ", " & " };
            foreach (var sep in andSeps)
            {
                if (input.Contains(sep))
                {
                    var result = new List<string>();
                    foreach (var s in input.Split(sep).Select(x => x.Trim()))
                    {
                        var s2 = SplitAuthor(s);
                        if (s2.Any())
                        {
                            result.AddRange(s2);
                        }
                        else
                        {
                            result.Add(s);
                        }
                    }

                    return result;
                }
            }

            if (input.Contains(','))
            {
                var split = input.Split(',').Select(x => x.Trim()).ToList();
                if (split[0].Contains(' '))
                {
                    return split;
                }
            }

            return new List<string>();
        }
    }
}
