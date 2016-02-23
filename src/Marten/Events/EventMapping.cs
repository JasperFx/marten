using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public class EventMapping : IDocumentMapping, IDocumentStorage
    {

        public static string ToEventTypeName(Type eventType)
        {
            return eventType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public EventMapping(Type eventType)
        {
            DocumentType = eventType;

            if (!eventType.CanBeCastTo<IEvent>())
                throw new ArgumentOutOfRangeException(nameof(eventType),
                    $"Only types implementing {typeof (IEvent)} can be accepted");


            EventTypeName = Alias = ToEventTypeName(eventType);

            IdMember = eventType.GetProperty(nameof(IEvent.Id));

            _inner = new DocumentMapping(eventType);
        }

        public string EventTypeName { get; set; }

        public string Alias { get; }
        public Type DocumentType { get; }
        public NpgsqlDbType IdType { get; } = NpgsqlDbType.Uuid;
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

        public string TableName { get; } = "mt_events";
        public PropertySearching PropertySearching { get; } = PropertySearching.JSON_Locator_Only;
        public IIdGeneration IdStrategy { get; } = new GuidIdGeneration();
        public MemberInfo IdMember { get; }

        public string SelectFields(string tableAlias)
        {
            return $"{tableAlias}.id, {tableAlias}.data";
        }

        public bool ShouldRegenerate(IDocumentSchema schema)
        {
            throw new NotImplementedException("Need to do this!");
        }

        private readonly DocumentMapping _inner;

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

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            return this;
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            throw new NotImplementedException();
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotSupportedException($"Invalid to remove schema objects for {DocumentType}");
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            factory.RunSql($"delete from mt_events where type = '{Alias}'");
        }
    }


}