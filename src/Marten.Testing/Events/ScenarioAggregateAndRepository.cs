using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Marten.Events;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Events
{
    public class ScenarioAggregateAndRepository : DocumentSessionFixture<NulloIdentityMap>
    {
        public ScenarioAggregateAndRepository()
        {
            StoreOptions(options =>
            {
                options.Events.StreamIdentity = StreamIdentity.AsString;
            });
        }

        [Fact]
        public void CanStoreAndHydrateAggregate()
        {
            var invoice = CreateInvoice();

            // SAMPLE: scenario-aggregate-storeandreadinvoice
            var repository = new AggregateRepository(theStore);
            
            repository.Store(invoice);

            var invoiceFromRepository = repository.Load<Invoice>(invoice.Id);
            
            Assert.Equal(invoice.ToString(), invoiceFromRepository.ToString());           
            Assert.Equal(invoice.Total, invoiceFromRepository.Total);
            // ENDSAMPLE
        }

        [Fact]
        public void CanStoreAndHydrateAggregatePreviousVersion()
        {
            var repository = new AggregateRepository(theStore);

            var invoice = CreateInvoice();

            repository.Store(invoice);

            // SAMPLE: scenario-aggregate-versionedload
            var invoiceFromRepository = repository.Load<Invoice>(invoice.Id, 2);

            Assert.Equal(124, invoiceFromRepository.Total);
            // ENDSAMPLE
        }

        [Fact]
        public void CanGuardVersion()
        {
            var repository = new AggregateRepository(theStore);

            // SAMPLE: scenario-aggregate-conflict
            var invoice = CreateInvoice();
            var invoiceWithSameIdentity = CreateInvoice();

            repository.Store(invoice);

            Assert.Throws<EventStreamUnexpectedMaxEventIdException>(() =>
            {
                repository.Store(invoiceWithSameIdentity);
            });
            // ENDSAMPLE
        }

        private static Invoice CreateInvoice()
        {
            // SAMPLE: scenario-aggregate-createinvoice
            var invoice = new Invoice(42);

            invoice.AddLine(100, 24, "Joo Janta 200 Super-Chromatic Peril Sensitive Sunglasses");
            invoice.AddLine(200, 16, "Happy Vertical People Transporter");
            // ENDSAMPLE

            return invoice;
        }
    }

    // SAMPLE: scenario-aggregate-invoice
    public sealed class Invoice : AggregateBase
    {
        public Invoice(int invoiceNumber) : this()
        {
            if (invoiceNumber <= 0)
            {
                throw new ArgumentException("Invoice number needs to be positive", nameof(invoiceNumber));
            }

            // Instantiation creates our initial event, capturing the invoice number
            RaiseEvent(new InvoiceCreated(invoiceNumber));
        }

        // Enforce any contracts on input, then raise event caputring the data
        public void AddLine(decimal price, decimal vat, string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                throw new ArgumentException("Description cannot be empty", nameof(description));
            }

            RaiseEvent(new LineItemAdded(price, vat, description));
        }

        public override string ToString()
        {
            var lineItems = string.Join(Environment.NewLine, lines.Select(x => $"{x.Item1}: {x.Item2} ({x.Item3}% VAT)"));
            return $"{lineItems}{Environment.NewLine}Total: {Total}";
        }

        public decimal Total { get; private set; }
        private readonly List<Tuple<string, decimal, decimal>> lines = new List<Tuple<string, decimal, decimal>>();

        private Invoice()
        {
            // Register the event types that make up our aggregate , together with their respective handlers
            Register<InvoiceCreated>(Apply);
            Register<LineItemAdded>(Apply);            
        }

        // Apply the deltas to mutate our state
        private void Apply(InvoiceCreated @event)
        {
            Id = @event.InvoiceNumber.ToString(CultureInfo.InvariantCulture);
        }

        // Apply the deltas to mutate our state
        private void Apply(LineItemAdded @event)
        {
            var price = @event.Price * (1 + @event.Vat / 100);
            Total += price;
            lines.Add(Tuple.Create(@event.Description, price, @event.Vat));
        }
    }
    // ENDSAMPLE

    // SAMPLE: scenario-aggregate-events
    public sealed class InvoiceCreated
    {
        public readonly int InvoiceNumber;

        public InvoiceCreated(int invoiceNumber)
        {
            InvoiceNumber = invoiceNumber;
        }
    }
    
    public sealed class LineItemAdded
    {
        public readonly decimal Price;
        public readonly decimal Vat;
        public readonly string Description;

        public LineItemAdded(decimal price, decimal vat, string description)
        {
            Price = price;
            Vat = vat;
            Description = description;
        }
    }
    // ENDSAMPLE

    // SAMPLE: scenario-aggregate-base
    // Infrastructure to capture modifications to state in events
    public abstract class AggregateBase
    {
        // For indexing our event streams
        public string Id { get; protected set; }
        // For protecting the state, i.e. conflict prevention
        public int Version { get; protected set; }

        private readonly List<object> uncommittedEvents = new List<object>();
        private readonly Dictionary<Type, Action<object>> handlers = new Dictionary<Type, Action<object>>();
        
        // Get the deltas, i.e. events that make up the state, not yet persisted
        public IEnumerable<object> GetUncommittedEvents()
        {
            return uncommittedEvents;
        }

        // Mark the deltas as persisted.
        public void ClearUncommittedEvents()
        {
            uncommittedEvents.Clear();            
        }

        // Infrastructure for raising events & registering handlers

        protected void Register<T>(Action<T> handle)
        {
            handlers[typeof(T)] = e => handle((T)e);
        } 

        protected void RaiseEvent(object @event)
        {
            ApplyEvent(@event);
            uncommittedEvents.Add(@event);
        }

        private void ApplyEvent(object @event)
        {
            handlers[@event.GetType()](@event);
            // Each event bumps our version
            Version++;
        }
    }
    // ENDSAMPLE

    // SAMPLE: scenario-aggregate-repository
    public sealed class AggregateRepository
    {
        private readonly IDocumentStore store;

        public AggregateRepository(IDocumentStore store)
        {
            this.store = store;
        }

        public void Store(AggregateBase aggregate)
        {
            using (var session = store.OpenSession())
            {
                // Take non-persisted events, push them to the event stream, indexed by the aggregate ID
                var events = aggregate.GetUncommittedEvents().ToArray();
                session.Events.Append(aggregate.Id, aggregate.Version, events);
                session.SaveChanges();
            }
            // Once succesfully persisted, clear events from list of uncommitted events
            aggregate.ClearUncommittedEvents();            
        }        

        private static readonly MethodInfo ApplyEvent = typeof(AggregateBase).GetMethod("ApplyEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        public T Load<T>(string id, int? version = null) where T : AggregateBase
        {
            IReadOnlyList<IEvent> events;
            using (var session = store.LightweightSession())
            {
                events = session.Events.FetchStream(id, version ?? 0);                
            }

            if (events != null && events.Any())
            {                                
                var instance = Activator.CreateInstance(typeof(T), true);                
                // Replay our aggregate state from the event stream
                events.Aggregate(instance, (o, @event) => ApplyEvent.Invoke(instance, new [] { @event.Data }));
                return (T)instance;
            }

            throw new InvalidOperationException($"No aggregate by id {id}.");
        }
    }
    // ENDSAMPLE
}