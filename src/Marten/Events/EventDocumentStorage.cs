using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Archiving;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Remotion.Linq;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events
{
    // NOTE!!!! This type has to remain public for the code generation to work

    /// <summary>
    /// Base type for the IEventStorage type that provides all the read/write operation
    /// mapping for the event store in a running system. The actual implementation of this
    /// base type is generated and compiled at runtime by Marten
    /// </summary>
    public abstract class EventDocumentStorage : IEventStorage
    {
        private readonly EventQueryMapping _mapping;
        private readonly ISerializer _serializer;
        private readonly string[] _fields;
        private readonly string _selectClause;
        private readonly ISqlFragment _defaultWhere;

        public EventDocumentStorage(StoreOptions options)
        {
            Events = options.EventGraph;
            _mapping = new EventQueryMapping(options);

            FromObject = _mapping.TableName.QualifiedName;
            Fields = _mapping;

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

        public void TruncateDocumentStorage(ITenant tenant)
        {
            tenant.RunSql($"truncate table {Events.DatabaseSchemaName}.mt_streams cascade");
        }

        public Task TruncateDocumentStorageAsync(ITenant tenant)
        {
            return tenant.RunSqlAsync($"truncate table {Events.DatabaseSchemaName}.mt_streams cascade");
        }

        public EventGraph Events { get; }

        public TenancyStyle TenancyStyle { get; }

        public IDeletion DeleteForDocument(IEvent document, ITenant tenant)
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

        public IDeletion HardDeleteForDocument(IEvent document, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public string FromObject { get; }
        public Type SelectedType => typeof(IEvent);
        public void WriteSelectClause(CommandBuilder sql)
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

        public IQueryHandler<T> BuildHandler<T>(IMartenSession session, Statement topStatement, Statement currentStatement)
        {
            return LinqHandlerBuilder.BuildHandler<IEvent, T>(this, topStatement);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            throw new NotSupportedException();
        }

        public Type SourceType => typeof(IEvent);
        public IFieldMapping Fields { get; }
        public ISqlFragment FilterDocuments(QueryModel model, ISqlFragment query)
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
        public IOperationFragment HardDeleteFragment { get; }
        public DuplicatedField[] DuplicatedFields { get; } = new DuplicatedField[0];
        public DbObjectName TableName => _mapping.TableName;
        public Type DocumentType => typeof(IEvent);

        public object IdentityFor(IEvent document)
        {
            return Events.StreamIdentity == StreamIdentity.AsGuid ? (object) document.Id : document.StreamKey;
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

        public void Eject(IMartenSession session, IEvent document)
        {
            // Nothing
        }

        public IStorageOperation Update(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public IStorageOperation Insert(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public IStorageOperation Upsert(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public IStorageOperation Overwrite(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public abstract IStorageOperation AppendEvent(EventGraph events, IMartenSession session, StreamAction stream, IEvent e);
        public abstract IStorageOperation InsertStream(StreamAction stream);
        public abstract IQueryHandler<StreamState> QueryForStream(StreamAction stream);
        public abstract IStorageOperation UpdateStreamVersion(StreamAction stream);

        public IEvent Resolve(DbDataReader reader)
        {
            var eventTypeName = reader.GetString(1);
            var mapping = Events.EventMappingFor(eventTypeName);
            if (mapping == null)
            {
                var dotnetTypeName = reader.GetFieldValue<string>(2);
                if (dotnetTypeName.IsEmpty())
                {
                    throw new UnknownEventTypeException(eventTypeName);
                }

                var type = Events.TypeForDotNetName(dotnetTypeName);
                mapping = Events.EventMappingFor(type);
            }

            var data = _serializer.FromJson(mapping.DocumentType, reader, 0).As<object>();

            var @event = mapping.Wrap(data);

            ApplyReaderDataToEvent(reader, @event);

            return @event;
        }

        public abstract void ApplyReaderDataToEvent(DbDataReader reader, IEvent e);

        public async Task<IEvent> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            var eventTypeName = await reader.GetFieldValueAsync<string>(1, token);
            var mapping = Events.EventMappingFor(eventTypeName);
            if (mapping == null)
            {
                var dotnetTypeName = await reader.GetFieldValueAsync<string>(2, token);
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
                mapping = Events.EventMappingFor(type);
            }

            var data = await _serializer.FromJsonAsync(mapping.DocumentType, reader, 0, token);

            var @event = mapping.Wrap(data);

            await ApplyReaderDataToEventAsync(reader, @event, token);

            return @event;
        }

        public abstract Task ApplyReaderDataToEventAsync(DbDataReader reader, IEvent e, CancellationToken token);


    }
}
