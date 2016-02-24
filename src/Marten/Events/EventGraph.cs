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
        private readonly Cache<Type, AggregateModel> _aggregates = new Cache<Type, AggregateModel>();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            throw new NotImplementedException();
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotImplementedException();
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            throw new NotImplementedException();
        }
    }
}