using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;

namespace Marten.Events
{
    public interface IEventStoreConfiguration
    {
        string DatabaseSchemaName { get; set; }

        void AddEventType(Type eventType);
        void AddEventTypes(IEnumerable<Type> types);

        EventMapping EventMappingFor(Type eventType);
        EventMapping EventMappingFor<T>() where T : class, new();
        IEnumerable<EventMapping> AllEvents();
        IEnumerable<AggregateModel> AllAggregates();
        EventMapping EventMappingFor(string eventType);
        bool IsActive { get; }
        AggregateModel AggregateFor<T>() where T : class, new();
        AggregateModel AggregateFor(Type aggregateType);
        Type AggregateTypeFor(string aggregateTypeName);
    }

    // TODO -- try to eliminate the IDocumentMapping implementation here
    // just making things ugly
    public class EventGraph : IDocumentMapping, IEventStoreConfiguration
    {
        private readonly Cache<string, EventMapping> _byEventName = new Cache<string, EventMapping>();
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();
        private readonly Cache<Type, AggregateModel> _aggregates = new Cache<Type, AggregateModel>(type => new AggregateModel(type));
        private readonly Cache<string, AggregateModel> _aggregateByName = new Cache<string, AggregateModel>();

        private bool _checkedSchema = false;
        private readonly object _locker = new object();
        private string _databaseSchemaName;

        internal StoreOptions Options { get; }

        public EventGraph(StoreOptions options)
        {
            Options = options;
            _events.OnMissing = eventType => typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };

            _aggregateByName.OnMissing = name =>
            {
                return AllAggregates().FirstOrDefault(x => x.Alias == name);
            };
        }

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor<T>() where T : class, new()
        {
            return EventMappingFor(typeof (T));
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _events;
        }

        public IEnumerable<AggregateModel> AllAggregates()
        {
            return _aggregates;
        } 

        public EventMapping EventMappingFor(string eventType)
        {
            return _byEventName[eventType];
        }

        public void AddEventType(Type eventType)
        {
            _events.FillDefault(eventType);
        }

        public void AddEventTypes(IEnumerable<Type> types)
        {
            types.Each(AddEventType);
        }


        public bool IsActive => _events.Any() || _aggregates.Any();

        public string Alias { get; } = null;
        public Type DocumentType { get; } = typeof (EventStream);

        public TableName Table => new TableName(DatabaseSchemaName, "mt_stream");

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? Options.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public PropertySearching PropertySearching { get; } = PropertySearching.JSON_Locator_Only;
        public IIdGeneration IdStrategy { get; set; } = new GuidIdGeneration();
        public MemberInfo IdMember { get; } = ReflectionHelper.GetProperty<EventStream>(x => x.Id);

        string[] IDocumentMapping.SelectFields()
        {
            throw new NotSupportedException();
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
        {
            if (_checkedSchema) return;

            _checkedSchema = true;

            var schemaExists = schema.TableExists(Table);
            if (schemaExists) return;

            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                throw new InvalidOperationException("The EventStore schema objects do not exist and the AutoCreateSchemaObjects is configured as " + autoCreateSchemaObjectsMode);
            }

            lock (_locker)
            {
                if (!schema.TableExists(Table))
                {
                    var writer = new StringWriter();

                    writeBasicTables(schema, writer);

                    executeSql(writer.ToString());


                    // This is going to have to be done separately
                    // TODO -- doesn't work anyway. Do this differently somehow.


                    //var js = SchemaBuilder.GetJavascript("mt_transforms").Replace("'", "\"").Replace("\n", "").Replace("\r", "");
                    //var sql = $"insert into mt_modules (name, definition) values ('mt_transforms', '{js}');";
                    //executeSql(sql);

                    //executeSql("select mt_initialize_projections();");
                }
            }

        }

        private void writeBasicTables(IDocumentSchema schema, StringWriter writer)
        {
            EnsureDatabaseSchema.WriteSql(DatabaseSchemaName, writer);

            writer.WriteSql(DatabaseSchemaName, "mt_stream");
            writer.WriteSql(DatabaseSchemaName, "mt_initialize_projections");
            writer.WriteSql(DatabaseSchemaName, "mt_apply_transform");
            writer.WriteSql(DatabaseSchemaName, "mt_apply_aggregation");
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            throw new NotSupportedException();
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            throw new NotSupportedException();
        }

        public IWhereFragment DefaultWhereFragment()
        {
            throw new NotSupportedException();
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            return new EventStreamStorage(this);
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            writeBasicTables(schema, writer);
           
            // TODO -- need to load the projection and initialize
        }

        void IDocumentMapping.RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotImplementedException();
        }

        void IDocumentMapping.DeleteAllDocuments(IConnectionFactory factory)
        {
            throw new NotImplementedException();
        }

        IncludeJoin<TOther> IDocumentMapping.JoinToInclude<TOther>(JoinType joinType, IDocumentMapping other, MemberInfo[] members, Action<TOther> callback)
        {
            throw new NotSupportedException();
        }

        public AggregateModel AggregateFor<T>() where T : class, new()
        {
            return AggregateFor(typeof (T));
        }

        public AggregateModel AggregateFor(Type aggregateType)
        {
            return _aggregates[aggregateType];
        }


        public Type AggregateTypeFor(string aggregateTypeName)
        {
            return _aggregateByName[aggregateTypeName].AggregateType;
        }


    }
}