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
using Marten.Storage;
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

        public IDocumentMapping Root => this;
        public Type DocumentType { get; }
        public string EventTypeName { get; set; }
        public string Alias { get; }
        public MemberInfo IdMember { get; }
        public NpgsqlDbType IdType { get; } = NpgsqlDbType.Uuid;
        public TenancyStyle TenancyStyle { get; } = TenancyStyle.Single;

        Type IDocumentMapping.IdType => typeof(Guid);

        public DbObjectName Table =>  new DbObjectName(_options.Events.DatabaseSchemaName, "mt_events");
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

        public abstract IDocumentStorage BuildStorage(StoreOptions options);

        public void DeleteAllDocuments(ITenant factory)
        {
            factory.RunSql($"delete from mt_events where type = '{Alias}'");
        }

        public IdAssignment<T> ToIdAssignment<T>(ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public IQueryableDocument ToQueryableDocument()
        {
            return this;
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members, Action<TOther> callback)
        {
            return _inner.JoinToInclude<TOther>(joinType, other, members, callback);
        }

    }

    public class EventMapping<T> : EventMapping, IDocumentStorage<T> where T : class
    {
        private readonly string _tableName;

        public EventMapping(EventGraph parent) : base(parent, typeof(T))
        {
            var schemaName = parent.DatabaseSchemaName;
            _tableName = schemaName == StoreOptions.DefaultDatabaseSchemaName ? "mt_events" : $"{schemaName}.mt_events";
        }

        public override IDocumentStorage BuildStorage(StoreOptions options)
        {
            return this;
        }

        public Type TopLevelBaseType => DocumentType;

        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand($"select d.data, d.id from {_tableName} as d where id = :id and type = '{Alias}'").With("id", id);
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            return new NpgsqlCommand($"select d.data, d.id from {_tableName} as d where id = ANY(:ids) and type = '{Alias}'").With("ids", ids);
        }

        public object Identity(object document)
        {
            return document.As<IEvent>().Id;
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity)
        {
            // Do nothing
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, string json)
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
            var json = reader.GetTextReader(startingIndex + 1);

            return map.Get<T>(id, json, null);
        }

        public async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<Guid>(startingIndex, token).ConfigureAwait(false);

            var json = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(startingIndex + 1).ConfigureAwait(false);

            return map.Get<T>(id, json, null);
        }

        public T Resolve(IIdentityMap map, IQuerySession session, object id)
        {
            if (map.Has<T>(id)) return map.Retrieve<T>(id);

            var cmd = LoaderCommand(id);
            cmd.Connection = session.Connection;
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read()) return null;

                var json = reader.GetTextReader(0);
                var doc = session.Serializer.FromJson<T>(json);
                map.Store(id, doc);

                return doc;
            }
        }

        public async Task<T> ResolveAsync(IIdentityMap map, IQuerySession session, CancellationToken token, object id)
        {
            if (map.Has<T>(id)) return map.Retrieve<T>(id);

            var cmd = LoaderCommand(id);
            cmd.Connection = session.Connection;

            using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                var found = await reader.ReadAsync(token).ConfigureAwait(false);

                if (!found) return null;

                var json = reader.GetTextReader(0);
                //var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                var doc = session.Serializer.FromJson<T>(json);
                map.Store(id, doc);

                return doc;
            }
        }


    }
}