using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten.Events.Archiving;
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
using Marten.Schema;
using Marten.Services;
using Marten.Services.Json.Transformations;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using static JasperFx.Events.EventTypeExtensions;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events;

[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
public abstract class EventMapping: EventTypeData, IDocumentMapping, IEventType
{
    protected readonly DocumentMapping _inner;
    protected readonly EventGraph _parent;
    private readonly ISqlFragment _defaultWhereFragment;

    protected EventMapping(EventGraph parent, Type eventType) : base(eventType)
    {
        TenancyStyle = parent.TenancyStyle;

        EventTypeName = eventType.GetEventTypeName(parent.EventNamingStyle);

        _parent = parent;
        DocumentType = eventType;

        // #4515: pick up binary-serializer wiring at construction. Either an
        // explicit per-type registration via UseBinarySerializer<T>(...) or
        // the BinaryEventAttribute + store-wide DefaultBinarySerializer.
        // Null = plain JSON-serialized event (the existing path).
        BinarySerializer = parent.ResolveBinarySerializerFor(eventType);

        IdMember = DocumentType.GetProperty(nameof(IEvent.Id))!;

        _inner = new DocumentMapping(eventType, parent.Options);

        ISqlFragment filter = new WhereFragment($"d.type = '{EventTypeName}'");
        filter = filter.CombineAnd(IsNotArchivedFilter.Instance);
        if (parent.TenancyStyle == TenancyStyle.Conjoined)
        {
            filter = filter.CombineAnd(CurrentTenantFilter.Instance);
        }

        _defaultWhereFragment = filter;

        JsonTransformation(null);
    }

    /// <summary>
    ///     #4515: binary serializer for this event type, or <c>null</c> for the
    ///     standard JSON path. When non-null, write operations route the payload
    ///     into <c>mt_events.bdata</c> (bytea); read operations dispatch on the
    ///     row's <c>bdata IS NULL</c> state so JSON-serialized rows for the same
    ///     event type still read correctly.
    /// </summary>
    [IgnoreDescription]
    public IEventBinarySerializer? BinarySerializer { get; internal set; }

    /// <summary>
    /// #4680: true when this <see cref="EventMapping"/> was created by
    /// <c>EventGraph.Upcast(...)</c>. The mapping's <see cref="DocumentType"/> is the
    /// upcast TARGET type (TNew); the <see cref="EventTypeName"/> still belongs to the
    /// SOURCE event-type-name (so on-disk events written under that name continue to
    /// resolve here). The resolver in <c>EventDocumentStorage.Resolve/ResolveAsync</c>
    /// uses this flag to skip the <c>dotnet_type</c>-driven alt-mapping swap; otherwise
    /// a typed Append of TOld into the same store would register a separate
    /// EventMapping&lt;TOld&gt; and the alt-mapping swap would shadow the upcaster on
    /// read.
    /// </summary>
    [IgnoreDescription]
    internal bool IsUpcastTarget { get; set; }

    /// <summary>
    ///     #4515: <c>true</c> when this event type is opted in to binary
    ///     serialization on the write path (a <see cref="BinarySerializer"/> is
    ///     wired). Read-path dispatch remains row-by-row on <c>bdata</c>'s NULL
    ///     state so pre-opt-in JSON rows continue to read through the JSON path.
    /// </summary>
    public bool IsBinary => BinarySerializer is not null;

    [IgnoreDescription]
    public Func<ISerializer, DbDataReader, IEvent> ReadEventData { get; private set; }

    [IgnoreDescription]
    public Func<ISerializer, DbDataReader, CancellationToken, Task<IEvent>> ReadEventDataAsync { get; private set; }

    [IgnoreDescription]
    public NpgsqlDbType IdType { get; } = NpgsqlDbType.Uuid;
    public TenancyStyle TenancyStyle { get; } = TenancyStyle.Single;
    public IReadOnlyList<DuplicatedField> DuplicatedFields { get; }
    public DeleteStyle DeleteStyle { get; }
    public bool UseVersionFromMatchingStream { get; set; }

    public PropertySearching PropertySearching { get; } = PropertySearching.JSON_Locator_Only;

    IDocumentMapping IDocumentMapping.Root => this;
    public Type DocumentType { get; }

    [IgnoreDescription]
    public MemberInfo IdMember { get; }

    Type IDocumentMapping.IdType => typeof(Guid);

    public DbObjectName TableName => new PostgresqlObjectName(_parent.DatabaseSchemaName, "mt_events");

    Type IEventType.EventType => DocumentType;




    public string[] SelectFields()
    {
        return new[] { "id", "data" };
    }

    public Weasel.Core.SqlGeneration.ISqlFragment FilterDocuments(Weasel.Core.SqlGeneration.ISqlFragment query, IStorageSession martenSession)
    {
        var pgQuery = (ISqlFragment)query;
        var extras = extraFilters(pgQuery).ToList();

        return pgQuery.CombineAnd(extras);
    }

    private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query)
    {
        yield return _defaultWhereFragment;
        if (!query.SpecifiesEventArchivalStatus())
        {
            yield return IsNotArchivedFilter.Instance;
        }

        var shouldBeTenanted = _parent.TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant();
        if (shouldBeTenanted)
        {
            yield return CurrentTenantFilter.Instance;
        }
    }

    public Weasel.Core.SqlGeneration.ISqlFragment DefaultWhereFragment()
    {
        return _defaultWhereFragment;
    }

    public abstract IEvent Wrap(object data);

    /// <summary>
    ///     <para>
    ///         Defines the event JSON payload transformation. It transforms one event schema into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By calling it, you tell that instead of the old CLR type, for the specific event type name,
    ///         you'd like to get the new CLR event type.
    ///         Provided functions take the deserialized object of the old event type and returns the new, mapped one.
    ///     </para>
    /// </summary>
    /// <param name="jsonTransformation">Json transfromation</param>
    public void JsonTransformation(JsonTransformation? jsonTransformation)
    {
        ReadEventData =
            jsonTransformation == null
                ? (serializer, reader) =>
                {
                    var data = serializer.FromJson(DocumentType, reader, 0);

                    return Wrap(data);
                }
                : (serializer, reader) =>
                {
                    var data = jsonTransformation.FromDbDataReader(serializer, reader, 0);

                    return Wrap(data);
                }
            ;

        ReadEventDataAsync = jsonTransformation == null
            ? async (serializer, reader, token) =>
            {
                var data = await serializer.FromJsonAsync(DocumentType, reader, 0, token)
                    .ConfigureAwait(false);

                return Wrap(data);
            }
            : async (serializer, reader, token) =>
            {
                var data = await jsonTransformation.FromDbDataReaderAsync(serializer, reader, 0, token)
                    .ConfigureAwait(false);

                return Wrap(data);
            };
    }
}

