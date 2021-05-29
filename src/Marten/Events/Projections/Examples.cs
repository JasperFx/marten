using System;
using System.Collections.Generic;

namespace Marten.Events.Projections
{
    public class SomeAggregate{}

    public class SomeEvent1{}
    public class SomeEvent2{}
    public class SomeEvent3
    {
        public Guid IdToDelete { get; set; }
    }
    public class SomeEvent4{}
    public class SomeEvent5{}

    public class SomeDocument1
    {
        public Guid Id { get; set; }
    }
    public class SomeDocument2{}
    public class SomeDocument3{}

    public class DocumentAttribute: Attribute
    {
        public Type DocumentType { get; }

        public DocumentAttribute(Type documentType)
        {
            DocumentType = documentType;
        }
    }



    // All the signatures could take in IQuerySession, or Event<T>
    public class SomeDocument1Projector
    {
        public SomeDocument1 Create(SomeEvent1 @event1)
        {
            return new SomeDocument1();
        }

        public SomeDocument2 Create(IEvent<SomeEvent2> @event)
        {
            return new SomeDocument2();
        }



        // Gotta help out the code by telling it what the document
        // is to be deleted
        [Document(typeof(SomeDocument2))]
        public Guid DeleteOn(SomeEvent3 @event)
        {
            return @event.IdToDelete;
        }

        // Could also take in Event<SomeEvent4>
        [Document(typeof(SomeDocument3))]
        public bool MaybeDelete(SomeEvent4 @event, out Guid id)
        {
            // Optional deletion
            throw new NotSupportedException();
        }

        // @wastaz's scenario. Could also be an IEnumerable
        // Create a bunch
        public SomeDocument3[] CreateMany(SomeEvent4 @event)
        {
            throw new NotSupportedException();
        }

        [Document(typeof(SomeDocument3))]
        public IEnumerable<Guid> DeleteMany(SomeEvent5 @event)
        {
            throw new NotSupportedException();
        }


    }
}
