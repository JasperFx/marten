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
///     Internal interface for the Linq subsystem
/// </summary>
public interface ISelectClause: ISqlFragment
{
    string FromObject { get; }

    Type SelectedType { get; }

    string[] SelectFields();

    ISelector BuildSelector(IMartenSession session);
    IQueryHandler<T> BuildHandler<T>(IMartenSession session, ISqlFragment topStatement, ISqlFragment currentStatement);
    ISelectClause UseStatistics(QueryStatistics statistics);
}

