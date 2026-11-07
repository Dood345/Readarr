using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// A concrete metadata backend. Implementations are selected by <see cref="MetadataProviderType"/>
    /// and reached through <see cref="BookMetadataProxy"/>.
    /// </summary>
    /// <remarks>
    /// Implementations must NOT also implement IProvideAuthorInfo/IProvideBookInfo/ISearchForNew*
    /// directly. Composition registers every interface as a singleton with a single default, so a
    /// second implementation of those makes resolution ambiguous and the container throws at
    /// startup. BookMetadataProxy is the only registration for them.
    /// </remarks>
    public interface IBookMetadataProvider
    {
        MetadataProviderType ProviderType { get; }

        Author GetAuthorInfo(string readarrId, bool useCache = true);
        HashSet<string> GetChangedAuthors(DateTime startTime);
        Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id);
        List<Author> SearchForNewAuthor(string title);
        List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true);
        List<Book> SearchByIsbn(string isbn);
        List<Book> SearchByAsin(string asin);
        List<Book> SearchByGoodreadsBookId(int goodreadsId, bool getAllEditions);
        List<object> SearchForNewEntity(string title);
    }
}
