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
using Marten.Internal.Storage;
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

    public void EjectById(IStorageSession session, object id)
    {
        // Nothing
    }

    public void RemoveDirtyTracker(IStorageSession session, object id)
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

    public ISelector BuildSelector(IStorageSession session)
    {
        return this;
    }

    public IQueryHandler<T> BuildHandler<T>(IStorageSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where T : notnull
    {
        return LinqQueryParser.BuildHandler<IEvent, T>(this, topStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        throw new NotSupportedException();
    }

    public Type SourceType => typeof(IEvent);

    public ISqlFragment FilterDocuments(ISqlFragment query, IStorageSession session)
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
    public IReadOnlyList<IDuplicatedField> DuplicatedFields { get; } = Array.Empty<IDuplicatedField>();
    public DbObjectName TableName => _mapping.TableName;
    public Type DocumentType => typeof(IEvent);

    public object IdentityFor(IEvent document)
    {
        return (Events.StreamIdentity == StreamIdentity.AsGuid ? document.Id : document.StreamKey)!;
    }

    public Type IdType { get; }

    public Guid? VersionFor(IEvent document, IStorageSession session)
    {
        return null;
    }

    public void Store(IStorageSession session, IEvent document)
    {
        // Nothing
    }

    public void Store(IStorageSession session, IEvent document, Guid? version)
    {
        // Nothing
    }

    public void Store(IStorageSession session, IEvent document, long revision)
    {
        // Nothing
    }

    public void Eject(IStorageSession session, IEvent document)
    {
        // Nothing
    }

    public IStorageOperation Update(IEvent document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation Insert(IEvent document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation Upsert(IEvent document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation Overwrite(IEvent document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation OverwriteProjected(IEvent document, string tenant)
    {
        throw new NotSupportedException();
    }

    // #4667 — events aren't projected through the document write path.
    public IStorageOperation UpsertProjected(IEvent document, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation InsertProjected(IEvent document, string tenant)
    {
        throw new NotSupportedException();
    }

    public IStorageOperation UpdateProjected(IEvent document, string tenant)
    {
        throw new NotSupportedException();
    }

    public abstract IStorageOperation AppendEvent(EventGraph events, IStorageSession session, StreamAction stream,
        IEvent e);

    public abstract IStorageOperation InsertStream(StreamAction stream);
    public abstract IQueryHandler<StreamState> QueryForStream(StreamAction stream);
    public abstract IStorageOperation UpdateStreamVersion(StreamAction stream);
    public abstract IStorageOperation AssertStreamVersion(StreamAction stream);

    public string StreamStateSelectSql => Marten.EventStorage.StreamStateSql.Build(Events);

    StreamState ISelector<StreamState>.Resolve(DbDataReader reader)
    {
        var state = new StreamState();
        if (Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            state.Id = reader.GetFieldValue<Guid>(0);
        }
        else
        {
            state.Key = reader.GetFieldValue<string>(0);
        }

        state.Version = reader.GetFieldValue<long>(1);

        if (!reader.IsDBNull(2))
        {
            var typeName = reader.GetFieldValue<string>(2);
            if (typeName.IsNotEmpty())
            {
                state.AggregateType = Events.AggregateTypeFor(typeName);
            }
        }

        state.LastTimestamp = reader.GetFieldValue<DateTimeOffset>(3);
        state.Created = reader.GetFieldValue<DateTimeOffset>(4);
        state.IsArchived = reader.GetFieldValue<bool>(5);

        return state;
    }

    async Task<StreamState> ISelector<StreamState>.ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var state = new StreamState();
        if (Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            state.Id = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
        }
        else
        {
            state.Key = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
        }

        state.Version = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);

        if (!await reader.IsDBNullAsync(2, token).ConfigureAwait(false))
        {
            var typeName = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);
            if (typeName.IsNotEmpty())
            {
                state.AggregateType = Events.AggregateTypeFor(typeName);
            }
        }

        state.LastTimestamp = await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false);
        state.Created = await reader.GetFieldValueAsync<DateTimeOffset>(4, token).ConfigureAwait(false);
        state.IsArchived = await reader.GetFieldValueAsync<bool>(5, token).ConfigureAwait(false);

        return state;
    }
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
        // #4680: an upcaster mapping is the authoritative interpretation of the stored
        // event-type name (it was registered with that name as its SOURCE). Skip the
        // dotnet_type alt-mapping swap or a typed Append of TOld in the same store would
        // shadow the upcaster on read. The swap remains for the original case it was added
        // for: same EventTypeName, multiple CLR types, NO upcaster registered.
        else if (!mapping.IsUpcastTarget && !reader.IsDBNull(2))
        {
            var dotnetTypeName = reader.GetFieldValue<string>(2);
            if (!string.IsNullOrEmpty(dotnetTypeName) && dotnetTypeName != mapping.DotNetTypeName)
            {
                var altMapping = Events.TryGetRegisteredMappingForDotNetTypeName(dotnetTypeName);
                if (altMapping != null)
                {
                    mapping = altMapping;
                }
            }
        }

        // #4515: per-row JSON-vs-binary dispatch. EventsTable pins bdata at
        // column ordinal 3 right after data/type/mt_dotnet_type. bdata IS NULL
        // means JSON-serialized event (existing path); non-null bytes mean
        // binary-serialized — deserialize via the mapping's registered
        // IEventBinarySerializer.
        IEvent @event;
        if (!reader.IsDBNull(3))
        {
            if (mapping.BinarySerializer is null)
            {
                throw new InvalidOperationException(
                    $"Event row at mt_events.bdata is non-null but no IEventBinarySerializer is registered " +
                    $"for type '{mapping.DocumentType.FullName}'. Configure with " +
                    $"opts.Events.UseBinarySerializer<{mapping.DocumentType.Name}>(...) " +
                    $"or set opts.Events.DefaultBinarySerializer.");
            }

            var bytes = reader.GetFieldValue<byte[]>(3);
            var data = mapping.BinarySerializer.Deserialize(mapping.DocumentType, bytes);
            @event = mapping.Wrap(data);
        }
        else
        {
            @event = mapping.ReadEventData(_serializer, reader);
        }

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
        // #4680: see the sync Resolve overload above -- upcaster mappings are authoritative
        // for their source event-type name and the dotnet_type alt-mapping swap would shadow
        // them when the source type is appended (typed) into the same store.
        else if (!mapping.IsUpcastTarget && !await reader.IsDBNullAsync(2, token).ConfigureAwait(false))
        {
            var dotnetTypeName = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(dotnetTypeName) && dotnetTypeName != mapping.DotNetTypeName)
            {
                var altMapping = Events.TryGetRegisteredMappingForDotNetTypeName(dotnetTypeName);
                if (altMapping != null)
                {
                    mapping = altMapping;
                }
            }
        }

        IEvent @event;
        try
        {
            // #4515: same per-row dispatch as the sync Resolve overload — bdata
            // at ordinal 3 picks JSON-vs-binary deserialization.
            if (!await reader.IsDBNullAsync(3, token).ConfigureAwait(false))
            {
                if (mapping.BinarySerializer is null)
                {
                    throw new InvalidOperationException(
                        $"Event row at mt_events.bdata is non-null but no IEventBinarySerializer is registered " +
                        $"for type '{mapping.DocumentType.FullName}'. Configure with " +
                        $"opts.Events.UseBinarySerializer<{mapping.DocumentType.Name}>(...) " +
                        $"or set opts.Events.DefaultBinarySerializer.");
                }

                var bytes = await reader.GetFieldValueAsync<byte[]>(3, token).ConfigureAwait(false);
                var data = mapping.BinarySerializer.Deserialize(mapping.DocumentType, bytes);
                @event = mapping.Wrap(data);
            }
            else
            {
                @event = await mapping.ReadEventDataAsync(_serializer, reader, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            // #4515: mt_events.seq_id shifted from ordinal 3 to ordinal 4 after
            // bdata's insertion (EventsTable.SelectColumns now pins data, type,
            // mt_dotnet_type, bdata, seq_id at 0..4).
            long sequence;
            try
            {
                sequence = await reader.GetFieldValueAsync<long>(4, token).ConfigureAwait(false);
            }
            catch
            {
                sequence = -1;
            }
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
