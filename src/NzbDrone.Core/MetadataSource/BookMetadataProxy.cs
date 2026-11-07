using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// The single registration for the metadata interfaces. Dispatches to whichever
    /// <see cref="IBookMetadataProvider"/> the MetadataProvider config setting selects.
    /// </summary>
    public class BookMetadataProxy : IProvideAuthorInfo, IProvideBookInfo, ISearchForNewBook, ISearchForNewAuthor, ISearchForNewEntity
    {
        private readonly IEnumerable<IBookMetadataProvider> _providers;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public BookMetadataProxy(IEnumerable<IBookMetadataProvider> providers,
            IConfigService configService,
            Logger logger)
        {
            _providers = providers;
            _configService = configService;
            _logger = logger;
        }

        private IBookMetadataProvider Provider
        {
            get
            {
                var configured = _configService.MetadataProvider;
                var provider = _providers.FirstOrDefault(x => x.ProviderType == configured);

                if (provider != null)
                {
                    return provider;
                }

                // Only reachable if a provider type is removed from the build while still set in
                // the database. Falling back beats refusing to start.
                var fallback = _providers.FirstOrDefault();

                if (fallback == null)
                {
                    throw new InvalidOperationException("No book metadata provider is registered");
                }

                _logger.Warn("Metadata provider {0} is not available, falling back to {1}", configured, fallback.ProviderType);

                return fallback;
            }
        }

        public Author GetAuthorInfo(string readarrId, bool useCache = true) => Provider.GetAuthorInfo(readarrId, useCache);

        public HashSet<string> GetChangedAuthors(DateTime startTime) => Provider.GetChangedAuthors(startTime);

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id) => Provider.GetBookInfo(id);

        public List<Author> SearchForNewAuthor(string title) => Provider.SearchForNewAuthor(title);

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true) => Provider.SearchForNewBook(title, author, getAllEditions);

        public List<Book> SearchByIsbn(string isbn) => Provider.SearchByIsbn(isbn);

        public List<Book> SearchByAsin(string asin) => Provider.SearchByAsin(asin);

        public List<Book> SearchByGoodreadsBookId(int goodreadsId, bool getAllEditions) => Provider.SearchByGoodreadsBookId(goodreadsId, getAllEditions);

        public List<object> SearchForNewEntity(string title) => Provider.SearchForNewEntity(title);
    }
}
