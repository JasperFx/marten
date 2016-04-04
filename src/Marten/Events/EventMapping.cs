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
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public abstract class EventMapping : IDocumentMapping
    {
        private readonly StoreOptions _options;
        private readonly EventGraph _parent;
        protected readonly DocumentMapping _inner;

        // TODO -- this logic is duplicated. Centralize in an ext method
        public static string ToEventTypeName(Type eventType)
        {
            return eventType.Name.ToTableAlias();
        }

        protected EventMapping(EventGraph parent, Type eventType)
        {
            _options = parent.Options;
            _parent = parent;
            DocumentType = eventType;

            EventTypeName = Alias = ToEventTypeName(DocumentType);
            IdMember = DocumentType.GetProperty(nameof(IEvent.Id));

            _inner = new DocumentMapping(eventType, parent.Options);
        }

        public Type DocumentType { get; }
        public string EventTypeName { get; set; }
        public string Alias { get; }
        public MemberInfo IdMember { get; }
        public IIdGeneration IdStrategy { get; set; } = new GuidIdGeneration();
        public NpgsqlDbType IdType { get; } = NpgsqlDbType.Uuid;

        public string QualifiedTableName => _options.DatabaseSchemaName + ".mt_events";
        public string TableName => "mt_events";

        public string DatabaseSchemaName
        {
            get { return _options.DatabaseSchemaName; }
            set { throw new NotSupportedException("The DatabaseSchemaName of Event can't be set."); }
        }

        public PropertySearching PropertySearching { get; } = PropertySearching.JSON_Locator_Only;

        public string[] SelectFields()
        {
            return new[] {"id", "data"};
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
        {
            _parent.GenerateSchemaObjectsIfNecessary(autoCreateSchemaObjectsMode, schema, executeSql);
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return _inner.FieldFor(members);
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return new WhereFragment($"d.type = '{EventTypeName}'");
        }

        public abstract IDocumentStorage BuildStorage(IDocumentSchema schema);

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            _parent.WriteSchemaObjects(schema, writer);
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotSupportedException($"Invalid to remove schema objects for {DocumentType}");
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            factory.RunSql($"delete from mt_events where type = '{Alias}'");
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IDocumentMapping other, MemberInfo[] members, Action<TOther> callback) where TOther : class
        {
            return _inner.JoinToInclude<TOther>(joinType, other, members, callback);
        }
    }

    public class EventMapping<T> : EventMapping, IDocumentStorage, IResolver<T> where T : class, IEvent
    {
        public EventMapping(EventGraph parent) : base(parent, typeof(T))
        {

        }

        public override IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            return this;
        }

        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand($"select d.data, d.id from mt_events as d where id = :id and type = '{Alias}'").With("id", id);
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
            return new NpgsqlCommand($"select d.data, d.id from mt_events as d where id = ANY(:ids) and type = '{Alias}'").With("ids", ids);
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

        public T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var id = reader.GetGuid(startingIndex);
            var json = reader.GetString(startingIndex + 1);

            return map.Get<T>(id, json);
        }

        public T Build(DbDataReader reader, ISerializer serializer)
        {
            return serializer.FromJson<T>(reader.GetString(0));
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