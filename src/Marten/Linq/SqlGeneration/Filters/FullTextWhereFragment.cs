#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Marten.Linq.Parsing.Methods.FullText;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Linq.SqlGeneration.Filters;

internal class FullTextWhereFragment: ISqlFragment
{
    // PostgreSQL text-search configuration names are stored as identifiers in
    // pg_ts_config (see https://www.postgresql.org/docs/current/textsearch-configuration.html).
    // We allow simple unquoted identifiers — optionally schema-qualified — so values
    // like "english", "french", or "pg_catalog.english" pass through, while anything
    // containing whitespace, quotes, semicolons, or other punctuation is rejected.
    // This is a security-critical check: regConfig is interpolated into SQL by Sql below,
    // so any value that escapes this pattern would be a SQL injection sink.
    private static readonly Regex _regConfigPattern = new(
        @"^[a-zA-Z_][a-zA-Z0-9_]{0,62}(\.[a-zA-Z_][a-zA-Z0-9_]{0,62})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _vector;
    private readonly string _regConfig;
    private readonly FullTextSearchFunction _searchFunction;
    private readonly string _searchTerm;

    public FullTextWhereFragment(DocumentMapping? mapping, FullTextSearchFunction searchFunction, string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig)
    {
        ValidateRegConfig(regConfig);

        _regConfig = regConfig;

        _vector = ResolveVector(mapping, regConfig);
        _searchFunction = searchFunction;
        _searchTerm = searchTerm;
    }

    /// <summary>
    ///     The <c>tsvector</c> this filter searches, which must be the same one the index was built over.
    /// </summary>
    /// <remarks>
    ///     A weighted index (#5298) is built over an expression that is ALREADY a tsvector, so it cannot
    ///     be reconstructed by wrapping a data config in <c>to_tsvector</c> the way the flat case is.
    ///     Weasel exposes <c>IndexedTsVector</c> as the one property both its DDL and this filter read,
    ///     precisely so the indexed vector and the searched vector cannot drift apart (weasel#541).
    ///     <para>
    ///     The unweighted shape is left byte-for-byte as it was. Changing it would change the SQL every
    ///     existing full-text query emits, for no gain.
    ///     </para>
    /// </remarks>
    private static string ResolveVector(DocumentMapping? mapping, string regConfig)
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

    private static void ValidateRegConfig(string regConfig)
    {
        if (regConfig is null)
        {
            throw new ArgumentNullException(nameof(regConfig));
        }

        if (!_regConfigPattern.IsMatch(regConfig))
        {
            throw new ArgumentException(
                $"Invalid PostgreSQL text-search configuration name '{regConfig}'. " +
                "regConfig must be a simple PostgreSQL identifier (optionally schema-qualified), " +
                "matching ^[a-zA-Z_][a-zA-Z0-9_]*(\\.[a-zA-Z_][a-zA-Z0-9_]*)?$.",
                nameof(regConfig));
        }
    }

    // don't parameterize full-text search config as it ruins the performance with the query plan in PG
    private string Sql => $"{_vector} @@ {_searchFunction}('{_regConfig}'::regconfig, ?)";

    public void Apply(ICommandBuilder builder)
    {
        builder.AppendWithParameters(Sql)[0].Value = _searchTerm;
    }

    /// <summary>
    ///     The full text index this search runs against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         #5315. This used to be <c>FirstOrDefault</c> over the indexes matching a regConfig, and every
    ///         full text index shares the default <c>english</c> unless someone deliberately varies it — so
    ///         on a document with two of them the filter searched one and silently ignored the other.
    ///         A document matching only on the second index simply never came back, with no error and
    ///         nothing in the SQL to suggest a second index had been considered and dropped. Which one won
    ///         was a function of declaration order.
    ///     </para>
    ///     <para>
    ///         Ambiguity is now refused rather than resolved arbitrarily. The query API has exactly one
    ///         selector, <c>regConfig</c>, so when it does not narrow to a single index there is no answer
    ///         this method could give that is better than an error — and ranking (#5298) makes the choice
    ///         load-bearing rather than merely untidy, because a <c>ts_rank</c> over a different vector than
    ///         the <c>@@</c> filtered on is silently wrong rather than slow.
    ///     </para>
    /// </remarks>
    private static FullTextIndexDefinition? FindIndex(DocumentMapping? mapping, string regConfig)
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
}
