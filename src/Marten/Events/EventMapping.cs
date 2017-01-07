using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Remotion.Linq;

namespace Marten.Events
{
    public abstract class EventMapping : IDocumentMapping, IQueryableDocument
    {
        private readonly StoreOptions _options;
        private readonly EventGraph _parent;
        protected readonly DocumentMapping _inner;

        protected EventMapping(EventGraph parent, Type eventType)
        {
            _options = parent.Options;
            _parent = parent;
            DocumentType = eventType;

            EventTypeName = Alias = DocumentType.Name.ToTableAlias();
            IdMember = DocumentType.GetProperty(nameof(IEvent.Id));

            _inner = new DocumentMapping(eventType, parent.Options);
        }

        public Type DocumentType { get; }
        public string EventTypeName { get; set; }
        public string Alias { get; }
        public MemberInfo IdMember { get; }
        public NpgsqlDbType IdType { get; } = NpgsqlDbType.Uuid;

        public TableName Table =>  new TableName(_options.Events.DatabaseSchemaName, "mt_events");
        public DuplicatedField[] DuplicatedFields { get; }
        public DeleteStyle DeleteStyle { get; }

        public string DatabaseSchemaName
        {
            get { return _options.Events.DatabaseSchemaName; }
            set { throw new NotSupportedException("The DatabaseSchemaName of Event can't be set."); }
        }

        public PropertySearching PropertySearching { get; } = PropertySearching.JSON_Locator_Only;

        public string[] SelectFields()
        {
            return new[] {"id", "data"};
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return _inner.FieldFor(members);
        }

        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return new WhereFragment($"d.type = '{EventTypeName}'");
        }

        public abstract IDocumentStorage BuildStorage(IDocumentSchema schema);

        public IDocumentSchemaObjects SchemaObjects => _parent.SchemaObjects;

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            factory.RunSql($"delete from mt_events where type = '{Alias}'");
        }

        public IdAssignment<T> ToIdAssignment<T>(IDocumentSchema schema)
        {
            throw new NotSupportedException();
        }

        public IQueryableDocument ToQueryableDocument()
        {
            return this;
        }

        public IDocumentUpsert BuildUpsert(IDocumentSchema schema)
        {
            throw new NotSupportedException("Please use Events.Append() or Events.StartStream() to add events to the event log");
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members, Action<TOther> callback)
        {
            return _inner.JoinToInclude<TOther>(joinType, other, members, callback);
        }

    }

    public class EventMapping<T> : EventMapping, IDocumentStorage, IResolver<T> where T : class
    {
        private readonly string _tableName;

        public EventMapping(EventGraph parent) : base(parent, typeof(T))
        {
            var schemaName = parent.DatabaseSchemaName;
            _tableName = schemaName == StoreOptions.DefaultDatabaseSchemaName ? "mt_events" : $"{schemaName}.mt_events";
        }

        public override IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            return this;
        }

        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand($"select d.data, d.id from {_tableName} as d where id = :id and type = '{Alias}'").With("id", id);
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            throw new NotSupportedException();
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            throw new NotSupportedException();
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            return new NpgsqlCommand($"select d.data, d.id from {_tableName} as d where id = ANY(:ids) and type = '{Alias}'").With("ids", ids);
        }

        public object Identity(object document)
        {
            return document.As<IEvent>().Id;
        }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            // Do nothing
        }

        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            // Do nothing
        }

        public void Remove(IIdentityMap map, object entity)
        {
            throw new InvalidOperationException("Use IDocumentSession.Events for all persistence of IEvent objects");
        }

        public void Delete(IIdentityMap map, object id)
        {
            throw new InvalidOperationException("Use IDocumentSession.Events for all persistence of IEvent objects");
        }

        public void Store(IIdentityMap map, object id, object entity)
        {
            throw new InvalidOperationException("Use IDocumentSession.Events for all persistence of IEvent objects");
        }

        public IStorageOperation DeletionForId(object id)
        {
            throw new NotSupportedException("You cannot delete events at this time");
        }

        public IStorageOperation DeletionForEntity(object entity)
        {
            throw new NotSupportedException("You cannot delete events at this time");
        }

        public IStorageOperation DeletionForWhere(IWhereFragment @where)
        {
            throw new NotSupportedException("You cannot delete events at this time");
        }

        public T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var id = reader.GetGuid(startingIndex);
            var json = reader.GetString(startingIndex + 1);

            return map.Get<T>(id, json, null);
        }

        public async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var json = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);

            return map.Get<T>(id, json, null);
        }

        public FetchResult<T> Fetch(DbDataReader reader, ISerializer serializer)
        {
            if (!reader.Read()) return null;

            var json = reader.GetString(0);
            var doc = serializer.FromJson<T>(json);

            return new FetchResult<T>(doc, json, null);
        }

        public async Task<FetchResult<T>> FetchAsync(DbDataReader reader, ISerializer serializer, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);

            if (!found) return null;

            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(json);


            return new FetchResult<T>(doc, json, null);
        }

        public T Resolve(IIdentityMap map, ILoader loader, object id)
        {
            return map.Get(id, () => loader.LoadDocument<T>(id));
        }

        public Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id)
        {
            return map.GetAsync(id, tkn => loader.LoadDocumentAsync<T>(id, tkn), token);
        }


    }
}