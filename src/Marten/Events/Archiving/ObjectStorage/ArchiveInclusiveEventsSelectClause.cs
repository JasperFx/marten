#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Archiving.ObjectStorage;

/// <summary>
///     A storage whose FROM depends on the filters of the query, so that it can be decided after the
///     where clause is parsed.
/// </summary>
internal interface ISelectClauseDependsOnFilters
{
    ISelectClause SelectClauseFor(IReadOnlyList<ISqlFragment> wheres);
}

/// <summary>
///     Reads events from <see cref="ArchiveInclusiveEventsView" /> instead of <c>mt_events</c>, with the
///     same columns and the same selector.
/// </summary>
internal sealed class ArchiveInclusiveEventsSelectClause: ISelectClause
{
    private readonly EventDocumentStorage _storage;
    private readonly string _sql;

    public ArchiveInclusiveEventsSelectClause(EventDocumentStorage storage)
    {
        _storage = storage;
        FromObject = $"{storage.Events.DatabaseSchemaName}.{ArchiveInclusiveEventsView.ViewName}";
        _sql = $"select {storage.SelectFields().Join(", ")} from {FromObject} as d";
    }

    public static bool IsNeededFor(IReadOnlyList<ISqlFragment> wheres) =>
        wheres.Any(x => x.ContainsAny<AllEventsFilter>() || x.ContainsAny<IsArchivedFilter>());

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
