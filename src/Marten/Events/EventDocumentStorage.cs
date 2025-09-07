using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events.Archiving;
using Marten.Events.Operations;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Services;
using Marten.Storage;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events;
// NOTE!!!! This type has to remain public for the code generation to work

/// <summary>
///     Base type for the IEventStorage type that provides all the read/write operation
///     mapping for the event store in a running system. The actual implementation of this
///     base type is generated and compiled at runtime by Marten
/// </summary>
public abstract class EventDocumentStorage: IEventStorage
{
    private readonly ISqlFragment _defaultWhere;
    private readonly string[] _fields;
    private readonly EventQueryMapping _mapping;
    private readonly string _selectClause;
    private readonly ISerializer _serializer;

    public EventDocumentStorage(StoreOptions options)
    {
        Events = options.EventGraph;
        _mapping = new EventQueryMapping(options);

        FromObject = _mapping.TableName.QualifiedName;

        _serializer = options.Serializer();

        IdType = Events.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);

        TenancyStyle = options.Events.TenancyStyle;

        // The json data column has to go first
        var table = new EventsTable(Events);
        var columns = table.SelectColumns();

        _fields = columns.Select(x => x.Name).ToArray();

        _selectClause = $"select {_fields.Join(", ")} from {Events.DatabaseSchemaName}.mt_events as d";

