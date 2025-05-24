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

namespace Marten.Events;

public abstract class EventMapping: EventTypeData, IDocumentMapping, IEventType
{
    protected readonly DocumentMapping _inner;
    protected readonly EventGraph _parent;
    private readonly ISqlFragment _defaultWhereFragment;

    protected EventMapping(EventGraph parent, Type eventType) : base(eventType)
    {
        TenancyStyle = parent.TenancyStyle;

        _parent = parent;
        DocumentType = eventType;

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

    public ISqlFragment FilterDocuments(ISqlFragment query, IMartenSession martenSession)
    {
        var extras = extraFilters(query).ToList();

        return query.CombineAnd(extras);
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

    public ISqlFragment DefaultWhereFragment()
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

public class EventMapping<T>: EventMapping, IDocumentStorage<T> where T : class
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

    [IgnoreDescription]
    public ISelectClause SelectClauseWithDuplicatedFields => this;
    public bool UseNumericRevisions { get; } = false;
    public object RawIdentityValue(object id)
    {
        return id;
    }

    public Task TruncateDocumentStorageAsync(IMartenDatabase database, CancellationToken ct = default)
    {
        return database.RunSqlAsync($"delete from table {_tableName} where type = '{Alias}'", ct: ct);
    }

    public bool UseOptimisticConcurrency { get; } = false;

    [IgnoreDescription]
    public IOperationFragment DeleteFragment => throw new NotSupportedException();

    [IgnoreDescription]
    public IOperationFragment HardDeleteFragment { get; }

    [IgnoreDescription]
    string ISelectClause.FromObject => _tableName;

    Type ISelectClause.SelectedType => typeof(T);

    void ISqlFragment.Apply(ICommandBuilder sql)
    {
        sql.Append("select data from ");
        sql.Append(_tableName);
        sql.Append(" as d");
    }

    ISelector ISelectClause.BuildSelector(IMartenSession session)
    {
        return new EventSelector<T>(session.Serializer);
    }

    IQueryHandler<TResult> ISelectClause.BuildHandler<TResult>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement)
    {
        var selector = new EventSelector<T>(session.Serializer);

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

    Guid? IDocumentStorage<T>.VersionFor(T document, IMartenSession session)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.Store(IMartenSession session, T document)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.Store(IMartenSession session, T document, Guid? version)
    {
        throw new NotSupportedException();
    }

    public void Store(IMartenSession session, T document, int revision)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.Eject(IMartenSession session, T document)
    {
        throw new NotSupportedException();
    }

    IStorageOperation IDocumentStorage<T>.Update(T document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    IStorageOperation IDocumentStorage<T>.Insert(T document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    IStorageOperation IDocumentStorage<T>.Upsert(T document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    IStorageOperation IDocumentStorage<T>.Overwrite(T document, IMartenSession session, string tenant)
    {
        throw new NotSupportedException();
    }

    public IDeletion DeleteForDocument(T document, string tenant)
    {
        throw new NotSupportedException();
    }

    void IDocumentStorage<T>.EjectById(IMartenSession session, object id)
    {
        // Nothing
    }

    void IDocumentStorage<T>.RemoveDirtyTracker(IMartenSession session, object id)
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
