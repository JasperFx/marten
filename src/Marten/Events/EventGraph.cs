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

namespace Marten.Events
{
    public class EventGraph : IDocumentMapping
    {

        private readonly Cache<string, EventMapping> _byEventName = new Cache<string, EventMapping>();
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();
        private readonly Cache<Type, AggregateModel> _aggregates = new Cache<Type, AggregateModel>(type => new AggregateModel(type));
        private readonly Cache<string, AggregateModel> _aggregateByName = new Cache<string, AggregateModel>();  

        public EventGraph()
        {
            _events.OnMissing = eventType => new EventMapping(eventType);

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };
        }

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor<T>() where T : IEvent
        {
            return EventMappingFor(typeof (T));
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _events;
        }

        public EventMapping EventMappingFor(string eventType)
        {
            return _byEventName[eventType];
        }


        public void AddEventType(Type eventType)
        {
            if (!eventType.IsConcreteTypeOf<IEvent>())
            {
                throw new ArgumentOutOfRangeException(nameof(eventType), "Event types must be concrete types implementing the IEvent interface");    
            }

            

            _events.FillDefault(eventType);
        }

        public string Alias { get; } = null;
        public Type DocumentType { get; } = typeof (EventStream);
        public string TableName { get; } = "mt_stream";
        public PropertySearching PropertySearching { get; } = PropertySearching.JSON_Locator_Only;
        public IIdGeneration IdStrategy { get; } = new GuidIdGeneration();
        public MemberInfo IdMember { get; } = ReflectionHelper.GetProperty<EventStream>(x => x.Id);
        public string SelectFields(string tableAlias)
        {
            throw new NotImplementedException();
        }

        public bool ShouldRegenerate(IDocumentSchema schema)
        {
            return !schema.DocumentTables().Contains("mt_streams");
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            throw new NotImplementedException();
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            throw new NotImplementedException();
        }

        public IWhereFragment DefaultWhereFragment()
        {
            throw new NotImplementedException();
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            return new EventStreamStorage(this);
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            // TODO -- will need to do something to add the JS module for mt_transforms
            // maybe doing it by replacing all instances of ' with " and building the sql directly?
            // See EventStoreAdmin.RebuildEventStoreSchema()

            writer.WriteSql("mt_stream");
            writer.WriteSql("mt_initialize_projections");
            writer.WriteSql("mt_apply_transform");
            writer.WriteSql("mt_apply_aggregation");


            //writer.WriteLine("COMMIT;");
            //writer.WriteLine("");


            // This is going to have to be done separately
            //var js = SchemaBuilder.GetJavascript("mt_transforms").Replace("'", "\"").Replace("\n", "").Replace("\r", "");

            //writer.WriteLine($"insert into mt_modules (name, definition) values ('mt_transforms', '{js}');");

            //writer.WriteLine();

            //writer.WriteLine("select mt_initialize_projections();");
            

        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotImplementedException();
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            throw new NotImplementedException();
        }

        public AggregateModel AggregateFor<T>() where T : IAggregate
        {
            return AggregateFor(typeof (T));
        }

        public AggregateModel AggregateFor(Type aggregateType)
        {
            return _aggregates[aggregateType];
        }

        public void AddAggregateType<T>() where T : IAggregate
        {
            _aggregates.FillDefault(typeof(T));
        }

        public void AddAggregateType(Type aggregateType)
        {
            _aggregates.FillDefault(aggregateType);
        }
        
    }
}