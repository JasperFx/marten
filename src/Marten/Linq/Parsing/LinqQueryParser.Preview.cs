#nullable enable
using Weasel.Postgresql;

namespace Marten.Linq.Parsing;

internal partial class LinqQueryParser
{
    /// <summary>
    ///     The single value mode that executing a query with <paramref name="fetchType" /> parses it with.
    ///     <see cref="FetchType.Count" /> is <c>CountAsync()</c> (<c>LongCountAsync()</c> sends the same SQL),
    ///     and <see cref="FetchType.FetchOne" /> is <c>FirstAsync()</c> / <c>FirstOrDefaultAsync()</c>.
    /// </summary>
    public static SingleValueMode? ValueModeFor(FetchType fetchType)
    {
        return fetchType switch
        {
            FetchType.Count => SingleValueMode.Count,
            FetchType.Any => SingleValueMode.Any,
            FetchType.FetchOne => SingleValueMode.First,
            _ => null
        };
    }

    /// <summary>
    ///     Renders the SQL that executing this query sends. Build the parser with
    ///     <see cref="ValueModeFor" /> of the fetch type being previewed.
    /// </summary>
    /// <remarks>
    ///     #5916: this used to build the statements without a single value mode and then turn the main selector
    ///     into a count, an existence check or a one row read itself. Execution does not work that way: it
    ///     parses the query with the single value mode and lets <see cref="BuildStatements" /> apply it, which
    ///     wraps a join (or a Distinct(), or a GroupBy()) in a CTE before counting it, drops the ordering from a
    ///     count and keeps a Take() under a First(). The two had drifted apart for every one of those shapes, so
    ///     the preview now builds the same statements execution does. Every query handler renders exactly those
    ///     statements from their top, which is all that is applied here.
    /// </remarks>
    public void BuildDiagnosticCommand(CommandBuilder sql)
    {
        BuildStatements().Top.Apply(sql);
    }
}
