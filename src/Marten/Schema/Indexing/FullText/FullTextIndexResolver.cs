#nullable enable
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Util;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Schema.Indexing.FullText;

/// <summary>
///     Resolves which full text index a search runs against, and the <c>tsvector</c> expression to search
///     or rank over.
/// </summary>
/// <remarks>
///     Shared by the <c>@@</c> filter and the <c>ts_rank</c> ordering, and that sharing is the point
///     rather than a convenience. A rank computed over a different vector than the one the filter matched
///     on is <em>silently wrong</em> — it returns rows in an order that looks plausible and means nothing
///     — where a mismatch in the filter at least returns visibly wrong rows. Having one resolver is what
///     makes the two incapable of disagreeing.
/// </remarks>
internal static class FullTextIndexResolver
{
    /// <summary>
    ///     The full text index this search runs against, or null when the document has none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         #5315. This used to be <c>FirstOrDefault</c> over the indexes matching a regConfig, and
    ///         every full text index shares the default <c>english</c> unless someone deliberately varies
    ///         it — so on a document with two of them the filter searched one and silently ignored the
    ///         other. A document matching only on the second simply never came back, with no error and
    ///         nothing in the SQL to suggest a second index had been considered and dropped. Which one won
    ///         was a function of declaration order.
    ///     </para>
    ///     <para>
    ///         Ambiguity is refused rather than resolved arbitrarily. The query API has exactly one
    ///         selector, <c>regConfig</c>, so when it does not narrow to a single index there is no answer
    ///         better than an error.
    ///     </para>
    /// </remarks>
    public static FullTextIndexDefinition? FindIndex(DocumentMapping? mapping, string regConfig)
    {
        if (mapping == null)
        {
            return null;
        }

        var candidates = mapping
            .Indexes
            .OfType<FullTextIndexDefinition>()
            .Where(i => i.RegConfig == regConfig)
            .ToArray();

        if (candidates.Length > 1)
        {
            var names = candidates.Select(x => x.Name).Join(", ");
            throw new AmbiguousFullTextIndexException(
                $"Document type {mapping.DocumentType.FullNameInCode()} has {candidates.Length} full text indexes registered for the text search configuration '{regConfig}' ({names}), so Marten cannot tell which one this search means. "
                + "Give the indexes different regConfig values and pass the one you want as the search's regConfig argument, or register a single index covering every member you need to search.");
        }

        return candidates.FirstOrDefault();
    }

    /// <summary>
    ///     The <c>tsvector</c> expression to search or rank over, aliased for the query.
    /// </summary>
    /// <remarks>
    ///     A weighted index (#5298) is built over an expression that is ALREADY a tsvector, so it cannot
    ///     be reconstructed by wrapping a data config in <c>to_tsvector</c> the way the flat case is.
    ///     Weasel exposes <c>IndexedTsVector</c> as the one property both its DDL and this read, precisely
    ///     so the indexed vector and the searched vector cannot drift apart (weasel#541).
    ///     <para>
    ///     The unweighted shape is byte-for-byte what it has always been. Changing it would change the SQL
    ///     every existing full-text query emits, for no gain.
    ///     </para>
    /// </remarks>
    public static string ResolveVector(DocumentMapping? mapping, string regConfig)
    {
        var index = FindIndex(mapping, regConfig);

        if (index?.TsVectorExpression != null)
        {
            return index.IndexedTsVector.ApplyTableAliasToDataColumn("d");
        }

        var dataConfig = (index?.DocumentConfig ?? FullTextIndexDefinition.DataDocumentConfig)
            .ApplyTableAliasToDataColumn("d");

        return $"to_tsvector('{regConfig}'::regconfig, {dataConfig})";
    }
}