public class EventMapping<T>: EventMapping, IDocumentStorage<T>, ILinqDocumentStorage where T : class
{
    private readonly string _tableName;
    private readonly Type _idType;

    public EventMapping(EventGraph parent): base(parent, typeof(T))
    {
        var schemaName = parent.DatabaseSchemaName;
        _tableName = schemaName == SchemaConstants.DefaultSchema ? "mt_events" : $"{schemaName}.mt_events";

        _idType = parent.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);

        var members = new DocumentQueryableMemberCollection(this, parent.Options);
        members.RemoveAnyIdentityMember();

        QueryMembers = members;

    }

    [IgnoreDescription]
    public IQueryableMemberCollection QueryMembers { get; }

    // The public DuplicatedFields (IReadOnlyList<DuplicatedField>) satisfies IDocumentMapping; the
    // db-neutral IDocumentStorage surface sees it through the IDuplicatedField view (covariant).
    IReadOnlyList<IDuplicatedField> IDocumentStorage.DuplicatedFields => DuplicatedFields;

    [IgnoreDescription]
    public ISelectClause SelectClauseWithDuplicatedFields => this;
    public bool UseNumericRevisions { get; } = false;
    public object RawIdentityValue(object id)
    {
        return id;
    }

    public Task TruncateDocumentStorageAsync(IStorageDatabase database, CancellationToken ct = default)
    {
        return database.RunSqlAsync($"delete from table {_tableName} where type = '{Alias}'", ct: ct);
    }

    public bool UseOptimisticConcurrency { get; } = false;

    [IgnoreDescription]
    public IOperationFragment DeleteFragment => throw new NotSupportedException();

    [IgnoreDescription]
    public IOperationFragment HardDeleteFragment { get; }

    [IgnoreDescription]
    string Weasel.Storage.ISelectClause.FromObject => _tableName;

    Type Weasel.Storage.ISelectClause.SelectedType => typeof(T);

    void ISqlFragment.Apply(ICommandBuilder sql)
    {
        sql.Append("select data from ");
        sql.Append(_tableName);
        sql.Append(" as d");
    }

    ISelector Weasel.Storage.ISelectClause.BuildSelector(IStorageSession session)
    {
        return new EventSelector<T>((ISerializer)session.Serializer);
    }

    IQueryHandler<TResult> ISelectClause.BuildHandler<TResult>(IStorageSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement)
    {
        var selector = new EventSelector<T>((ISerializer)session.Serializer);

        return LinqQueryParser.BuildHandler<T, TResult>(selector, topStatement);
    }

    ISelectClause ISelectClause.UseStatistics(QueryStatistics statistics)
    {
        throw new NotSupportedException();
    }

    Type IDocumentStorage.SourceType => typeof(IEvent);

    object IDocumentStorage<T>.IdentityFor(T document)
    {
        throw new NotSupportedException();
    }

    Type IDocumentStorage.IdType => _idType;

    Guid? IDocumentStorage<T>.VersionFor(T document, IStorageSession session)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.Store(IStorageSession session, T document)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.Store(IStorageSession session, T document, Guid? version)
    {
        throw new NotSupportedException();
    }

    public void Store(IStorageSession session, T document, long revision)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.Eject(IStorageSession session, T document)
    {
        throw new NotSupportedException();
    }

    Weasel.Storage.IStorageOperation IDocumentStorage<T>.Update(T document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    Weasel.Storage.IStorageOperation IDocumentStorage<T>.Insert(T document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    Weasel.Storage.IStorageOperation IDocumentStorage<T>.Upsert(T document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    Weasel.Storage.IStorageOperation IDocumentStorage<T>.Overwrite(T document, IStorageSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    Weasel.Storage.IStorageOperation IDocumentStorage<T>.OverwriteProjected(T document, string tenant)
    {
        throw new NotSupportedException();
    }

    // #4667 — events aren't projected through the document write path.
    Weasel.Storage.IStorageOperation IDocumentStorage<T>.UpsertProjected(T document, string tenant)
    {
        throw new NotSupportedException();
    }

    Weasel.Storage.IStorageOperation IDocumentStorage<T>.InsertProjected(T document, string tenant)
    {
        throw new NotSupportedException();
    }

    Weasel.Storage.IStorageOperation IDocumentStorage<T>.UpdateProjected(T document, string tenant)
    {
        throw new NotSupportedException();
    }

    public IDeletion DeleteForDocument(T document, string tenant)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.EjectById(IStorageSession session, object id)
    {
        // Nothing
    }

    void IDocumentStorage<T>.RemoveDirtyTracker(IStorageSession session, object id)
    {
        // Nothing
    }

    public IDeletion HardDeleteForDocument(T document, string tenantId)
    {
        throw new NotSupportedException();
    }

    public void SetIdentityFromString(T document, string identityString)
    {
        throw new NotImplementedException();
    }

    public void SetIdentityFromGuid(T document, Guid identityGuid)
    {
        throw new NotImplementedException();
    }

    public override IEvent Wrap(object data)
    {
        return new Event<T>((T)data) { EventTypeName = EventTypeName, DotNetTypeName = DotNetTypeName };
    }

    internal class EventSelector<TEvent>: ISelector<TEvent>
    {
        private readonly ISerializer _serializer;

        public EventSelector(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public TEvent Resolve(DbDataReader reader)
        {
            return _serializer.FromJson<TEvent>(reader, 0);
        }

        public async Task<TEvent> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            var doc = await _serializer.FromJsonAsync<TEvent>(reader, 0, token).ConfigureAwait(false);

            return doc;
        }
    }
}
