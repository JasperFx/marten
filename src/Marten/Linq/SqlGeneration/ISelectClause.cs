#nullable enable
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

public interface IModifyableFromObject
{
    string FromObject { get; set; }
}

/// <summary>
/// Implemented by select clauses that can emit a PostgreSQL
/// <c>SELECT DISTINCT ON (expression)</c> prefix, used to translate the LINQ
/// <c>DistinctBy(keySelector)</c> operator. See #4565.
/// </summary>
internal interface IDistinctOnSelectClause
{
    /// <summary>
    /// Raw SQL of the DISTINCT ON key expression (for example <c>d.data ->> 'Name'</c>),
    /// or null when no DistinctBy() is applied.
    /// </summary>
    string? DistinctOn { get; set; }
}

/// <summary>
///     Internal interface for the Linq subsystem. #4821: the dialect-neutral subset
///     (FromObject / SelectedType / SelectFields / BuildSelector) moved to
///     <see cref="Weasel.Storage.ISelectClause"/>; this Marten interface derives from it and
///     keeps the Postgres/LINQ-typed members (query handlers, statistics). Implementors'
///     existing members satisfy the neutral base slots unchanged; their Postgres
///     <c>Apply(ICommandBuilder)</c> satisfies the neutral fragment via the dialect DIM.
/// </summary>
public interface ISelectClause: Weasel.Storage.ISelectClause, ISqlFragment
{
    IQueryHandler<T> BuildHandler<T>(IStorageSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement)
        where T : notnull;

    ISelectClause UseStatistics(QueryStatistics statistics);
}
