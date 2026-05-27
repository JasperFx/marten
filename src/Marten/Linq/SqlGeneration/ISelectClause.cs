#nullable enable
using System;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
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
///     Internal interface for the Linq subsystem
/// </summary>
public interface ISelectClause: ISqlFragment
{
    string FromObject { get; }

    Type SelectedType { get; }

    string[] SelectFields();

    ISelector BuildSelector(IMartenSession session);

    IQueryHandler<T> BuildHandler<T>(IMartenSession session, ISqlFragment topStatement, ISqlFragment currentStatement)
        where T : notnull;
    ISelectClause UseStatistics(QueryStatistics statistics);
}