        _defaultWhere = Events.TenancyStyle == TenancyStyle.Conjoined
            ? CompoundWhereFragment.And(IsNotArchivedFilter.Instance, CurrentTenantFilter.Instance)
            : IsNotArchivedFilter.Instance;
    }

    public IQueryableMemberCollection QueryMembers => _mapping.QueryMembers;
    public ISelectClause SelectClauseWithDuplicatedFields => this;
    public bool UseNumericRevisions { get; } = false;
    public object RawIdentityValue(object id)
    {
        return id;
    }

    public EventGraph Events { get; }

    public Task TruncateDocumentStorageAsync(IMartenDatabase database, CancellationToken ct = default)
    {
        return database.RunSqlAsync($"truncate table {Events.DatabaseSchemaName}.mt_streams cascade", ct: ct);
    }

    public TenancyStyle TenancyStyle { get; }

    public IDeletion DeleteForDocument(IEvent document, string tenant)
    {
        throw new NotSupportedException();
    }

    public void EjectById(IMartenSession session, object id)
    {
        // Nothing
    }

    public void RemoveDirtyTracker(IMartenSession session, object id)
    {
        // Nothing
    }

    public IDeletion HardDeleteForDocument(IEvent document, string tenantId)
    {
        throw new NotSupportedException();
    }

    public void SetIdentityFromString(IEvent document, string identityString)
    {
        throw new NotImplementedException();
    }

    public void SetIdentityFromGuid(IEvent document, Guid identityGuid)
    {
        throw new NotImplementedException();
    }

    public string FromObject { get; }
    public Type SelectedType => typeof(IEvent);

    public void Apply(ICommandBuilder sql)
    {
        sql.Append(_selectClause);
    }

    public string[] SelectFields()
    {
        return _fields;
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return this;
    }

    public IQueryHandler<T> BuildHandler<T>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where T : notnull
    {
        return LinqQueryParser.BuildHandler<IEvent, T>(this, topStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        throw new NotSupportedException();
    }

    public Type SourceType => typeof(IEvent);

    public ISqlFragment FilterDocuments(ISqlFragment query, IMartenSession session)
    {
        var shouldBeTenanted = Events.TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant();
        if (shouldBeTenanted)
        {
            query = query.CombineAnd(CurrentTenantFilter.Instance);
        }

        return query.SpecifiesEventArchivalStatus()
            ? query
            : query.CombineAnd(IsNotArchivedFilter.Instance);
    }

    public ISqlFragment DefaultWhereFragment()
    {
        return _defaultWhere;
    }

    public bool UseOptimisticConcurrency { get; } = false;
    public IOperationFragment DeleteFragment => throw new NotSupportedException();
    public IOperationFragment HardDeleteFragment => throw new NotSupportedException();
    public IReadOnlyList<DuplicatedField> DuplicatedFields { get; } = Array.Empty<DuplicatedField>();
    public DbObjectName TableName => _mapping.TableName;
    public Type DocumentType => typeof(IEvent);

    public object IdentityFor(IEvent document)
    {
        return (Events.StreamIdentity == StreamIdentity.AsGuid ? document.Id : document.StreamKey)!;
    }

    public Type IdType { get; }

    public Guid? VersionFor(IEvent document, IMartenSession session)
    {
        return null;
    }

    public void Store(IMartenSession session, IEvent document)
    {
        // Nothing
    }

    public void Store(IMartenSession session, IEvent document, Guid? version)
    {
        // Nothing
    }

    public void Store(IMartenSession session, IEvent document, int revision)
    {
        // Nothing
    }

    public void Eject(IMartenSession session, IEvent document)
    {
        // Nothing
    }

    public IStorageOperation Update(IEvent document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation Insert(IEvent document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation Upsert(IEvent document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation Overwrite(IEvent document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public abstract IStorageOperation AppendEvent(EventGraph events, IMartenSession session, StreamAction stream,
        IEvent e);

    public abstract IStorageOperation InsertStream(StreamAction stream);
    public abstract IQueryHandler<StreamState> QueryForStream(StreamAction stream);
    public abstract IStorageOperation UpdateStreamVersion(StreamAction stream);
    public IStorageOperation IncrementStreamVersion(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? new IncrementStreamVersionById(Events, stream)
            : new IncrementStreamVersionByKey(Events, stream);
    }

    public IEvent Resolve(DbDataReader reader)
    {
        var eventTypeName = reader.GetString(1);
        var mapping = Events.EventMappingFor(eventTypeName);
        if (mapping == null)
        {
            var dotnetTypeName = reader.GetFieldValue<string>(2);

            mapping = eventMappingForDotNetTypeName(dotnetTypeName, eventTypeName);
        }

        var @event = mapping.ReadEventData(_serializer, reader);

        ApplyReaderDataToEvent(reader, @event);

        return @event;
    }

    public async Task<IEvent> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var eventTypeName = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);
        var mapping = Events.EventMappingFor(eventTypeName);
        if (mapping == null)
        {
            var dotnetTypeName = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);

            mapping = eventMappingForDotNetTypeName(dotnetTypeName, eventTypeName);
        }

        IEvent @event;
        try
        {
            @event = await mapping.ReadEventDataAsync(_serializer, reader, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            var sequence = await reader.GetFieldValueAsync<long>(3, token).ConfigureAwait(false);
            throw new EventDeserializationFailureException(sequence, mapping, e);
        }

        await ApplyReaderDataToEventAsync(reader, @event, token).ConfigureAwait(false);

        return @event;
    }

    public abstract void ApplyReaderDataToEvent(DbDataReader reader, IEvent e);

    public abstract Task ApplyReaderDataToEventAsync(DbDataReader reader, IEvent e, CancellationToken token);

    private EventMapping eventMappingForDotNetTypeName(string dotnetTypeName, string eventTypeName)
    {
        if (dotnetTypeName.IsEmpty())
        {
            throw new UnknownEventTypeException(eventTypeName);
        }

        Type type;
        try
        {
            type = Events.TypeForDotNetName(dotnetTypeName);
        }
        catch (ArgumentNullException)
        {
            throw new UnknownEventTypeException(dotnetTypeName);
        }

        return Events.EventMappingFor(type);
    }

    public virtual IStorageOperation
        QuickAppendEventWithVersion(StreamAction stream, IEvent e)
    {
        throw new NotSupportedException(
            "You will have to re-generate the Marten code before the \"quick append events\" feature is available");
    }

    public virtual IStorageOperation QuickAppendEvents(StreamAction stream)
    {
        throw new NotSupportedException(
            "You will have to re-generate the Marten code before the \"quick append events\" feature is available");
    }
}
