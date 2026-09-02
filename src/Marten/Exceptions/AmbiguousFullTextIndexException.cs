#nullable enable
using System;

namespace Marten.Exceptions;

/// <summary>
///     Thrown when a full text search cannot tell which of a document's full text indexes it should run
///     against.
/// </summary>
/// <remarks>
///     <para>
///         #5315. <c>PlainTextSearch</c> and its siblings take a search term and a text search
///         configuration, and <c>regConfig</c> is the only thing that selects an index. Every full text
///         index shares the default <c>english</c> unless someone deliberately varies it, so on a document
///         carrying two of them there is nothing in the query expressing which was meant.
///     </para>
///     <para>
///         Marten used to resolve that by taking the first match, which meant a document matching only on
///         the other index never came back — no error, and nothing in the generated SQL hinting that a
///         second index had been considered and dropped. Which one won depended on declaration order.
///         Refusing is the honest answer: the information needed to choose is not in the query.
///     </para>
/// </remarks>
public class AmbiguousFullTextIndexException: MartenException
{
    public AmbiguousFullTextIndexException(string message): base(message)
    {
    }

    public AmbiguousFullTextIndexException(string message, Exception innerException): base(message,
        innerException)
    {
    }
}
