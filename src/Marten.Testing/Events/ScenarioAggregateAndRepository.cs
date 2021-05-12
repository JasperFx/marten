using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class ScenarioAggregateAndRepository: IntegrationContext
    {
        public ScenarioAggregateAndRepository(DefaultStoreFixture fixture) : base(fixture)
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

            #region sample_scenario-aggregate-storeandreadinvoice
            var repository = new AggregateRepository(theStore);

            repository.Store(invoice);

            var invoiceFromRepository = repository.Load<Invoice>(invoice.Id);

            Assert.Equal(invoice.ToString(), invoiceFromRepository.ToString());
            Assert.Equal(invoice.Total, invoiceFromRepository.Total);
            #endregion sample_scenario-aggregate-storeandreadinvoice
        }

        [Fact]
        public void CanStoreAndHydrateAggregatePreviousVersion()
        {
            var repository = new AggregateRepository(theStore);

            var invoice = CreateInvoice();

            repository.Store(invoice);

            #region sample_scenario-aggregate-versionedload
            var invoiceFromRepository = repository.Load<Invoice>(invoice.Id, 2);

            Assert.Equal(124, invoiceFromRepository.Total);
            #endregion sample_scenario-aggregate-versionedload
        }

        [Fact]
        public void CanGuardVersion()
        {
            var repository = new AggregateRepository(theStore);

            #region sample_scenario-aggregate-conflict
            var invoice = CreateInvoice();
            var invoiceWithSameIdentity = CreateInvoice();

            repository.Store(invoice);

            Assert.Throws<EventStreamUnexpectedMaxEventIdException>(() =>
            {
                repository.Store(invoiceWithSameIdentity);
            });
            #endregion sample_scenario-aggregate-conflict
        }

        [Fact]
        public void CanRetrieveVersion()
        {
            var repository = new AggregateRepository(theStore);

            var invoice = CreateInvoice();
            invoice.Version.ShouldBe(3);
            repository.Store(invoice);

            // Assert version was incremented properly
            var invoiceFromRepository = repository.Load<Invoice>(invoice.Id);
            invoiceFromRepository.Version.ShouldBe(3);

            // Update aggregate
            invoiceFromRepository.AddLine(100, 23, "Some nice product with 23% VAT");
            repository.Store(invoiceFromRepository);

            // Assert version was incremented properly
            invoiceFromRepository = repository.Load<Invoice>(invoice.Id);
            invoiceFromRepository.Version.ShouldBe(4);
        }

        private static Invoice CreateInvoice()
        {
            #region sample_scenario-aggregate-createinvoice
            var invoice = new Invoice(42);

            invoice.AddLine(100, 24, "Joo Janta 200 Super-Chromatic Peril Sensitive Sunglasses");
            invoice.AddLine(200, 16, "Happy Vertical People Transporter");
            #endregion sample_scenario-aggregate-createinvoice

            return invoice;
        }
    }

    #region sample_scenario-aggregate-invoice
    public sealed class Invoice: AggregateBase
    {
        public Invoice(int invoiceNumber)
        {
            if (invoiceNumber <= 0)
            {
                throw new ArgumentException("Invoice number needs to be positive", nameof(invoiceNumber));
            }

            // Instantiation creates our initial event, capturing the invoice number
            var @event = new InvoiceCreated(invoiceNumber);

            // Call Apply to mutate state of aggregate based on event
            Apply(@event);

            // Add the event to uncommitted events to use it while persisting the events to Marten events store
            AddUncommittedEvent(@event);
        }

        private Invoice()
        {
        }

        // Enforce any contracts on input, then raise event capturing the data
        public void AddLine(decimal price, decimal vat, string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                throw new ArgumentException("Description cannot be empty", nameof(description));
            }

            var @event = new LineItemAdded(price, vat, description);

            // Call Apply to mutate state of aggregate based on event
            Apply(@event);

            // Add the event to uncommitted events to use it while persisting the events to Marten events store
            AddUncommittedEvent(@event);
        }

        public override string ToString()
        {
            var lineItems = string.Join(Environment.NewLine, lines.Select(x => $"{x.Item1}: {x.Item2} ({x.Item3}% VAT)"));
            return $"{lineItems}{Environment.NewLine}Total: {Total}";
        }

        public decimal Total { get; private set; }

        private readonly List<Tuple<string, decimal, decimal>> lines = new List<Tuple<string, decimal, decimal>>();

        // Apply the deltas to mutate our state
        private void Apply(InvoiceCreated @event)
        {
            Id = @event.InvoiceNumber.ToString(CultureInfo.InvariantCulture);

            // Ensure to update version on every Apply method.
            Version++;
        }

        // Apply the deltas to mutate our state
        private void Apply(LineItemAdded @event)
        {
            var price = @event.Price * (1 + @event.Vat / 100);
            Total += price;
            lines.Add(Tuple.Create(@event.Description, price, @event.Vat));

            // Ensure to update version on every Apply method.
            Version++;
        }
    }

    #endregion sample_scenario-aggregate-invoice

    #region sample_scenario-aggregate-events
    public sealed class InvoiceCreated
    {
        public int InvoiceNumber { get; }

        public InvoiceCreated(int invoiceNumber)
        {
            InvoiceNumber = invoiceNumber;
        }
    }

    public sealed class LineItemAdded
    {
        public decimal Price { get; }
        public decimal Vat { get; }
        public string Description { get; }

        public LineItemAdded(decimal price, decimal vat, string description)
        {
            Price = price;
            Vat = vat;
            Description = description;
        }
    }

    #endregion sample_scenario-aggregate-events

    #region sample_scenario-aggregate-base
    // Infrastructure to capture modifications to state in events
    public abstract class AggregateBase
    {
        // For indexing our event streams
        public string Id { get; protected set; }

        // For protecting the state, i.e. conflict prevention
        // The setter is only public for setting up test conditions
        public long Version { get; set; }

        // JsonIgnore - for making sure that it won't be stored in inline projection
        [JsonIgnore]
        private readonly List<object> _uncommittedEvents = new List<object>();

        // Get the deltas, i.e. events that make up the state, not yet persisted
        public IEnumerable<object> GetUncommittedEvents()
        {
            return _uncommittedEvents;
        }

        // Mark the deltas as persisted.
        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
        }

        protected void AddUncommittedEvent(object @event)
        {
            // add the event to the uncommitted list
            _uncommittedEvents.Add(@event);
        }
    }

    #endregion sample_scenario-aggregate-base

    #region sample_scenario-aggregate-repository
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
            // Once successfully persisted, clear events from list of uncommitted events
            aggregate.ClearUncommittedEvents();
        }

        public T Load<T>(string id, int? version = null) where T : AggregateBase
        {
            using (var session = store.LightweightSession())
            {
                var aggregate = session.Events.AggregateStream<T>(id, version ?? 0);
                return aggregate ?? throw new InvalidOperationException($"No aggregate by id {id}.");
            }
        }
    }

    #endregion sample_scenario-aggregate-repository
}
