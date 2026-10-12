#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.ColdStorage;

/// <summary>
///     A storage whose FROM depends on the filters of the query, decided after the where clause is parsed.
/// </summary>
internal interface ISelectClauseDependsOnFilters
{
    ISelectClause SelectClauseFor(IReadOnlyList<ISqlFragment> wheres);
}

internal sealed class MaybeColdFilter: ISqlFragment
{
    public static readonly MaybeColdFilter Instance = new();

    public void Apply(ICommandBuilder builder) => builder.Append("1 = 1");
}

internal sealed class MaybeColdParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(ColdStorageExtensions).GetMethod(nameof(ColdStorageExtensions.MaybeCold))!;

    public bool Matches(MethodCallExpression expression) => expression.Method == _method;

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression) => MaybeColdFilter.Instance;
}

/// <summary>
///     Reads events from <c>mt_events_with_cold</c> instead of <c>mt_events</c>, with the same columns
///     and the same selector.
/// </summary>
internal sealed class ColdInclusiveEventsSelectClause: ISelectClause
{
    private readonly EventDocumentStorage _storage;
    private readonly string _sql;

    public ColdInclusiveEventsSelectClause(EventDocumentStorage storage, string fromObject)
    {
        _storage = storage;
        FromObject = fromObject;
        _sql = $"select {storage.SelectFields().Join(", ")} from {FromObject} as d";
    }

    public static bool IsNeededFor(IReadOnlyList<ISqlFragment> wheres) =>
        wheres.Any(x => x.ContainsAny<AllEventsFilter>() || x.ContainsAny<IsArchivedFilter>() || x.ContainsAny<MaybeColdFilter>());

    public string FromObject { get; }

    public Type SelectedType => typeof(IEvent);

    public void Apply(ICommandBuilder sql) => sql.Append(_sql);

    public string[] SelectFields() => _storage.SelectFields();

    public ISelector BuildSelector(IStorageSession session) => _storage.BuildSelector(session);

    public IQueryHandler<T> BuildHandler<T>(IStorageSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where T : notnull
    {
        return LinqQueryParser.BuildHandler<IEvent, T>((ISelector<IEvent>)_storage, topStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics) => throw new NotSupportedException();
}
